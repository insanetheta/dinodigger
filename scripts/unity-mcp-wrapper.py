#!/usr/bin/env python3
"""
unity-mcp-wrapper.py (v2 — self-healing)

Sits between Claude Code and the Unity AI Assistant relay (`relay --mcp`).
Claude Code spawns this wrapper once; the wrapper manages relay child processes
and keeps the MCP connection alive across Unity domain reloads.

Verified relay facts this design relies on (probed against 2.9.0-pre.2):
- A fresh relay serves requests WITHOUT an `initialize` handshake (returned the
  full 40-tool tools/list uninitialized), so relay restarts are protocol-
  transparent and no handshake replay is needed.
- The relay exits or hangs when the in-editor bridge socket disappears during a
  domain reload; it reconnects by itself when restarted after the bridge is back.

Failure modes fixed vs v1:
1. v1's stdin-forward thread dropped the line it was carrying when the relay
   died (broken pipe swallowed) — requests vanished, Claude Code hung.
   v2 uses a single stdin reader feeding a queue; lines are only dequeued after
   a successful write to the relay, so nothing is ever lost.
2. v1 never answered requests that died with the relay — Claude Code timed out
   and marked the server failed (manual /mcp).
   v2 tracks pending request ids and, when the relay dies, immediately emits a
   JSON-RPC error response for each, so the client sees a retryable tool error
   instead of a dead connection.
3. v1 could not detect a HUNG relay (process alive, bridge gone) — calls hung
   forever. v2 has a watchdog: any request pending longer than WATCHDOG_SECS
   gets the relay killed (which triggers the restart + pending-error path).
"""

import glob
import json
import os
import platform
import queue
import subprocess
import sys
import threading
import time
import zipfile

UNITY_PROJECT = os.path.join(os.path.dirname(__file__), "..")
PKG_CACHE = os.path.join(UNITY_PROJECT, "Library", "PackageCache")
RESTART_DELAY = 1.0     # seconds before relaunching a dead relay
WATCHDOG_SECS = 45.0    # max time a request may stay unanswered before the relay is killed


def log(msg):
    sys.stderr.write(f"[unity-mcp-wrapper] {msg}\n")
    sys.stderr.flush()


def find_relay_binary():
    """Locate the relay binary (pre-2.3 .app bundle, 2.3+ zip, or plain binary)."""
    arch = platform.machine().lower()
    system = platform.system().lower()

    if system == "darwin":
        zip_name = "relay_mac_arm64" if arch == "arm64" else "relay_mac_x64"
        app_name = zip_name + ".app"
    elif system == "linux":
        zip_name = "relay_linux"
        app_name = None
    else:
        zip_name = "relay_win.exe"
        app_name = None

    pattern = os.path.join(PKG_CACHE, "com.unity.ai.assistant@*", "RelayApp~")
    matches = sorted(glob.glob(pattern))
    if not matches:
        log(f"No com.unity.ai.assistant package found in {PKG_CACHE}")
        return None

    relay_dir = matches[-1]

    if app_name:
        old_style = os.path.join(relay_dir, app_name, "Contents", "MacOS", zip_name)
        if os.path.isfile(old_style):
            log(f"Found old-style relay at {old_style}")
            return old_style

    zip_path = os.path.join(relay_dir, zip_name)
    if not os.path.isfile(zip_path):
        log(f"Relay zip not found: {zip_path}")
        return None

    if not zipfile.is_zipfile(zip_path):
        os.chmod(zip_path, 0o755)
        log(f"Found plain relay binary at {zip_path}")
        return zip_path

    extract_dir = zip_path + "_extracted"
    if app_name:
        binary_path = os.path.join(extract_dir, app_name, "Contents", "MacOS", zip_name)
    else:
        binary_path = os.path.join(extract_dir, zip_name)

    if not os.path.isfile(binary_path):
        log(f"Extracting relay from {zip_path} ...")
        os.makedirs(extract_dir, exist_ok=True)
        with zipfile.ZipFile(zip_path, "r") as zf:
            zf.extractall(extract_dir)
        log("Extraction complete")

    if not os.path.isfile(binary_path):
        log(f"Binary not found after extraction: {binary_path}")
        return None

    os.chmod(binary_path, 0o755)
    log(f"Found relay binary at {binary_path}")
    return binary_path


class Wrapper:
    def __init__(self, relay_cmd):
        self.relay_cmd = relay_cmd
        self.inbox = queue.Queue()          # lines from Claude Code, never dropped
        self.proc = None
        self.pending = {}                   # request id -> (line, first_sent_monotonic)
        self.pending_lock = threading.Lock()
        self.stdout_lock = threading.Lock()
        self.client_eof = False

    # ---- Claude Code stdin -> inbox (single reader for the wrapper's lifetime)
    def stdin_reader(self):
        try:
            for line in sys.stdin.buffer:
                self.inbox.put(line)
        except (OSError, ValueError):
            pass
        self.client_eof = True
        self.inbox.put(None)  # wake the writer so it can notice EOF

    def emit_to_client(self, raw_bytes):
        with self.stdout_lock:
            sys.stdout.buffer.write(raw_bytes)
            sys.stdout.buffer.flush()

    def synth_error(self, req_id):
        resp = {"jsonrpc": "2.0", "id": req_id,
                "error": {"code": -32000,
                          "message": "Unity relay restarted (likely a domain reload). The tool call was lost — please retry."}}
        self.emit_to_client((json.dumps(resp) + "\n").encode())

    def fail_pending(self):
        with self.pending_lock:
            dead = list(self.pending.keys())
            self.pending.clear()
        for rid in dead:
            log(f"answering lost request id={rid} with retryable error")
            self.synth_error(rid)

    def track_outgoing(self, line):
        try:
            obj = json.loads(line)
            if isinstance(obj, dict) and "method" in obj and "id" in obj:
                with self.pending_lock:
                    self.pending[obj["id"]] = time.monotonic()
        except (json.JSONDecodeError, UnicodeDecodeError):
            pass

    def clear_pending_for(self, line):
        try:
            obj = json.loads(line)
            if isinstance(obj, dict) and "id" in obj and "method" not in obj:
                with self.pending_lock:
                    self.pending.pop(obj["id"], None)
        except (json.JSONDecodeError, UnicodeDecodeError):
            pass

    # ---- inbox -> relay stdin. Requeues the line if the write fails.
    def writer_loop(self, proc, relay_gone):
        while not relay_gone.is_set():
            try:
                line = self.inbox.get(timeout=0.25)
            except queue.Empty:
                continue
            if line is None:        # client EOF marker
                return
            try:
                proc.stdin.write(line)
                proc.stdin.flush()
                self.track_outgoing(line)
            except (OSError, BrokenPipeError, ValueError):
                # relay died mid-write: requeue THIS line for the next relay
                self.inbox.put(line)
                return

    # ---- watchdog: kill the relay if any request is unanswered too long
    def watchdog_loop(self, proc, relay_gone):
        while not relay_gone.is_set():
            time.sleep(2.0)
            now = time.monotonic()
            with self.pending_lock:
                stale = [rid for rid, t0 in self.pending.items() if now - t0 > WATCHDOG_SECS]
            if stale:
                log(f"watchdog: request(s) {stale} pending > {WATCHDOG_SECS}s — killing hung relay")
                try:
                    proc.kill()
                except OSError:
                    pass
                return

    def run(self):
        threading.Thread(target=self.stdin_reader, daemon=True).start()
        restarts = 0
        while not self.client_eof:
            try:
                proc = subprocess.Popen(
                    self.relay_cmd,
                    stdin=subprocess.PIPE,
                    stdout=subprocess.PIPE,
                    stderr=sys.stderr,
                )
            except Exception as e:
                log(f"Failed to launch relay: {e} — retrying in {RESTART_DELAY}s")
                time.sleep(RESTART_DELAY)
                continue

            self.proc = proc
            restarts += 1
            log(f"Relay started (pid {proc.pid}, launch #{restarts})")

            relay_gone = threading.Event()
            threading.Thread(target=self.writer_loop, args=(proc, relay_gone), daemon=True).start()
            threading.Thread(target=self.watchdog_loop, args=(proc, relay_gone), daemon=True).start()

            # relay stdout -> Claude Code stdout (blocks until relay exits)
            try:
                for line in proc.stdout:
                    self.clear_pending_for(line)
                    self.emit_to_client(line)
            except (OSError, BrokenPipeError):
                pass

            relay_gone.set()
            proc.wait()
            log(f"Relay exited (code {proc.returncode})")
            # answer anything that died with the relay so Claude Code's call
            # fails fast-and-retryable instead of timing out the whole server
            self.fail_pending()
            time.sleep(RESTART_DELAY)
        log("Client EOF — exiting")


def main():
    log("Starting (wrapper v2: queue + pending-error + watchdog)")
    relay_binary = find_relay_binary()
    if not relay_binary:
        log("FATAL: Could not locate relay binary. Is Unity AI Assistant installed?")
        sys.exit(1)
    # Pin the relay to THIS project's editor instance — without this the relay attaches to
    # whichever Unity bridge it discovers first (verified misroute: it connected to another
    # open project's editor when this project's editor was closed).
    project_path = os.path.realpath(UNITY_PROJECT)
    log(f"Pinning relay to project: {project_path}")
    Wrapper([relay_binary, "--mcp", "--project-path", project_path]).run()


if __name__ == "__main__":
    main()

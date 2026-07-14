using UnityEngine;

namespace DinoDigger.Core
{
    /// <summary>Eight-way compass facing, ordered to match sprite arrays.</summary>
    public enum Dir8
    {
        N = 0,
        NE = 1,
        E = 2,
        SE = 3,
        S = 4,
        SW = 5,
        W = 6,
        NW = 7
    }

    /// <summary>
    /// Utility that maps a movement vector to one of 8 facings and back.
    ///
    /// COORDINATE / SPRITE CONVENTION (verified against GeneratedArtImporter):
    /// the world matches the on-screen compass 1:1 — world +Y is screen-UP = N
    /// (the character's BACK view), world -Y is screen-DOWN = S (the FRONT view that
    /// faces the camera), world +X is screen-RIGHT = E, world -X is screen-LEFT = W.
    /// Sprites were generated with front = S, and the importer fills the arrays in
    /// Dir8 order (N,NE,E,SE,S,SW,W,NW) using the SAME compass names, so a world
    /// movement of (0,-1) must select S, (1,0) E, (0,1) N, (-1,0) W.
    ///
    /// The angle is measured CLOCKWISE from +Y and the 8 sectors are CENTERED on the
    /// compass points (RoundToInt puts the sector boundaries at ±22.5° off each
    /// point), so a heading is never biased one direction — this is deliberately NOT
    /// edge-aligned.
    /// </summary>
    public static class Direction8
    {
        /// <summary>
        /// Convert a (world-space) movement vector into a Dir8. Returns the
        /// supplied fallback when the vector is effectively zero-length.
        /// </summary>
        public static Dir8 FromVector(Vector2 v, Dir8 fallback = Dir8.S)
        {
            if (v.sqrMagnitude < 0.0001f)
            {
                return fallback;
            }

            // Clockwise degrees from north (+Y). Atan2(x, y): x east, y north.
            // (0,-1) -> 180° -> S; (1,0) -> 90° -> E; (0,1) -> 0° -> N; (-1,0) -> -90° -> W.
            float deg = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
            int index = Mathf.RoundToInt(deg / 45f); // sectors centered on compass points
            index = ((index % 8) + 8) % 8; // wrap into 0..7
            return (Dir8)index;
        }

        /// <summary>A unit vector pointing in the given facing (screen-ish space).</summary>
        public static Vector2 ToVector(Dir8 dir)
        {
            float deg = (int)dir * 45f;
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        /// <summary>Safe indexer into an 8-length sprite array.</summary>
        public static Sprite Pick(Sprite[] sprites, Dir8 dir, Sprite fallback = null)
        {
            if (sprites == null || sprites.Length == 0)
            {
                return fallback;
            }

            int i = (int)dir;
            if (i < 0 || i >= sprites.Length || sprites[i] == null)
            {
                return sprites[0] != null ? sprites[0] : fallback;
            }

            return sprites[i];
        }
    }

    /// <summary>
    /// Shared velocity-smoothing + hysteresis for 8-way facing. Both the backhoe and
    /// the dinos drive their sprite facing through one of these so the fix lives in a
    /// single place. It solves the "jiggle every frame" seizure bug three ways:
    ///
    ///   (a) SMOOTHING  — the heading is an exponential moving average of the movement
    ///       deltas over ~<see cref="DefaultSmoothTime"/>s, so a single noisy frame can
    ///       never snap the facing.
    ///   (b) HYSTERESIS — the current facing's 45° sector is widened by
    ///       <see cref="DefaultHysteresisDeg"/>°; the facing only switches once the
    ///       smoothed heading crosses that widened boundary, so a heading hovering on a
    ///       sector edge stays put instead of flapping between neighbours.
    ///   (c) DEADBAND   — deltas below <see cref="DefaultMinSpeed"/> (world units/sec)
    ///       are ignored entirely, so idle micro-motion never changes facing.
    ///
    /// It is a struct (no allocation, WebGL-safe). Reset it when teleporting.
    /// </summary>
    public struct FacingSmoother
    {
        public const float DefaultSmoothTime = 0.15f;   // seconds of EMA over headings
        public const float DefaultHysteresisDeg = 11f;  // extra ° past a boundary before switching
        public const float DefaultMinSpeed = 0.15f;     // world units/sec below which motion is ignored

        private Vector2 _heading; // smoothed heading (EMA of movement direction)
        private Dir8 _facing;
        private bool _ready;

        public Dir8 Facing => _facing;

        /// <summary>Snap the smoother to a known facing (call on spawn / teleport).</summary>
        public void Reset(Dir8 facing)
        {
            _facing = facing;
            _heading = Direction8.ToVector(facing);
            _ready = true;
        }

        /// <summary>
        /// Feed one frame of movement (world-space delta this frame) and get the
        /// smoothed, hysteresis-stabilised facing back.
        /// </summary>
        public Dir8 Tick(Vector2 delta, float dt,
            float minSpeed = DefaultMinSpeed,
            float smoothTime = DefaultSmoothTime,
            float hysteresisDeg = DefaultHysteresisDeg)
        {
            if (!_ready)
            {
                Reset(_facing);
            }

            if (dt <= 0f)
            {
                return _facing;
            }

            float dist = delta.magnitude;
            if (dist < minSpeed * dt)
            {
                return _facing; // deadband: idle micro-motion holds the facing
            }

            Vector2 dir = delta / dist;
            float blend = smoothTime <= 0f ? 1f : 1f - Mathf.Exp(-dt / smoothTime);
            _heading = Vector2.Lerp(_heading, dir, blend);
            if (_heading.sqrMagnitude < 1e-6f)
            {
                return _facing; // heading passing through zero (a reversal): hold a frame
            }

            _facing = ResolveWithHysteresis(_heading, _facing, hysteresisDeg);
            return _facing;
        }

        private static Dir8 ResolveWithHysteresis(Vector2 heading, Dir8 current, float hysteresisDeg)
        {
            float headingDeg = Mathf.Atan2(heading.x, heading.y) * Mathf.Rad2Deg;
            float currentDeg = (int)current * 45f;

            // Keep the current facing while the smoothed heading stays within its own
            // sector (half-width 22.5°) widened by the hysteresis margin. Only switch
            // once it moves clearly INTO a neighbouring sector.
            if (Mathf.Abs(Mathf.DeltaAngle(headingDeg, currentDeg)) <= 22.5f + hysteresisDeg)
            {
                return current;
            }

            return Direction8.FromVector(heading, current);
        }
    }
}

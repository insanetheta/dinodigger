using System.Collections;
using UnityEngine;
using DinoDigger.Dig;
using DinoDigger.Managers;

namespace DinoDigger.Testing
{
    /// <summary>
    /// DIG AUDIO integration case (epic DinoDigger-7c4). Lives in its own file so the audio
    /// pass and the concurrent dig work never touch the same lines; registered from
    /// IntegrationTestCases.BuildCases like every other case.
    ///
    /// WHAT THIS CAN AND CANNOT PROVE. Nothing here listens to anything — a headless-ish
    /// editor run has no ears, and asserting on AudioSource.isPlaying would only prove Unity
    /// started a voice, which is not the thing that breaks. What breaks is WIRING: a hook that
    /// stops reaching the service after a refactor, or a mute that stops being honoured. So the
    /// case asserts on AudioManager's own call counters:
    ///
    ///   1. a REAL bite, tapped through the real input pipeline, reaches the service on the Dig
    ///      bus (this is the half that would catch an unwired hook), and
    ///   2. the reward and cascade hooks reach it too, and
    ///   3. MUTE SUPPRESSES ALL OF IT — including the looping party vamp, which is the one
    ///      sound that could otherwise keep running under a muted game.
    ///
    /// The counters deliberately tick even when a clip is null, so the case does not silently
    /// pass-or-fail on whether the audio import has been run in this editor session.
    /// </summary>
    public partial class IntegrationTestRunner
    {
        private IEnumerator Case_AudioHooksFire(TestContext ctx)
        {
            AudioManager audio = ctx.GM != null ? ctx.GM.Audio : null;
            ctx.Assert(audio != null, "GameManager has no AudioManager — audio never initialised");

            // Mute is PERSISTED in PlayerPrefs, so a case that leaves it flipped silences every
            // later case AND every later run of the day. Captured here, restored in the finally.
            bool wasMuted = audio.Muted;

            try
            {
                audio.SetMuted(false);

                yield return EnterDig(ctx);
                DigModeController dm = ctx.GM.TestDigMode;

                // ---- (1) A REAL BITE REACHES THE SERVICE. ----
                // Tapped through the same pipeline a child uses, not by calling the hook: this
                // is the assertion that catches a dig hook quietly coming unwired.
                DirtTile tile = FindPlainTile(dm);
                ctx.Assert(tile != null, "no plain (unburied) dirt tile found to bite");
                tile.TestSetMaxHealth(3);   // guarantee the first hit CRACKS rather than destroys

                yield return ctx.WaitUntil(() => (dm.TestArmReady && !tile.IsFalling) || tile.IsDestroyed,
                    15f, "the dig arm never parked, so no bite could be timed");
                ctx.Assert(!tile.IsDestroyed, "the test tile was destroyed before the bite");

                int before = tile.TestDamage;
                audio.TestResetCounters();
                ctx.TapWorld(tile.transform.position);
                yield return ctx.WaitUntil(() => tile.TestDamage > before || tile.IsDestroyed,
                    15f, "the tapped tile never took damage");

                ctx.Assert(audio.TestCategoryCounts[(int)SfxCategory.Dig] >= 1,
                    "a real bite cracked a tile but nothing reached the audio service on the Dig " +
                    $"bus (total plays this bite: {audio.TestPlayCount}) — a dig hook is unwired");

                // ---- (2) THE REWARD AND CASCADE HOOKS REACH IT TOO. ----
                // Driven directly: a crystal blob and a ten-tile cascade are expensive to author
                // and are already covered by their own cases. What is unproven, and cheap to
                // prove, is that these two hooks land on the buses the mix expects.
                audio.TestResetCounters();
                audio.CrystalPop(false);
                audio.LandingThump();

                ctx.Assert(audio.TestPlayCount == 2,
                    $"expected 2 one-shots from the pop + thump hooks, got {audio.TestPlayCount}");
                ctx.Assert(audio.TestCategoryCounts[(int)SfxCategory.Reward] == 1,
                    "the crystal pop did not land on the Reward bus");
                ctx.Assert(audio.TestCategoryCounts[(int)SfxCategory.Dig] == 1,
                    "the cascade landing thump did not land on the Dig bus");

                // ---- (3) MUTE SUPPRESSES EVERYTHING. ----
                // The parent gate's whole promise. Enforced at the service, so it must hold for
                // hooks called directly AND for a real tap.
                audio.SetMuted(true);
                audio.TestResetCounters();

                audio.TileCrack();
                audio.CrystalPop(true);
                audio.LandingThump();

                ctx.Assert(audio.TestPlayCount == 0,
                    $"muted, but {audio.TestPlayCount} one-shot(s) still played");
                ctx.Assert(audio.TestSuppressedCount == 3,
                    $"expected 3 suppressed one-shots while muted, counted {audio.TestSuppressedCount}");

                // A muted party must not start a LOOP — a one-shot that slips through is a blip,
                // a loop that slips through plays until the party timer ends.
                audio.StartDanceLoop();
                ctx.Assert(!audio.DanceLoopPlaying,
                    "the dance party vamp started while the game was muted");

                // And the real pipeline stays silent too.
                DirtTile second = FindPlainTile(dm);
                if (second != null)
                {
                    yield return ctx.WaitUntil(() => dm.TestArmReady || second.IsDestroyed,
                        15f, "the dig arm never parked for the muted bite");
                    audio.TestResetCounters();
                    ctx.TapWorld(second.transform.position);
                    yield return ctx.WaitFrames(10);

                    ctx.Assert(audio.TestPlayCount == 0,
                        $"a real tap played {audio.TestPlayCount} one-shot(s) while muted");
                }

                ctx.Log($"bite reached the Dig bus; pop/thump routed; mute suppressed " +
                        $"{audio.TestSuppressedCount}+ plays and blocked the party loop");
            }
            finally
            {
                audio.StopDanceLoop();
                audio.SetMuted(wasMuted);
                audio.TestResetCounters();
            }
        }
    }
}

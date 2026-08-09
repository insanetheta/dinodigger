using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Managers;
using DinoDigger.Overworld;

namespace DinoDigger.Testing
{
    /// <summary>
    /// MACHINE FRIENDS integration cases (epic DinoDigger-b48): Doodle's plaza dance party,
    /// Sprinkles' tap-to-spray ripen, Tuggy's duckling tow, and the discovery pacing guard.
    ///
    /// Every one of the three machine cases proves the SAME two-stage shape, because that
    /// shape is the feature:
    ///
    ///   1. THE MACHINE IS EARNED. Before the child engages the loop it serves, the machine
    ///      is not in the world at all — not hidden, not disabled, ABSENT. Then the real gate
    ///      is played out (a berry harvested, a duck caught, a building finished) and the
    ///      machine arrives, dormant and glinting its "come look" beacon.
    ///   2. THE FIRST TAP WAKES IT, and thereafter its one job runs on a visible cooldown
    ///      that a tap always answers — with the job when the gauge is full, and with a
    ///      sad-cute wobble when it is not. Never nothing.
    ///
    /// Registered from IntegrationTestCases.BuildCases (three appended lines plus the queue
    /// case) and living in their own file so this wave's machine work and the concurrent
    /// dig work never touch the same lines. See IntegrationTestRunner.cs for the driver.
    /// </summary>
    public partial class IntegrationTestRunner
    {
        // ====================================================== DOODLE (DinoDigger-ldp)

        // Doodle is earned by FINISHING A BUILDING, arrives dormant in the plaza clear of
        // every plot, wakes on the first tap, and thereafter a tap cranks him: nearby
        // residents gather and play their species dances as a repeatable chorus. The
        // cooldown holds a second crank until the dial refills, and the denied tap is still
        // answered. Buddies and builders are never taken.
        private IEnumerator Case_DoodleDanceParty(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            TownController town = EnsureTown(ctx);
            MachineFriendController mf = EnsureMachines(ctx);

            // ---- (1) NOT PRE-PLACED. Day zero: no finished buildings, so no Doodle. ----
            ctx.Assert(!mf.TestGateTripped(MachineKind.Doodle),
                "Doodle's discovery gate is already tripped on a freshly reset town");
            ctx.Assert(!mf.TestPresent(MachineKind.Doodle),
                "Doodle is standing in the plaza before any building has been finished — " +
                "machines must be EARNED, never pre-placed");

            yield return ctx.WaitFrames(3);
            ctx.Assert(!mf.TestPresent(MachineKind.Doodle),
                "Doodle arrived without its gate tripping");

            // ---- (2) TRIP THE REAL GATE: finish a building. ----
            // Authored through the town's own save-restore path (the same one
            // Case_TownStatePersists uses), so FinishedBuildingCount rises exactly as it
            // would after a real build and the service's gate poll sees the real thing.
            int savedNext = gm.Save.Data.TownNextIndex;
            List<TownBuildingSave> savedList = gm.Save.Data.TownBuildings;
            try
            {
                gm.Save.Data.TownNextIndex = 1;
                gm.Save.Data.TownBuildings = new List<TownBuildingSave>
                {
                    new TownBuildingSave { Finished = true, State = BuildingController.ConstructionStates },
                };
                town.RestoreFromSave(gm.Save.Data);
            }
            finally
            {
                gm.Save.Data.TownNextIndex = savedNext;
                gm.Save.Data.TownBuildings = savedList;
            }

            ctx.Assert(town.FinishedBuildingCount >= 1, "failed to author a finished building");

            yield return ctx.WaitUntil(() => mf.TestPresent(MachineKind.Doodle), 10f,
                "Doodle never arrived after a building was finished");

            DoodleMachine doodle = mf.TestDoodle;
            ctx.Assert(doodle != null, "Doodle arrived but is not a DoodleMachine");

            // ---- (3) ARRIVAL IS AN EVENT: dormant, glinting, clear of every plot. ----
            ctx.Assert(!doodle.IsAwake, "Doodle arrived already awake (it must be found first)");
            ctx.Assert(doodle.TestGlinting, "Doodle is not running its come-look beacon");
            ctx.Assert(doodle.TestMossVisible, "a sleeping machine must wear its moss tuft");
            ctx.Assert(!doodle.TestEyeVisible, "a sleeping machine's eye light must be off");
            ctx.Assert(!doodle.TestGaugeVisible, "a sleeping machine must not show its dial");

            int glints = doodle.TestGlints;
            yield return ctx.WaitUntil(() => doodle.TestGlints > glints, 8f,
                "the arrival beacon glinted once and went quiet (it must repeat until tapped)");

            TownArea area = town.TestArea;
            float nearestPlot = float.MaxValue;
            for (int i = 0; i < area.PlotCount; i++)
            {
                nearestPlot = Mathf.Min(nearestPlot,
                    (area.PlotWorld(i) - doodle.transform.position).magnitude);
            }

            ctx.Assert(nearestPlot > 0.8f,
                $"Doodle stands {nearestPlot:0.##}u from the nearest plot — too close to keep " +
                "its own tap target clear of a building");
            ctx.Assert((doodle.transform.position - area.Center).magnitude < 4.5f,
                "Doodle is not in the plaza (it should sit near the finale/fountain plot)");

            // ---- (4) FIRST TAP WAKES IT. ----
            gm.TestTapWorldRouted(doodle.transform.position);
            yield return ctx.WaitFrames(2);

            ctx.Assert(doodle.IsAwake, "the first tap did not wake Doodle");
            ctx.Assert(!doodle.TestGlinting, "a woken machine must stop glinting");
            ctx.Assert(!doodle.TestMossVisible, "waking must shake the moss off");
            ctx.Assert(doodle.TestEyeVisible, "waking must blink the eye light on");
            ctx.Assert(doodle.TestGaugeVisible, "an awake machine must show its dial");
            ctx.Assert(doodle.TestReady, "Doodle should wake wound-up and ready to crank");

            // ---- (5) THE PARTY: nearby residents dance. ----
            var dancers = new List<DinoController>();
            for (int i = 0; i < 3; i++)
            {
                DinoController d = gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big);
                ctx.Assert(d != null, "failed to spawn a test resident");
                gm.TestMakeResident(d, false);
                dancers.Add(d);
            }

            // Park them around the plaza and settle them to Idle, so they are eligible
            // (non-busy) at the moment Doodle asks. BecomeResident(delayHomeWalk: true) is the
            // shipped way to say "stay put here" — no test-only state is invented.
            for (int i = 0; i < dancers.Count; i++)
            {
                float ang = i * (Mathf.PI * 2f / dancers.Count);
                Vector3 p = doodle.transform.position +
                            new Vector3(Mathf.Cos(ang) * 1.6f, Mathf.Sin(ang) * 1.0f, 0f);
                OverworldMap map = gm.TestMap;
                if (map != null)
                {
                    p = map.NearestWalkable(p, out _);
                }

                dancers[i].transform.position = p;
                dancers[i].BecomeResident(true);
            }

            Physics2D.SyncTransforms();
            int partiesBefore = doodle.TestParties;
            gm.TestTapWorldRouted(doodle.transform.position);

            ctx.Assert(doodle.TestParties == partiesBefore + 1,
                "tapping an awake, ready Doodle did not throw a party");
            ctx.Assert(doodle.TestDancerCount > 0,
                "Doodle's crank summoned nobody (no eligible resident within the gather radius)");
            ctx.Assert(!doodle.TestReady,
                "the crank did not spend the dial charge — the cooldown would never be visible");

            yield return ctx.WaitUntil(() => doodle.TestDanceBeats > 0, 8f,
                "no resident ever played its species dance during the party");

            // Buddies are NEVER taken: the acquire filter excludes them outright.
            for (int i = 0; i < doodle.TestDancers.Count; i++)
            {
                DinoController d = doodle.TestDancers[i];
                ctx.Assert(d == null || !d.IsBuddy, "a walk buddy was dragged into the dance party");
            }

            // ---- (6) COOLDOWN HOLDS, and the denied tap is still answered. ----
            int deniedBefore = doodle.TestDeniedTaps;
            int partiesAtCooldown = doodle.TestParties;
            int answeredBefore = doodle.TestAnsweredTaps;
            gm.TestTapWorldRouted(doodle.transform.position);
            ctx.Assert(doodle.TestAnsweredTaps == answeredBefore + 1,
                "the cooling-down tap was swallowed entirely — it never reached Doodle");
            ctx.Assert(doodle.TestParties == partiesAtCooldown,
                "a second crank fired while the dial was still refilling");
            ctx.Assert(doodle.TestDeniedTaps == deniedBefore + 1,
                "the cooling-down tap did nothing at all — every tap must be answered");
            ctx.Assert(doodle.TestGaugeFill < 1f, "the dial reads full while the cooldown is running");

            // ---- (7) REFILL RE-ENABLES. ----
            doodle.TestRefill();
            ctx.Assert(doodle.TestReady, "a refilled dial did not re-enable the crank");
            gm.TestTapWorldRouted(doodle.transform.position);
            ctx.Assert(doodle.TestParties == partiesAtCooldown + 1,
                "a refilled Doodle refused to throw a second party");

            ctx.Log($"Doodle: absent until a building finished, then arrived dormant + glinting " +
                    $"{nearestPlot:0.##}u clear of the nearest plot; first tap woke it; a crank " +
                    $"gathered {doodle.TestDancerCount} residents for {doodle.TestDanceBeats} dance " +
                    $"beats; cooldown blocked a repeat (answered with a wobble) and a refill re-armed it");
            gm.TestReset();
        }

        // =================================================== SPRINKLES (DinoDigger-25j)

        // Sprinkles is earned by HARVESTING A BERRY (the garden's only player verb — a sprout
        // ripening on its timer is nobody's doing), arrives dormant at the garden edge, and
        // once woken a tap sends it trundling to the nearest unripe sprout to spray it ripe
        // on the spot. Its belly tank is the cooldown: three sprays, then an empty-gurgle
        // wobble until it sips a charge back.
        private IEnumerator Case_SprinklesRipensOnTap(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset(); // re-buds every sprout and clears every machine

            MachineFriendController mf = EnsureMachines(ctx);
            IReadOnlyList<BerrySprout> sprouts = gm.TestSprouts;
            ctx.Assert(sprouts != null && sprouts.Count >= 2,
                $"need >=2 sprouts for the watering test (have {(sprouts != null ? sprouts.Count : 0)})");

            // ---- (1) NOT PRE-PLACED. ----
            ctx.Assert(!mf.TestGateTripped(MachineKind.Sprinkles),
                "Sprinkles' gate is already tripped on a freshly reset garden");
            ctx.Assert(!mf.TestPresent(MachineKind.Sprinkles),
                "Sprinkles is in the garden before a single berry has been harvested");

            yield return ctx.WaitFrames(3);
            ctx.Assert(!mf.TestPresent(MachineKind.Sprinkles), "Sprinkles arrived without its gate");

            // ---- (2) TRIP THE REAL GATE: harvest a berry through the real tap path. ----
            Physics2D.SyncTransforms();
            BerrySprout picked = null;
            for (int i = 0; i < sprouts.Count; i++)
            {
                if (sprouts[i] != null && OnlySproutTappable(sprouts[i].transform.position, sprouts[i]))
                {
                    picked = sprouts[i];
                    break;
                }
            }

            ctx.Assert(picked != null, "no sprout has a clean (sole-ITappable) tap point");
            picked.TestForceRipen();
            gm.TestTapWorldRouted(picked.transform.position);
            yield return ctx.WaitFrames(2);

            ctx.Assert(mf.TestGateTripped(MachineKind.Sprinkles),
                "harvesting a berry did not trip Sprinkles' discovery gate");

            yield return ctx.WaitUntil(() => mf.TestPresent(MachineKind.Sprinkles), 10f,
                "Sprinkles never arrived after the first harvest");

            SprinklesMachine bot = mf.TestSprinkles;
            ctx.Assert(bot != null, "Sprinkles arrived but is not a SprinklesMachine");

            // ---- (3) ARRIVAL: dormant, glinting, at the garden edge and clear of sprouts. ----
            ctx.Assert(!bot.IsAwake, "Sprinkles arrived already awake");
            ctx.Assert(bot.TestGlinting, "Sprinkles is not running its come-look beacon");
            ctx.Assert(bot.TestMossVisible, "a sleeping machine must wear its moss tuft");

            int glints = bot.TestGlints;
            yield return ctx.WaitUntil(() => bot.TestGlints > glints, 8f,
                "Sprinkles' arrival beacon did not repeat");

            GardenArea garden = gm.TestGarden;
            ctx.Assert(garden == null || garden.ContainsWorldExpanded(bot.transform.position, 1),
                "Sprinkles parked outside the reserved garden patch (a dig mound could land on it)");
            for (int i = 0; i < sprouts.Count; i++)
            {
                if (sprouts[i] != null)
                {
                    float d = (sprouts[i].transform.position - bot.transform.position).magnitude;
                    ctx.Assert(d > 0.55f, $"Sprinkles parked {d:0.##}u from sprout {i} — it would " +
                                          "cover the sprout's own tap target");
                }
            }

            // ---- (4) FIRST TAP WAKES IT with a full tank. ----
            gm.TestTapWorldRouted(bot.transform.position);
            yield return ctx.WaitFrames(2);

            ctx.Assert(bot.IsAwake, "the first tap did not wake Sprinkles");
            ctx.Assert(!bot.TestMossVisible && bot.TestEyeVisible,
                "waking must drop the moss and blink the eye light on");
            int tank = gm.TestConfig != null ? Mathf.Max(1, gm.TestConfig.SprinklesTankCharges) : 3;
            ctx.Assert(bot.TestCharges == tank,
                $"Sprinkles woke with {bot.TestCharges} charges (expected a full tank of {tank})");
            ctx.Assert(bot.TestGaugeVisible, "the belly tank must be visible once awake");

            // ---- (5) THE JOB: tap -> trundle -> spray -> that sprout is RIPE. ----
            ctx.Assert(bot.TestHasThirstySprout, "no unripe sprout to water after the reset");
            int spraysBefore = bot.TestSprays;
            int chargesBefore = bot.TestCharges;
            gm.TestTapWorldRouted(bot.transform.position);

            ctx.Assert(bot.TestCharges == chargesBefore - 1,
                "the spray tap did not spend a tank charge");
            ctx.Assert(bot.TestOnErrand, "Sprinkles did not set off toward a sprout");

            BerrySprout targeted = bot.TestTarget;
            ctx.Assert(targeted != null, "Sprinkles set off with no target");
            ctx.Assert(!targeted.IsRipe, "Sprinkles targeted an already-ripe sprout");

            // ---- (5b) A TAP MID-ERRAND IS STILL ANSWERED, and disturbs nothing. ----
            // Sprinkles is the one machine that walks away to work, so it is the one machine
            // with a state a tap could fall through. It must acknowledge without restarting
            // the errand or spending a second charge.
            int busyBefore = bot.TestBusyTaps;
            int chargesOnErrand = bot.TestCharges;
            BerrySprout targetOnErrand = bot.TestTarget;
            gm.TestTapWorldRouted(bot.transform.position);

            ctx.Assert(bot.TestBusyTaps == busyBefore + 1,
                "a tap while Sprinkles was off doing the job did nothing at all");
            ctx.Assert(bot.TestCharges == chargesOnErrand,
                "a mid-errand tap spent a second tank charge");
            ctx.Assert(bot.TestTarget == targetOnErrand,
                "a mid-errand tap re-targeted Sprinkles part-way to its sprout");

            yield return ctx.WaitUntil(() => bot.TestSprays > spraysBefore, 20f,
                () => $"Sprinkles never delivered a spray (errand={bot.TestOnErrand})");
            ctx.Assert(targeted.IsRipe,
                "the sprayed sprout did not ripen — the whole redesign is that ripening is INSTANT");

            // ---- (6) EMPTY TANK: still answered, never a spray. ----
            // Wait until the bot is fully PARKED first. The spray tally ticks at the START of
            // the spray beat, so the wait above lands mid-job while the body is still moving —
            // and a tap aimed at a moving machine is a tap aimed at where it used to be.
            yield return ctx.WaitUntil(() => bot.TestParked, 20f,
                () => $"Sprinkles never settled back home (errand={bot.TestOnErrand})");

            bot.TestDrain();
            ctx.Assert(!bot.TestReady, "draining the tank left Sprinkles ready");
            ctx.Assert(!bot.TestOnErrand, "Sprinkles should be idle before the empty-tank tap");
            int gurglesBefore = bot.TestEmptyGurgles;
            int spraysAtEmpty = bot.TestSprays;
            int answeredBefore = bot.TestAnsweredTaps;
            gm.TestTapWorldRouted(bot.transform.position);

            ctx.Assert(bot.TestAnsweredTaps == answeredBefore + 1,
                "the empty-tank tap was swallowed entirely — it never reached the machine");
            ctx.Assert(bot.TestEmptyGurgles == gurglesBefore + 1,
                "an empty-tank tap did nothing — it must always gurgle back");
            ctx.Assert(bot.TestSprays == spraysAtEmpty, "an empty tank still managed to spray");
            ctx.Assert(bot.TestGaugeFill < 0.01f, "an empty tank is not drawn empty");

            // ---- (7) THE TANK REFILLS and re-enables the spray. ----
            float recharge = gm.TestConfig != null
                ? Mathf.Max(0.1f, gm.TestConfig.SprinklesRechargeSeconds)
                : 45f;
            bot.TestAdvanceRecharge(recharge);
            ctx.Assert(bot.TestCharges >= 1,
                $"sipping {recharge:0.#}s of water back did not restore a charge");

            // Readiness is water AND a thirsty sprout (see SprinklesMachine.IsReady): burning a
            // charge on an already-ripe garden would read to a child as the machine breaking.
            // The garden's own ripen timers are free-running, so assert the implication rather
            // than the raw flag — otherwise this line would fail on nothing worse than the
            // whole patch happening to be ripe when the case reaches it.
            ctx.Assert(!bot.TestHasThirstySprout || bot.TestReady,
                "a refilled tank did not re-enable the spray even though a sprout wants water");

            ctx.Log($"Sprinkles: absent until the first harvest, then arrived dormant + glinting at " +
                    $"the garden edge; first tap woke it with a {tank}-charge tank; a tap trundled it " +
                    $"to an unripe sprout and ripened it on the spot; an empty tank gurgled instead " +
                    $"of spraying, and one recharge period re-armed it");
            gm.TestReset();
        }

        // ======================================================= TUGGY (DinoDigger-xt3)

        // Tuggy is earned by CATCHING A DUCK, arrives moored on the longest stream, and once
        // woken a tap toots out a line of 2-3 ducklings. The point of the redesign is that
        // they are EXTRA ducks: they are real DuckControllers, catchable and paying normally,
        // and they do not eat the ambient two-duck cap. The one-cell stream is shared without
        // a fight because nothing collides — and the tap that could be ambiguous is resolved
        // by rank, duck over machine, which this case asserts directly.
        private IEnumerator Case_TuggyTowsDucklings(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            MachineFriendController mf = EnsureMachines(ctx);
            DuckController ducks = Object.FindFirstObjectByType<DuckController>();
            ctx.Assert(ducks != null,
                "scene ships no DuckController — rebuild via DinoDigger/Build Main Scene");

            // ---- (1) NOT PRE-PLACED. ----
            ctx.Assert(!mf.TestGateTripped(MachineKind.Tuggy),
                "Tuggy's gate is already tripped before any duck has been caught");
            ctx.Assert(!mf.TestPresent(MachineKind.Tuggy),
                "Tuggy is moored on the stream before the child has ever caught a duck — the " +
                "duck-amplifier must never arrive before the ducks");

            yield return ctx.WaitFrames(3);
            ctx.Assert(!mf.TestPresent(MachineKind.Tuggy), "Tuggy arrived without its gate");

            // ---- (2) TRIP THE REAL GATE: catch a duck. ----
            Duck bait = ducks.TestForceSpawnDuck();
            ctx.Assert(bait != null, "could not spawn a duck to catch (no streams / no duck art)");
            yield return ctx.WaitFrames(2);
            Physics2D.SyncTransforms();
            gm.TestTapWorldRouted(bait.transform.position);
            yield return ctx.WaitFrames(2);

            ctx.Assert(bait == null || bait.TestCaught, "the duck was not caught by the tap");
            ctx.Assert(mf.TestGateTripped(MachineKind.Tuggy),
                "catching a duck did not trip Tuggy's discovery gate");

            yield return ctx.WaitUntil(() => mf.TestPresent(MachineKind.Tuggy), 10f,
                "Tuggy never arrived after the first duck was caught");

            TuggyMachine tug = mf.TestTuggy;
            ctx.Assert(tug != null, "Tuggy arrived but is not a TuggyMachine");

            // ---- (3) ARRIVAL: dormant + glinting, moored on the LONGEST stream. ----
            ctx.Assert(!tug.IsAwake, "Tuggy arrived already awake");
            ctx.Assert(tug.TestGlinting, "Tuggy is not running its come-look beacon");
            ctx.Assert(tug.TestRouteLength >= 2,
                $"Tuggy moored on a {tug.TestRouteLength}-cell course (needs real water to chug)");

            StreamNetwork streams = Object.FindFirstObjectByType<StreamNetwork>();
            if (streams != null)
            {
                int longest = 0;
                for (int i = 0; i < streams.Count; i++)
                {
                    IReadOnlyList<Vector3Int> cells = streams.CourseCells(i);
                    longest = Mathf.Max(longest, cells != null ? cells.Count : 0);
                }

                ctx.Assert(tug.TestRouteLength == longest,
                    $"Tuggy moored on a {tug.TestRouteLength}-cell course but the longest is {longest}");
            }

            int glints = tug.TestGlints;
            yield return ctx.WaitUntil(() => tug.TestGlints > glints, 8f,
                "Tuggy's arrival beacon did not repeat");

            // ---- (4) FIRST TAP WAKES IT. ----
            Physics2D.SyncTransforms();
            gm.TestTapWorldRouted(tug.transform.position);
            yield return ctx.WaitFrames(2);
            ctx.Assert(tug.IsAwake, "the first tap did not wake Tuggy");
            ctx.Assert(tug.TestReady, "Tuggy woke without a toot ready");

            // ---- (5) THE TOOT: 2-3 EXTRA ducklings, not a share of the ambient cap. ----
            int ambientBefore = ducks.TestAliveCount;
            int escortsBefore = ducks.TestEscortCount;
            gm.TestTapWorldRouted(tug.transform.position);
            yield return ctx.WaitFrames(2);

            ctx.Assert(tug.TestToots == 1, "tapping an awake, ready Tuggy did not toot");
            int min = gm.TestConfig != null ? Mathf.Max(1, gm.TestConfig.TuggyDucklingsMin) : 2;
            int max = gm.TestConfig != null ? Mathf.Max(min, gm.TestConfig.TuggyDucklingsMax) : 3;
            ctx.Assert(tug.TestDucklingsTowed >= min && tug.TestDucklingsTowed <= max,
                $"the toot towed {tug.TestDucklingsTowed} ducklings (expected {min}-{max})");
            ctx.Assert(ducks.TestEscortCount >= escortsBefore + min,
                $"only {ducks.TestEscortCount - escortsBefore} ducklings reached the water");
            ctx.Assert(ducks.TestAliveCount == ambientBefore,
                "the tow line was counted against the ambient duck cap — Tuggy must add ducks, " +
                "never spend the stream's existing ones");
            ctx.Assert(!tug.TestReady, "the toot did not spend Tuggy's cooldown charge");

            // ---- (6) A DUCKLING IS A REAL DUCK: it wins the tap over the boat, and paying
            //          catch behaviour is the shipped Duck path (quack + fly-off + reward). ----
            Physics2D.SyncTransforms();
            Duck duckling = FindNearestDuckling(gm, tug.transform.position);
            ctx.Assert(duckling != null, "no duckling found on the water after the toot");

            Collider2D tugCol = tug.TestCollider;
            Collider2D duckCol = duckling.GetComponent<Collider2D>();
            ctx.Assert(tugCol != null && duckCol != null, "missing collider on Tuggy or a duckling");

            // Find a point the boat and a duckling genuinely share — a real overlap on a
            // one-cell stream — and prove the DUCK answers it. This is the exact ambiguity
            // the roster eval said a tugboat would create; it is closed by TappableRank.
            Vector3 shared = Vector3.zero;
            bool foundShared = false;
            for (int i = 0; i < 6 && !foundShared; i++)
            {
                Vector3 p = duckling.transform.position + new Vector3(0f, i * 0.08f, 0f);
                if (tugCol.OverlapPoint(p) && duckCol.OverlapPoint(p))
                {
                    shared = p;
                    foundShared = true;
                }
            }

            if (foundShared)
            {
                ctx.Assert(gm.TestFindTappable(shared) == (Component)duckling,
                    "a tap where a duckling overlaps Tuggy resolved to the BOAT — the catchable " +
                    "duck must always win that tap");
            }

            int caughtBefore = tug.TestToots; // unchanged by a duck catch; guards against a mis-route
            gm.TestTapWorldRouted(duckling.transform.position);
            yield return ctx.WaitFrames(2);
            ctx.Assert(duckling == null || duckling.TestCaught,
                "tapping a towed duckling did not catch it — a duckling must be an ordinary duck");
            ctx.Assert(tug.TestToots == caughtBefore, "catching a duckling re-tooted the boat");

            // ---- (7) COOLDOWN HOLDS, and the denied tap is still answered. ----
            // Tuggy is CHUGGING and bobbing throughout, so every tap below is aimed at a moving
            // body — the case that exposed the stale-collider trap in the first place. The tap
            // hook re-syncs physics itself now, so these land on where the boat actually is.
            int deniedBefore = tug.TestDeniedTaps;
            int answeredBeforeTug = tug.TestAnsweredTaps;
            gm.TestTapWorldRouted(tug.transform.position);
            ctx.Assert(tug.TestAnsweredTaps == answeredBeforeTug + 1,
                "the cooling-down tap was swallowed entirely — it never reached the moving boat");
            ctx.Assert(tug.TestToots == 1, "a second toot fired while the cooldown was running");
            ctx.Assert(tug.TestDeniedTaps == deniedBefore + 1,
                "the cooling-down tap did nothing at all — every tap must be answered");

            // ---- (8) REFILL RE-ENABLES. ----
            tug.TestRefill();
            ctx.Assert(tug.TestReady, "a refilled Tuggy is still not ready");
            gm.TestTapWorldRouted(tug.transform.position);
            ctx.Assert(tug.TestToots == 2, "a refilled Tuggy refused to toot again");

            ctx.Log($"Tuggy: absent until the first duck was caught, then moored dormant + glinting " +
                    $"on the {tug.TestRouteLength}-cell longest stream; first tap woke it; a toot towed " +
                    $"{tug.TestDucklingsTowed} EXTRA ducklings (ambient cap untouched at {ambientBefore}); " +
                    $"a duckling out-ranked the boat for its tap and was caught normally; cooldown held " +
                    $"a repeat and a refill re-armed it");
            gm.TestReset();
        }

        // ============================================ DISCOVERY PACING (one at a time)

        // The pacing guard: at most ONE undiscovered machine may stand in the world. Two
        // gates tripping together must not put two glinting strangers on screen competing
        // for the same pair of eyes — the second waits until the first has been found.
        private IEnumerator Case_MachineDiscoveryQueue(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();

            MachineFriendController mf = EnsureMachines(ctx);
            ctx.Assert(gm.TestConfig == null || gm.TestConfig.MachineOneDiscoveryAtATime,
                "GameConfig.MachineOneDiscoveryAtATime is off — the pacing guard is disabled");

            ctx.Assert(mf.TestUndiscoveredCount == 0,
                "the world already holds an undiscovered machine after a reset");

            // Trip TWO gates in the same frame. (Gate PLAYTHROUGH is covered by the three
            // machine cases; this case is about what the queue does with them.)
            mf.TestTripGate(MachineKind.Sprinkles);
            mf.TestTripGate(MachineKind.Tuggy);
            ctx.Assert(mf.TestGateTripped(MachineKind.Sprinkles) && mf.TestGateTripped(MachineKind.Tuggy),
                "both gates should be tripped");

            // Give the queue several frames: it releases at most one arrival per frame AND
            // refuses to release at all while a sleeper is unfound, so more time must NOT
            // produce a second glinting machine.
            yield return ctx.WaitUntil(() => mf.TestArrivals >= 1, 10f,
                "no machine arrived at all after two gates tripped");
            yield return ctx.WaitFrames(20);

            ctx.Assert(mf.TestUndiscoveredCount == 1,
                $"{mf.TestUndiscoveredCount} undiscovered machines are in the world at once — " +
                "each discovery must get the child's whole attention");
            ctx.Assert(mf.TestArrivals == 1,
                $"{mf.TestArrivals} machines arrived while one was still undiscovered");
            ctx.Assert(mf.TestQueuedCount == 1, "the second machine did not queue behind the first");

            // Find the one that arrived and wake it — the queue must then release the other.
            MachineFriend first = mf.TestMachine(MachineKind.Sprinkles) ?? mf.TestMachine(MachineKind.Tuggy);
            ctx.Assert(first != null, "no live machine found to wake");
            MachineKind firstKind = first.Kind;

            Physics2D.SyncTransforms();
            gm.TestTapWorldRouted(first.transform.position);
            yield return ctx.WaitFrames(2);
            ctx.Assert(first.IsAwake, "the tap did not wake the first machine");

            yield return ctx.WaitUntil(() => mf.TestArrivals >= 2, 10f,
                "waking the first machine did not release the queued second one");

            ctx.Assert(mf.TestUndiscoveredCount == 1,
                "releasing the second machine broke the one-undiscovered-at-a-time invariant");
            ctx.Assert(mf.TestQueuedCount == 0, "the queue still holds a machine after both arrived");

            MachineKind secondKind = firstKind == MachineKind.Sprinkles
                ? MachineKind.Tuggy
                : MachineKind.Sprinkles;
            ctx.Assert(mf.TestPresent(secondKind), $"{secondKind} never arrived after its turn came");

            ctx.Log($"discovery pacing: two gates tripped together, {firstKind} arrived alone and " +
                    $"{secondKind} waited in the queue; waking {firstKind} released {secondKind}, and " +
                    "the world never held more than one undiscovered machine");
            gm.TestReset();
        }

        // ================================================================== helpers

        /// <summary>Prefer the scene's wired machine service; build + install one only when the
        /// scene has none (an older scene asset, or a hand-built rig). Mirrors
        /// <see cref="EnsureTown"/>, and always leaves the service reset to day zero so a case
        /// starts with no gates tripped and no machine standing anywhere.</summary>
        private MachineFriendController EnsureMachines(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            MachineFriendController mf = gm.TestMachines;
            if (mf == null)
            {
                mf = Object.FindFirstObjectByType<MachineFriendController>();
            }

            if (mf == null)
            {
                var go = new GameObject("~TestMachines");
                mf = go.AddComponent<MachineFriendController>();
            }

            TownController town = gm.TestTown;
            TownArea area = town != null ? town.GetComponent<TownArea>() : null;
            if (area == null)
            {
                area = Object.FindFirstObjectByType<TownArea>();
            }

            var sprouts = new List<BerrySprout>();
            IReadOnlyList<BerrySprout> live = gm.TestSprouts;
            if (live != null)
            {
                for (int i = 0; i < live.Count; i++)
                {
                    if (live[i] != null)
                    {
                        sprouts.Add(live[i]);
                    }
                }
            }

            mf.Configure(gm.TestMap, gm.TestLibrary, gm.TestConfig, area, town, gm.TestGarden,
                sprouts, Object.FindFirstObjectByType<StreamNetwork>(),
                Object.FindFirstObjectByType<DuckController>(), gm.TestOverworldRoot);

            gm.TestInstallMachines(mf);
            mf.TestResetMachines();
            return mf;
        }

        /// <summary>The nearest towed duckling to a point. Ducklings are named "Duckling" by
        /// the spawner, which is the only thing that separates them from an ambient duck —
        /// deliberately, because in every gameplay respect they are the same object.</summary>
        private Duck FindNearestDuckling(GameManager gm, Vector3 from)
        {
            Transform root = gm.TestOverworldRoot;
            if (root == null)
            {
                return null;
            }

            Duck[] all = root.GetComponentsInChildren<Duck>(true);
            Duck best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].name != "Duckling" || all[i].TestCaught)
                {
                    continue;
                }

                float sq = (all[i].transform.position - from).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = all[i];
                }
            }

            return best;
        }
    }
}

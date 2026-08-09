using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DinoDigger.Config;
using DinoDigger.Core;
using DinoDigger.Dig;
using DinoDigger.Managers;
using DinoDigger.Overworld;
using DinoDigger.UI;

namespace DinoDigger.Testing
{
    /// <summary>
    /// THE FOSSIL FINALE integration cases (DinoDigger-5ve / -3rz): the skeleton board fills,
    /// the Dino-Matic is dug out by the town crew, a completed skeleton becomes a real baby
    /// dinosaur that tap-joins the team, and a bone dug after the collection is finished pays
    /// out in coins instead.
    ///
    /// Between them they replace the two cases that retired with the egg-shard nest
    /// (NestAssembly, ShardHatchCeremony): the same behaviours — a progress display filling
    /// toward a threshold, and a ceremony that spawns a baby which joins on a tap — asserted
    /// against the systems that actually exist now.
    ///
    /// Registered from IntegrationTestCases.BuildCases and living in their own file so this
    /// wave's finale work never touches the same lines as the rest of the suite. See
    /// IntegrationTestRunner.cs for the driver.
    /// </summary>
    public partial class IntegrationTestRunner
    {
        // ============================================ SKELETON BOARD (DinoDigger-5ve)

        /// <summary>
        /// Bank bones through the REAL path and prove the collection screen is a faithful
        /// picture of the bank at every step:
        ///   1. no bones -> no HUD button at all (nothing on screen is ever a dead end);
        ///   2. each banked bone fills exactly one more slot, and WHICH slots are filled is
        ///      derived from the per-bone counts (a second rib fills the second rib slot);
        ///   3. tapping a filled bone wiggles it (the only interaction, and it always answers);
        ///   4. the last bone completes the species: the card brightens and celebrates once;
        ///   5. the whole thing survives a save v5 roundtrip.
        /// </summary>
        private IEnumerator Case_SkeletonBoardFills(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            yield return ctx.WaitFrames(2);

            SkeletonBoard board = gm.TestSkeletonBoard;
            ctx.Assert(board != null,
                "no SkeletonBoard — GameManager should self-heal one onto the HUD canvas at boot");

            // ---- (1) Day zero: no bones, so no button, and nothing to open. ----
            ctx.Assert(!gm.AnyBoneBanked, "a freshly reset game already has banked bones");
            ctx.Assert(!board.TestButtonVisible,
                "the HUD bone button is showing before a single bone has been dug");
            ctx.Assert(board.TestCardCount == SkeletonPlan.Species.Length,
                $"board draws {board.TestCardCount} cards, expected {SkeletonPlan.Species.Length}");

            DinoType species = SkeletonPlan.FocusOrder[0];
            int slots = SkeletonPlan.SlotCount(species);
            ctx.Assert(board.TestSlotCount(species) == slots,
                $"{species} card draws {board.TestSlotCount(species)} slots, plan says {slots}");
            ctx.Assert(board.TestFilledSlots(species) == 0, "a fresh card has filled slots");
            ctx.Assert(!board.TestCardBright(species), "a fresh card is not drawn dark");

            // ---- (2) Bank the skeleton bone by bone; the picture tracks the bank. ----
            for (int slot = 0; slot < slots; slot++)
            {
                int bone = SkeletonPlan.SlotBone(species, slot);
                ctx.Assert(gm.BankBone(species, bone), $"bone {slot} refused to bank");
                yield return ctx.WaitFrames(1);

                ctx.Assert(board.TestFilledSlots(species) == slot + 1,
                    $"{board.TestFilledSlots(species)} slots drawn filled after banking {slot + 1} bones");
                ctx.Assert(gm.TestBonesBanked == slot + 1,
                    $"bank holds {gm.TestBonesBanked} bones after {slot + 1}");

                if (slot == 0)
                {
                    yield return ctx.WaitFrames(2); // the button's visibility is derived in Update
                    ctx.Assert(board.TestButtonVisible,
                        "the HUD bone button did not appear after the first bone was banked");
                }

                if (slot < slots - 1)
                {
                    ctx.Assert(!gm.TestSkeletonComplete(species),
                        $"{species} reported complete after only {slot + 1}/{slots} bones");
                    ctx.Assert(!board.TestCardBright(species),
                        "the card brightened before the skeleton was finished");
                }
            }

            // The DRAWN board and the BANK agree, checked against the snapshot the save writes.
            int snapshotBones = 0;
            List<BoneSave> snapshot = gm.BoneBankSnapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].Species == species)
                {
                    snapshotBones += snapshot[i].Count;
                }
            }

            ctx.Assert(snapshotBones == slots,
                $"BoneBankSnapshot holds {snapshotBones} {species} bones, the board draws {slots}");

            // ---- (3) Open it, and tap a bone. ----
            board.TestPressButton();
            yield return ctx.WaitFrames(2);
            ctx.Assert(board.IsOpen, "pressing the HUD bone button did not open the board");

            SkeletonBoardSlot tapped = board.TestSlot(species, 0);
            ctx.Assert(tapped != null && tapped.IsFilled, "no filled slot to tap");
            int wiggles = tapped.TestWiggles;
            tapped.Wiggle();
            yield return ctx.WaitFrames(1);
            ctx.Assert(tapped.TestWiggles == wiggles + 1, "tapping a filled bone did not wiggle it");

            // ---- (4) Complete + celebrate. ----
            ctx.Assert(gm.TestSkeletonComplete(species), $"{species} not complete after {slots} bones");
            ctx.Assert(board.TestCardBright(species),
                "a completed skeleton must brighten to full colour on the board");
            ctx.Assert(board.TestCompletionCelebrations >= 1,
                "no completion celebration fired when the skeleton finished");
            ctx.Assert(gm.TestRevivalPending,
                "a completed skeleton must register as waiting for the Dino-Matic");

            board.Close();
            yield return ctx.WaitFrames(1);
            ctx.Assert(!board.IsOpen, "the board did not close");

            // ---- (5) Save v5 roundtrip: the collection comes back exactly. ----
            // BankBone persists through GameManager.SaveNow, so Save.Data already carries the
            // rows; serialising and reparsing them proves the v5 payload survives the trip
            // without touching the player's file (the SaveRoundtrip case owns that).
            SaveData live = gm.Save.Data;
            ctx.Assert(live.Version == SaveData.CurrentVersion,
                $"live save stamped v{live.Version}, expected v{SaveData.CurrentVersion}");

            var clone = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(live));
            ctx.Assert(clone != null && clone.Bones != null, "v5 payload did not survive serialisation");

            int clonedBones = 0;
            for (int i = 0; i < clone.Bones.Count; i++)
            {
                if (clone.Bones[i].Species == species)
                {
                    clonedBones += clone.Bones[i].Count;
                    ctx.Assert(clone.Bones[i].Count <= SkeletonPlan.NeedOf(species, clone.Bones[i].BoneIndex),
                        $"roundtripped bank holds more {(BoneType)clone.Bones[i].BoneIndex} " +
                        $"than the {species} skeleton needs");
                }
            }

            ctx.Assert(clonedBones == slots,
                $"save roundtrip lost bones ({clonedBones}/{slots} {species} bones survived)");

            ctx.Log($"{species}: {slots} slots filled one bone at a time, board matched the bank " +
                    $"at every step, card brightened + celebrated once, tap wiggled a bone, " +
                    $"and the v5 payload roundtripped {clonedBones} bones");
            gm.TestReset();
        }

        // ============================================== DINO-MATIC (DinoDigger-3rz)

        /// <summary>
        /// The excavation, end to end: the machine is ABSENT until the first bone is banked,
        /// then it arrives as a buried mound glinting for attention, the town's NPC crew digs
        /// it out through its construction states, and it lands in the save. Throughout, the
        /// player is untouched — the backhoe still drives, the game never leaves Roam, and the
        /// town's own build queue is not consumed (the free site raises no building).
        /// </summary>
        private IEnumerator Case_MachineExcavates(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            yield return ctx.WaitFrames(2);

            DinoMaticController svc = gm.TestDinoMatic;
            TownController town = gm.TestTown;
            ctx.Assert(svc != null, "no DinoMaticController — GameManager should self-heal one at boot");
            ctx.Assert(town != null, "no TownController (rebuild via DinoDigger/Build Main Scene)");

            int wallet = gm.Save.Data.TreasureCount;
            float perState = gm.TestConfig != null ? gm.TestConfig.TownSecondsPerBuildState : 8f;
            try
            {
                // Freeze the PAID queue by emptying the wallet: this case is about the free
                // site, and a plot breaking ground first would just make it wait. (The runner
                // restores TownSecondsPerBuildState; the wallet is restored below.)
                gm.Save.Data.TreasureCount = 0;
                if (gm.TestConfig != null)
                {
                    gm.TestConfig.TownSecondsPerBuildState = 0.35f; // an excavation, not a wait
                }

                // ---- (1) NOT PRE-PLACED. No bone banked, so no machine. ----
                ctx.Assert(!svc.IsFound, "the Dino-Matic's gate is already tripped on a fresh reset");
                ctx.Assert(!svc.TestPresent, "the Dino-Matic is standing in the world before any bone was dug");
                yield return ctx.WaitFrames(3);
                ctx.Assert(!svc.TestPresent, "the Dino-Matic arrived without its gate tripping");

                // ---- (2) THE FIRST BONE IS THE GATE. ----
                DinoType species = SkeletonPlan.FocusOrder[0];
                ctx.Assert(gm.BankBone(species, SkeletonPlan.SlotBone(species, 0)), "the first bone refused to bank");
                ctx.Assert(svc.IsFound, "banking the first bone did not trip the Dino-Matic's gate");
                ctx.Assert(gm.Save.Data.DinoMaticFound, "the gate did not persist");

                yield return ctx.WaitUntil(() => svc.TestPresent, 15f,
                    "the Dino-Matic never arrived after the first bone was banked");

                DinoMatic site = svc.Site;
                ctx.Assert(site != null, "the service reports a site but hands back null");

                // ---- (3) ARRIVAL IS AN EVENT: buried, glinting, and standing somewhere sane. ----
                ctx.Assert(!site.IsExcavated, "the Dino-Matic arrived already dug out");
                ctx.Assert(site.TestGlinting, "the buried machine is not running its come-look beacon");

                int glints = site.TestGlints;
                yield return ctx.WaitUntil(() => site.TestGlints > glints, 10f,
                    "the arrival beacon glinted once and went quiet (it must repeat until it is dug out)");

                Vector3 at = site.transform.position;
                ctx.Assert(gm.TestMap == null || gm.TestMap.IsWalkableWorld(at),
                    "the Dino-Matic landed on unwalkable ground");
                ctx.Assert(gm.TestMeadow == null || !gm.TestMeadow.ContainsOuter(at),
                    "the Dino-Matic landed inside the dino meadow");
                ctx.Assert(gm.TestMap == null || !gm.TestMap.InTownDistrict(at),
                    "the Dino-Matic landed in the town district (it belongs by the dig belt)");
                ctx.Assert(gm.TestGarden == null || !gm.TestGarden.ContainsWorldExpanded(at, 1),
                    "the Dino-Matic landed on the berry patch");

                float nearestMound = float.MaxValue;
                IReadOnlyList<DigMound> mounds = gm.TestMounds;
                for (int i = 0; i < mounds.Count; i++)
                {
                    if (mounds[i] != null)
                    {
                        Vector3 m = mounds[i].transform.position;
                        m.z = at.z;
                        nearestMound = Mathf.Min(nearestMound, (m - at).magnitude);
                    }
                }

                ctx.Assert(nearestMound > 1.0f,
                    $"the Dino-Matic stands {nearestMound:0.##}u from the nearest mound — too close " +
                    "to keep its own tap target clear");

                // ---- (4) THE PLAYER IS NEVER DRAFTED, and never interrupted. ----
                ctx.Assert(gm.State.Is(GameState.Roam),
                    $"the excavation changed the game state to {gm.State.Current}");

                BackhoeController backhoe = gm.TestBackhoe;
                Vector3 before = backhoe.transform.position;
                Vector3 drive = FindMoveTarget(gm.TestMap, before, 1.5f);
                ctx.Assert((drive - before).sqrMagnitude > 0.25f, "no distinct move target near the backhoe");
                gm.TestTapWorldRouted(drive);
                yield return ctx.WaitUntil(() => !backhoe.IsMoving, LegBudget(before, drive),
                    "tap-to-move never completed while the machine was being dug out");
                ctx.Assert((backhoe.transform.position - before).sqrMagnitude > 0.25f,
                    "the backhoe stopped responding to taps while the machine was being dug out");

                // ---- (5) THE CREW DIGS IT OUT. Residents only — the town's own labour pool. ----
                var crew = new List<DinoController>();
                for (int i = 0; i < 4; i++)
                {
                    crew.Add(gm.TestSpawnDino(DinoType.TRex, GrowthStage.Big));
                }

                // The first two took the buddy slots; force the rest into the resident pool and
                // stand them next to the site so the commute is a step, not a cross-island hike.
                for (int i = 0; i < crew.Count; i++)
                {
                    gm.TestMakeResident(crew[i], false);
                    if (crew[i] != null)
                    {
                        crew[i].transform.position = WalkableNear(gm.TestMap, at + new Vector3(0.8f, 0f, 0f));
                    }
                }

                yield return ctx.WaitUntil(() => town.TestActiveIsFree, 20f,
                    "the town crew never adopted the Dino-Matic as a work site");
                ctx.Assert(town.TestActiveSite == site, "the town's active site is not the Dino-Matic");

                yield return ctx.WaitUntil(() => site.IsExcavated, 45f,
                    () => $"the crew never finished the excavation (stuck at state {site.TestState} " +
                          $"of {BuildingController.ConstructionStates}, {town.TestBuilderCount} builders)");

                yield return ctx.WaitFrames(3);

                // ---- (6) It is a machine now, and it cost the town nothing. ----
                ctx.Assert(!site.TestGlinting, "a dug-out machine must stop glinting");
                ctx.Assert(town.TestActiveSite != site, "the crew is still assigned to a finished excavation");
                ctx.Assert(town.TestNextIndex == 0,
                    $"the excavation consumed {town.TestNextIndex} town plot(s) — it must raise no building");
                ctx.Assert(gm.Save.Data.DinoMaticState == BuildingController.ConstructionStates,
                    $"the finished excavation persisted as state {gm.Save.Data.DinoMaticState}");
                ctx.Assert(gm.State.Is(GameState.Roam), "the excavation left the game out of Roam");

                ctx.Log($"absent until the first bone; arrived {nearestMound:0.##}u clear of the nearest " +
                        $"mound, glinting; crew of {crew.Count} dug it out through " +
                        $"{BuildingController.ConstructionStates} states with no plot consumed; " +
                        "backhoe stayed under player control throughout");
            }
            finally
            {
                gm.Save.Data.TreasureCount = wallet;
                if (gm.TestConfig != null)
                {
                    gm.TestConfig.TownSecondsPerBuildState = perState;
                }
            }

            gm.TestReset();
        }

        /// <summary>
        /// The revival, end to end, INCLUDING the awkward ordering: the child finishes a
        /// skeleton BEFORE the machine has even been dug out. That must not deadlock — the
        /// board celebrates anyway, the machine glints harder when it turns up, and the moment
        /// it is excavated its button lights. Then a tap runs the ceremony (skip-tolerant), a
        /// baby of that species appears on the pad, and tapping IT joins the team and ends the
        /// ceremony — the same join path the egg-shard hatch always used.
        /// </summary>
        private IEnumerator Case_ReviveCeremonyJoins(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            yield return ctx.WaitFrames(2);

            DinoMaticController svc = gm.TestDinoMatic;
            SkeletonBoard board = gm.TestSkeletonBoard;
            ctx.Assert(svc != null && board != null, "no Dino-Matic service / skeleton board");

            DinoType species = SkeletonPlan.FocusOrder[0];

            // ---- (1) FINISH THE SKELETON FIRST. No machine yet: no deadlock allowed. ----
            int banked = gm.TestCompleteSkeleton(species);
            ctx.Assert(banked == SkeletonPlan.SlotCount(species),
                $"banked {banked} bones to complete {species}, expected {SkeletonPlan.SlotCount(species)}");
            yield return ctx.WaitFrames(2);

            ctx.Assert(gm.TestSkeletonComplete(species), $"{species} skeleton did not complete");
            ctx.Assert(!gm.TestSpeciesRevived(species), "the species is revived before any ceremony ran");
            ctx.Assert(board.TestCardBright(species),
                "the board must celebrate a finished skeleton even with no machine to use it on");
            ctx.Assert(gm.TestRevivalPending, "a finished skeleton is not registering as pending");

            yield return ctx.WaitUntil(() => svc.TestPresent, 15f,
                "the Dino-Matic never arrived, so the finished skeleton has nowhere to go (DEADLOCK)");

            DinoMatic site = svc.Site;
            ctx.Assert(site != null, "the service reports a site but hands back null");

            // It glints HARDER with a skeleton waiting: the eager beacon repeats faster than the
            // idle one, so two glints must land inside the plain interval.
            int glints = site.TestGlints;
            yield return ctx.WaitUntil(() => site.TestGlints >= glints + 2, 12f,
                "the buried machine did not glint harder with a finished skeleton waiting");

            // ---- (2) Dig it out (MachineExcavates owns the crew; this case owns the ceremony). ----
            site.TestForceExcavated();
            yield return ctx.WaitFrames(3);
            ctx.Assert(site.IsExcavated, "the machine did not reach its excavated state");
            ctx.Assert(site.TestButtonLit,
                "an excavated machine with a finished skeleton must light its button");

            // ---- (3) TAP THE MACHINE -> the ceremony. ----
            Physics2D.SyncTransforms();
            Collider2D col = site.GetComponent<Collider2D>();
            Vector3 tapAt = col != null ? col.bounds.center : site.transform.position;
            ctx.Assert(gm.TestFindTappable(tapAt) as DinoMatic == site,
                "a tap on the Dino-Matic does not resolve to the machine");

            gm.TestTapWorldRouted(tapAt);
            yield return ctx.WaitUntil(() => gm.TestCeremonyActive, 10f,
                "tapping the ready machine did not start the revival ceremony");
            ctx.Assert(gm.State.Is(GameState.Ceremony), "the ceremony did not take the game into Ceremony state");

            // SKIP-TOLERANT: a toddler hammering the machine mid-show advances it and can never
            // block or restart it.
            gm.TestTapWorldRouted(tapAt);
            gm.TestTapWorldRouted(tapAt);

            yield return ctx.WaitUntil(() => gm.TestCeremonyDino != null, 20f,
                "the ceremony never produced a baby dinosaur");

            DinoController baby = gm.TestCeremonyDino;
            ctx.Assert(baby.Type == species, $"the machine revived a {baby.Type}, not the finished {species}");
            ctx.Assert(baby.Stage == GrowthStage.Baby, $"the revived dino arrived at stage {baby.Stage}");
            ctx.Assert(!baby.IsBuddy, "the revived baby should wait as a resident until it is tapped");
            ctx.Assert(gm.TestSpeciesRevived(species), "the species was not marked revived by the ceremony");
            ctx.Assert(!gm.TestRevivalPending, "the machine still reports a revival pending after using it");

            yield return ctx.WaitFrames(2);
            ctx.Assert(board.TestCardBright(species),
                "the board must keep the revived species drawn in full colour");

            // ---- (4) TAP THE BABY -> it joins, and the ceremony ends. ----
            Physics2D.SyncTransforms();
            gm.TestTapWorldRouted(baby.transform.position);
            yield return ctx.WaitUntil(() => baby.IsBuddy, 12f, "tapping the revived baby did not make it a buddy");
            yield return ctx.WaitUntil(() => gm.State.Is(GameState.Roam), 12f,
                "the game never returned to Roam after the ceremony");
            ctx.Assert(!gm.TestCeremonyActive, "the ceremony did not end after tap-to-join");

            ctx.Log($"{species}: skeleton finished before the machine existed (no deadlock — the board " +
                    $"celebrated and the arriving machine glinted harder), dug out, tapped, ceremony " +
                    "survived three taps, baby appeared and tap-joined the team");
            gm.TestReset();
        }

        /// <summary>
        /// The collection is FINISHED — every skeleton revived — and the child digs another
        /// bone. It must not vanish and it must not silently over-fill a done skeleton: it
        /// converts at bank time into a fountain of coins, banked through the same guarded
        /// reward path every other coin uses.
        /// </summary>
        private IEnumerator Case_DuplicateBonePaysOut(TestContext ctx)
        {
            GameManager gm = ctx.GM;
            gm.TestReset();
            yield return ctx.WaitFrames(2);

            try
            {
                // FREEZE THE BUILD QUEUE. This case counts coins exactly, and the always-on
                // town builder spends the instant the wallet clears the next plot's price —
                // inside the very frame a coin banks — which would make the wait below chase a
                // total that had already been spent. (Restored in the finally, and again by the
                // runner after every case.)
                TownController.TestSuspendBuilds = true;

                // Own all five fossil species: a fossil dino standing in the world IS a revived
                // skeleton (see GameManager.SpawnDino) — the same state the ceremony leaves.
                for (int i = 0; i < SkeletonPlan.Species.Length; i++)
                {
                    gm.TestSpawnDino(SkeletonPlan.Species[i], GrowthStage.Baby);
                }

                yield return ctx.WaitFrames(1);
                ctx.Assert(gm.TestAllSkeletonsRevived, "owning all five fossil species did not finish the board");
                ctx.Assert(!gm.TestRevivalPending, "a finished board still reports a revival pending");

                // The dig has nothing left to bury, either — a site must not hand out bones
                // that could only ever be duplicates.
                ctx.Assert(!gm.TryNextNeededBone(out _, out _),
                    "the dig still wants to bury a bone with every skeleton revived");

                int coins = gm.TestDuplicateBoneCoins;
                int before = gm.Save.Data.TreasureCount;
                int bonesBefore = gm.TestBonesBanked;

                // ---- The duplicate. ----
                DinoType species = SkeletonPlan.Species[0];
                bool bankedIt = gm.BankBone(species, (int)BoneType.Skull, gm.RewardSpawnPoint);
                ctx.Assert(!bankedIt, "a duplicate bone was banked into a finished collection");
                ctx.Assert(gm.TestBonesBanked == bonesBefore,
                    $"the bank grew {bonesBefore} -> {gm.TestBonesBanked} on a duplicate bone");

                yield return ctx.WaitUntil(() => gm.Save.Data.TreasureCount >= before + coins, 30f,
                    () => $"the duplicate bone paid {gm.Save.Data.TreasureCount - before} coins, expected {coins}");

                ctx.Log($"board fully revived: the dig wants no more bones, and a duplicate paid " +
                        $"{gm.Save.Data.TreasureCount - before} coins instead of banking " +
                        $"(bank held at {bonesBefore})");
            }
            finally
            {
                TownController.TestSuspendBuilds = false;
            }

            gm.TestReset();
        }
    }
}

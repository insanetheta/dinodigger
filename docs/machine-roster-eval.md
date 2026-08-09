# Machine Roster — Critical Evaluation

Companion to `docs/backstory.md` (roster source) and epic DinoDigger-6fi. Every claim
below was checked against shipped code, not the bible's assumptions. Verified ground
truth that changes verdicts:

- **Bridges already exist and dinos path over them** (`OverworldMap` bridge cells,
  offset-tolerant pathing). Nothing on the island is ever blocked by a stream.
- **The duck-catch is the stream's whole joy** (`DuckController`: ambient spawn 20-40s,
  max 2, tap = quack + fly-off + reward). It is a scarce, timed tap-payoff.
- **Sprout growth is a 25s timer** (`GameConfig.SproutRipenSeconds/RegrowSeconds`).
  A growth-*rate* buff is arithmetic on a timer no 2-year-old perceives.
- **D3 depth layers are NOT shipped** (DinoDigger-dv1, P2 open). D2 Dino-Matic beats
  are P1 open. D1 (cascades, crystal pops, geodes, piñata pots, surprise pockets,
  toy roller) is in.
- **Mass dance is a one-time event** (parade, `SaveData.ParadeDone`). The nine
  per-species `DanceType` animations exist but are only ever seen one tap at a time.
- **Town life is already a courier spectacle**: `TownLifeController` visits carry
  props (shopping fruit, tossed coins, bowling boulders) between buildings.

Scoring lens: for a 2-3 year old, a machine earns its place only if it creates a
**new verb** or a **new immediate tap-payoff**. Ambient spectacle is valuable (the
town proves it) but it is the *cheapest* thing to add through existing systems
(townsfolk scenes), so an ambient-only *character* must clear a higher bar.

---

## Per-machine evaluation

### Glow (lantern bot — deep-dig reveal)

| Criterion | Assessment |
|---|---|
| Loop fit | Moment-to-moment, inside the dig — the loop holding >50% of playtime. The only roster machine that changes the **core verb**: the child sees a faint outline one tile ahead and *chooses* where to tap. Dig taps become decisions, not lottery pulls. |
| Fun-add | New information-payoff every dark layer, forever fresh because the buried content varies (bones, geodes, toys). Also solves a problem D3 itself creates: dark strata could read as unresponsive/scary to a toddler; Glow is the built-in comfort object. |
| Overlap | Complements peeks (crack-reveals fire on damage anywhere; Glow previews adjacency in dark strata only). One tuning risk: the dig rig is getting crowded (buddy superpowers + Glow both live on/near the rig) — keep Glow visually passive, a light source not an actor. |
| Readability | Lantern = light. Perfect one-glance job. Best discovery beat in the roster: found *through the core verb* (a soft glow behind dirt, break the tile, it stretches and hops up). |
| Cost:fun | Medium cost, highest fun. Art: ~5-6 gens (mossy/lit/hop/rig-idle states + belly-glow FX). Systems: one reveal-adjacent modifier in `DigModeController` + `DirtTile` dark-strata rendering — small *once D3 layers exist*. Hard dependency: D3 (DinoDigger-dv1) must ship first. |

### Doodle (music-box bot — plaza dance party)

| Criterion | Assessment |
|---|---|
| Loop fit | Session loop, town. Genuine new verb: **crank** (tap Doodle → crank turns → tune plays → nearby townsfolk break into their species dances). |
| Fun-add | Converts the one-time parade delight into an on-demand repeatable event, and finally exhibits the nine already-built `DanceType` animations as a *chorus* instead of one tap at a time. Music + repeated dancing is the single most replay-tolerant payoff at age 2 (song repetition is a feature, not a bug). Highest reuse of shipped content in the roster. |
| Overlap | Amplifies: dances, plaza, townsfolk. Mild pattern-collision with tap-to-cheer (both "tap thing near buildings, town reacts") — distinguish by placement (plaza center vs construction site) and payoff flavor (party vs work-burst). Parade stays special as the one-time first. |
| Readability | Crank + floating notes + everyone dances. A wind-up music box is a real toddler object; cause→effect is instant. The Parasaurolophus (the musical duck-bill) straightening the bent crank is the roster's best dino-machine friendship beat. |
| Cost:fun | Medium: ~5 gens (sleep/crank/play states) + one looping tune + a dance-summon broadcast (`TownLifeController`/`DinoController.Dance` already exists). Synergizes with the pending dig-audio pass (DinoDigger-7c4). Depends on shipped town life only — no epic blockers, but town is not the P0 focus. |

### Sprinkles (watering bot — garden growth)

| Criterion | Assessment |
|---|---|
| Loop fit | Session loop, garden. **As specced: ambient only.** After the one-time wake tap, the child does nothing new — Sprinkles autonomously waters and sprouts ripen on a faster timer. |
| Fun-add | As specced: near zero perceptible. Shaving a 25s invisible timer is a designer-facing buff; a 2-year-old cannot attribute "berries seem bigger lately" to the robot. The wake-up beat is good; encounter 50 is a wandering prop. |
| Overlap | No cannibalization — the garden has no competing actor. But it also barely amplifies: three sprouts, tap-harvest, done. |
| Readability | The *object* is perfectly legible (watering-can nose, spray arc). The *job as specced* is illegible because cause and effect are separated by a timer. |
| Cost:fun | Low-medium cost (~4-5 gens + spray FX; touches `BerrySprout` timer + tiny wander AI), but as specced the fun is a rounding error. **Redesign rescues it cheaply** (below). |

**Redesign (concrete):** make Sprinkles a tap-payoff, not a buff. Tap Sprinkles → it
scurries to the nearest **budding** sprout and sprays a sparkling arc → that sprout
visibly swells and **ripens right now** (reuse `BerrySprout.Ripen()`, which already
tweens scale + sparkles). Cooldown is wordless and diegetic: its transparent belly-tank
drains empty and slowly refills from a puddle-sip animation; empty tank = tap gives a
happy shake but no spray (tap still rewarded). This gives the garden its own analog of
a buddy superpower, makes the child the cause of every spray, and costs one tank
overlay + one scurry anim beyond the original spec.

### Tuggy (tugboat — stream ferry)

| Criterion | Assessment |
|---|---|
| Loop fit | Overworld ambient. No verb. The stated job — carry ducks and buddies across streams — solves a problem that **does not exist**: bridges are shipped, pathing crosses them, and a no-fail world has nothing that was ever stuck. |
| Fun-add | Spectacle only, and weak spectacle: a boat slowly crossing water reads identically on encounter 1 and 50. Nothing new to tap, collect, or nurture. |
| Overlap | **Actively harms the duck-catch**, the stream's one scarce joy: a duck riding Tuggy makes the catch-tap ambiguous (boat or duck?), and a ferry that carries ducks along/away reduces clean catch windows. The ducks-peck-it-awake discovery beat is the most charming in the bible — attached to the least defensible machine. |
| Readability | Boat = ride is legible; *why* it ferries is not, because the child has never seen anyone fail to cross. |
| Cost:fun | Worst ratio in the roster. Medium-high cost: boat art + bob/wake FX + boarding choreography + ride nodes in `StreamNetwork` + new states in `DinoController` — the most watchdog-laden, fragile system in the codebase (commute timeouts exist for a reason). All of it buys ambient traffic. |

### Zippy (parcel drone — town courier)

| Criterion | Assessment |
|---|---|
| Loop fit | Meta/town ambient. No verb. Its system hook — "make town loops visibly connect" — is a design-diagram goal, not a child experience. |
| Fun-add | Background traffic. Toddlers do love vehicles, but the town **already runs a courier spectacle with better actors**: townsfolk visits carry fruit, coins, and boulders between buildings, and they're dinos — which is the fantasy. |
| Overlap | Directly cannibalizes townsfolk visits, and thematically inverts rule 5 of the canon: the dinos are supposed to run their stone-age world; a drone doing their deliveries makes them passive recipients. |
| Readability | A flying basket is readable, but a quadcopter is the one roster silhouette toddlers *don't* know from picture books (digger, boat, sprinkler, lantern, music box are all board-book vocabulary). It's also the only flyer, needing its own traversal layer. |
| Cost:fun | Medium-high (flight anims, route/pickup/dropoff system across 9 buildings, `TownLifeController` integration) for ambient-only payoff that duplicates an existing, better spectacle. |

---

## Ranking and verdicts

| Rank | Machine | Verdict | Why (net) |
|---|---|---|---|
| 1 | **Glow** | **BUILD NOW** (with D3) | Only machine that upgrades the core verb, in the highest-playtime loop; also D3's own darkness-comfort solution. Ships inside DinoDigger-dv1. |
| 2 | **Doodle** | **BUILD LATER** | Best repeatable spectacle per unit cost — exhibits nine shipped dances as an on-demand party. After the dig epic and audio pass; no reason to build during a P0 dig push. |
| 3 | **Sprinkles** | **REDESIGN** (then build later) | As specced it's an invisible timer buff; as a tap-to-spray instant-ripen with a visible tank cooldown it becomes a cheap, legible new verb for the garden. |
| 4 | **Tuggy** | **CUT** | Solves a non-problem (bridges shipped), damages the duck-catch, and lands new states in the most fragile system. Keep the ducks-wake-it beat in the idea drawer for a future stream feature; kill the ferry. |
| 5 | **Zippy** | **CUT** | Duplicates townsfolk-visit spectacle with a worse actor, breaks the board-book silhouette language, and hands a dino job to a robot — the exact fantasy-theft the bible warns against. |

## Guardrail check — is the roster too big?

The bible's own ceiling (backhoe + Dino-Matic + 5 < 9 dino species) passes
arithmetically, **but the arithmetic is the wrong test.** What a toddler experiences
is per-scene density: as proposed, the overworld would host backhoe + Sprinkles +
Tuggy simultaneously, and the town Dino-Matic + Zippy + Doodle. Three machine
characters in one camera frame *is* the robots-take-over failure, whatever the global
count says. **Yes, the roster is too big — by exactly the two ambient-only machines.**
After the cuts: backhoe (everywhere), Dino-Matic (dig/town seam), Glow (deep dig),
Doodle (plaza), Sprinkles (garden) — five machines, each in a different space, never
more than two in frame, every one attached to a verb. That is the right size.

## New character — is there a gap?

After the cuts, loop coverage is: dig-deep = Glow, revival = Dino-Matic, growth
supply = Sprinkles, town joy = Doodle, everything = backhoe. The streams lose their
machine — correctly, because the ducks are the stream's characters and the catch is
its verb. **No current loop is unserved; the honest recommendation is zero additions
now.**

The one real gap is a *verb* gap, not a loop gap: nurture currently has a single verb
(feed). If a second nurture verb is ever wanted, the best candidate is **Bubbles the
bath bot** — a squat tub on tracks, found beached and barnacled by the stream; a
muddy-from-the-dig buddy climbs in, the child taps to scrub, bubbles pop, a shiny
happy dino hops out and does its dance. Washing toys is canonical age-2 play, it links
dig → meadow ("we got dirty digging"), and it gives the stream bank a machine whose
job the duck-catch doesn't own. Build it only after the five above are live and only
alongside a real dino-care loop — it would be machine #6 vs 9 species, still one per
scene.

## Sequencing — the pilot

**Glow ships with D3 (DinoDigger-dv1) as the pilot of the whole machine-character
pattern.** Reasons: (1) the wake-up-a-sleeper pattern will already be proven at
centerpiece scale by the D2 Dino-Matic excavation, so Glow extends a beat the child
knows rather than betting the pattern on an untested one; (2) it pilots in the loop
with the most playtime, so the pattern's value is measured where it matters; (3) its
discovery *is* digging — zero new discovery machinery; (4) it derisks D3 itself
(dark layers with a comfort-light were always one feature, not two). Then Sprinkles
(redesigned) as the cheap overworld follow-up, then Doodle with the audio pass.
Tuggy and Zippy do not enter the backlog.

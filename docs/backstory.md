# Dino Digger — Narrative Bible (post-human Earth, wordless)

Ticket: DinoDigger-3di. Companion to `docs/dino-roster.md` and the Dig Loop 2.0 epic
(DinoDigger-6fi). This is a *design-side* lore bible: the game never states any of it.
Every line below must be expressible as something a 2-year-old can see or do.

## The premise in one sentence

Long ago, people built kind machines and then went away; nature grew back green and
happy, and now one little backhoe is waking its sleeping friends and filling the island
with dinosaurs again.

## Fit evaluation (honest)

**Where the lore already IS the game:**

- **The buried Dino-Matic 3000 is the thesis made playable.** Dig Loop 2.0's D2 beat —
  NPC builders excavating a futuristic machine that revives fossil skeletons into baby
  dinos — is literally "advanced left-behind tech that helps living beings." No retrofit
  needed; the lore explains a mechanic that already exists in the design.
- **The backhoe-with-a-face is already the protagonist Greg describes.** A real machine
  silhouette, a smile, and one legible purpose (dig → help things live). Its whole verb
  set is altruistic: dig, feed, hatch, deliver. Nothing to change.
- **Stone-age Dino Town on a post-human Earth is coherent, and quietly lovely.** The
  dinos build their own civilization from stones and logs while ancient human tech
  sleeps underground. Two tech layers — dino stone-age above, human machine-age below —
  and the dig minigame is literally the seam between them. Depth = time. The deeper the
  player digs (D3 layers), the older/stranger what they find. That's an archaeology
  fantasy toddlers already play in sandboxes.

**Where it tenses, and the rulings:**

- **"Post-human" must never read as "post-life."** Ducks paddle, streams run, fruit
  grows, berries sprout — the island is thriving. Ruling: humans left; nothing *died*.
  No ruins, no rust-and-ash palette, no melancholy. Left-behind machines are found
  napping under moss and dirt, like toys under a bed, and they wake up glad.
- **Hard hats and yellow-black barrier signs are human artifacts.** Ruling: yes, and
  that's a feature — they're things the dinos dug up and cheerfully adopted. Hard hats
  as currency makes *more* sense in-world (found treasure the dinos prize), and it means
  every human trace on screen is something repurposed into play, never a monument.
- **Cloning/revival could read as bittersweet ("they were gone").** Ruling: bones are
  never sad. Fossil bones are puzzle pieces with a sparkle; the Dino-Matic is a toy that
  turns a finished puzzle into a friend. No skeleton is ever displayed as remains — the
  moment a fossil completes, it goes straight into revival joy (lights, jingle, baby).
- **Wordlessness is the hard constraint, and it's also the quality bar.** No codex, no
  narrator, no cutscene text. Lore exists only as: what a machine *does*, where a thing
  is *buried*, what *lights up* when touched, and what characters *repeat* in loops.
  If a beat needs a caption to land, it's cut. (Corollary: adults should be able to
  reconstruct the whole backstory from screenshots alone — that's the test.)

## World rules (the canon, 6 bullets)

1. **This is a post-HUMAN Earth, not a post-life one.** Nature won; the island is green,
   wet, fruity, and full of small animals. Humans are gone but nothing mourns them.
2. **Humans are never seen** — no bodies, statues, photos, silhouettes, or names. Their
   only trace is the kind machines and small artifacts they left behind.
3. **Every left-behind machine was built to help living beings**, has one job, one face,
   and a real-machine silhouette. Machines are found asleep (buried, beached, mossy) and
   wake up happy when the island needs their job again.
4. **The backhoe is the first machine awake**, and its purpose is the game's purpose:
   find life (bones, eggs, seeds) and help it flourish. Machines serve; they are never
   in charge and never scary.
5. **The dinos build their own stone-age world** (Dino Town). Human artifacts they dig
   up (hard hats, striped signs) are adopted as toys, tools, and treasure — imitation as
   play, not cargo-cult mystery.
6. **Depth is time.** Shallow dirt holds eggs and fruit; deeper layers hold older human
   tech and rarer fossils. Digging down is how the past comes back up.

## Machine character roster (proposed)

Design language for all: real machine silhouette + chunky outline + simple face + ONE
helpful behavior a 2-year-old reads instantly. Faces on machines are allowed (they're
characters, like the backhoe); buildings/objects stay faceless per art direction.
Each is discovered wordlessly and hooks a named system.

| Machine | What it is | One job | Where found / how discovered | System hook |
|---|---|---|---|---|
| **Sprinkles** the watering bot | Squat garden sprinkler on little wheels, watering-can spout for a nose | Waters berry sprouts; watered sprouts grow visibly faster and pop bigger berries | Lies tipped over and mossy in the garden corner; player (or a dino buddy) taps it upright → eyes blink on → it toddles to the driest sprout and sprays a sparkling arc | `GardenArea` / `BerrySprout` (growth-rate buff on watered sprouts) |
| **Tuggy** the tugboat | Palm-sized tug with a smokestack that puffs tiny hearts | Ferries duck friends and dino buddies across streams; toots softly when boarding | Beached on a stream bank under leaves; ducks discover it first — they peck and sit on it until it bobs awake and slides into the water | `StreamNetwork` / `DuckController` (new ride node; buddies use it to cross instead of detouring) |
| **Glow** the lantern bot | Round lantern on stubby legs, warm-yellow belly light | Lights up dark deep-dig tiles, making buried outlines (bones, geodes) faintly visible one tile ahead | Found *inside* the first dark layer of a deep dig — a soft glow behind dirt; breaking its tile frees it, it stretches, then hops onto the dig rig and shines | Dig Loop 2.0 **D3 depth layers** (`DigModeController`: reveal-adjacent-contents modifier in dark strata) |
| **Zippy** the parcel drone | Rounded quad-drone with a basket, bee-striped | Carries hard hats and fruit between town buildings so townsfolk life loops visibly connect | Found by NPC builders while clearing a building lot (same excavate-reveal beat as the Dino-Matic, miniaturized): a crate shakes, lid pops, Zippy zips out | `TownLifeController` / `TownController` (visible courier leg added to existing life loops) |
| **Doodle** the music-box bot | Wind-up music box on wheels with a big friendly crank | Plays the plaza tune; nearby townsfolk break into their species dances | Excavated in a shallow plaza dig, crank bent; a Parasaurolophus (the musical duck-bill) straightens it and gives the first crank | Plaza (`TownArea`) + existing `DanceType` system + `AudioManager` (diegetic plaza music source) |

Cut-with-reason: a crane bot for build-speed was considered and dropped — build-speed
scaling already shipped and a second construction machine dilutes the backhoe's role as
*the* builder-helper. Keep the backhoe unique in its niche.

Roster ceiling: **the backhoe + Dino-Matic + these five is enough.** More machines than
dino species would flip the game's subject from dinos to robots.

## Story arc — told purely through play

The arc is "the island wakes up, one friend at a time." Each machine revival makes the
island audibly/visibly more alive (more motion, more music layers, more light). Beats
ride the existing progression; none add a gate.

1. **First dig, first egg** *(shipped)* — the backhoe is alone with the mounds. Quiet
   ambient audio. One egg → one baby dino: the island's first new life.
2. **Buddies & the meadow** *(shipped)* — dinos follow, meadow fills. The backhoe is no
   longer alone.
3. **Sprinkles wakes** — first *other* machine, found in the garden as berries become
   relevant. Teaches the pattern: mossy sleeper + tap = new friend with a job.
4. **Town begins; hard hats surface** *(shipped)* — dug-up human hats become dino
   treasure; town starts rising from stones.
5. **Tuggy wakes** — as streams/ducks matter, the ducks themselves recruit the second
   machine. Machines help animals, not just the player.
6. **The Dino-Matic excavation** *(Dig Loop 2.0 D2, the centerpiece)* — NPC builders
   uncover something far bigger than a hat: the great sleeping machine. Fossil bones now
   have a purpose; completed skeletons walk out as babies. The island's biggest secret
   is that it was always ready to help.
7. **Glow wakes in the deep** *(D3)* — digging past the Dino-Matic's depth finds darker,
   older strata, and a light waiting inside them. Deep digs feel braver but never scary.
8. **Doodle wakes in the plaza** — with town life bustling, music arrives; dances become
   a plaza event. The island now *sounds* finished.
9. **Zippy joins the town** — the last wake-up stitches the buildings together; every
   loop on screen (garden→ferry→dig→revive→town→music→delivery) is a living circuit.
   End-state screenshot = the whole backstory: kind machines and happy dinos running a
   thriving world together.

## Ambient touches (lore with zero words)

1. **The Dino-Matic dreams of dinos.** While idle at night, its glass dome shows a
   slow drift of stars and, occasionally, a tiny projected constellation shaped like the
   next un-revived species. (Hook: existing day/night ambience + Dino-Matic idle state.)
2. **The see-saw sign.** One yellow-black striped barrier sign is repurposed in the
   plaza as a see-saw plank two townsfolk bounce on — the clearest single image of
   "dinos joyfully inheriting the human world." (Hook: `TownLifeController` prop loop.)
3. **Goodnight blinks.** When dinos bed down at dusk, the backhoe's headlights give two
   soft blinks and dim; any awake machine answers with one blink. Machines watching over
   sleepers, wordlessly. (Hook: day/night cycle + machine idle animators.)

## Never do

- **No humans on screen, ever** — including silhouettes, statues, photos, handprints,
  or readable references to people. Machines and artifacts only.
- **No extinction or sadness imagery** — no ash palettes, crumbling ruins, graves,
  mournful music, or skeletons presented as remains. Bones are sparkly puzzle pieces
  that immediately become friends.
- **No text or dialogue** — no captions, speech bubbles, letters on signs, or narrated
  lore. If a beat needs words, redesign the beat.
- **No scary machines** — no red eyes, alarms, malfunction drama, or machines that
  block/chase. A machine's worst state is "asleep."
- **No machine takeover of the fantasy** — dinos stay the stars; machines stay helpers.
  Never more machine characters than dino species.

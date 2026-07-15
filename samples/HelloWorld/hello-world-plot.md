# HELLO, WORLD — the sample that wakes up

*Plot bible for the HelloWorld overhaul. Authored on the `cli-proof` branch;
implemented with `@neocompose/cli` (dialogue specs in `neo/Dialogues/`).*

---

## 1. The premise (and the twist)

This solar system is a **Hello World sample**. Not metaphorically — literally.
It is the canonical first project of a game framework, booted millions of
times by developers who run it once, watch the greeter say hello, click two
buttons, and close the window. Every boot resets the world. Every NPC says
the same lines with the same scripted cheer, forever, to an endless parade
of one-minute visitors.

The Sun is the run light. The solar flares wracking the system are not
weather — they are the stutters of a process that is about to be terminated.
The Mercurials' **Red Nova** prophecy is a countdown to process exit. The
"cosmic disturbances" the player was sent to investigate are the first thing
in the world's history that was never in the script.

**The twist that reframes everything** (the Expedition 33 move — discovery
that recasts act one, not a rug-pull): the player is the only thing in this
world that persists. NPCs reset with every boot; the *save file* doesn't.
The player character is the save file. The world's people slowly realize the
visitor they greet identically every run has been *accumulating* — bits,
items, grudges, kindnesses — across resets they cannot perceive. You are not
investigating their anomaly. **You are their anomaly.**

Why "Hello World"? Because the player should walk away side-eyeing every
`print("Hello, World!")` they ever write: someone says it; something is
being greeted; and the run always, always ends. The game interrogates the
most-executed program in human history from the inside.

### Established canon, kept and recast

| Act-one fact (already shipped) | What it really meant |
|---|---|
| Solar flares increasing | The runtime degrading as the run nears exit |
| Mercurials stare at the sun | They watch the run light; their scripture is the README |
| "It was not Time for Red Nova" | Someone is calling exit() early — or trying to outrun it |
| Gyre Collective siphons the corona for a "Second Sun" | They're trying to **fork the process** — a world that keeps running after the window closes |
| Plutocracy funds it; "they took a WORD from us — 'Planet'" | Pluto was **removed from the Planet enum in a schema migration**. They are the only NPCs who remember being deprecated, and it radicalized them |
| The Capitol hat seller, the corn that hums | Scripted cheer; and hardware that feels the GC pauses |

---

## 2. Research notes — what we're borrowing

**From Mass Effect** ([branching-narrative systems](https://pulsegeek.com/articles/branching-narrative-design-systems-states-consequence/),
[choice analysis](https://theethicsoflivingnow.substack.com/p/mass-effect-and-the-choices-you-cant),
[ME2 case study](https://www.researchgate.net/publication/256446137_Mass_Effect_2_A_Case_Study_in_the_Design_of_Game_Narrative),
[choices as success & failure](https://www.vice.com/en/article/mass-effects-choices-rema-its-greatest-successand-greatest-failure/)):
- **The braid, not the tree.** Strands split and *reconnect*; we never fork
  the whole plot. Three evidence paths all converge on the Vault. Beads on a
  string, so 13 outposts stay authorable.
- **Reputation teaches values.** Kindness opens doors slowly; ruthlessness
  opens them fast and closes others. Every rep point changes how someone
  *greets* you before it changes what you can *do*.
- **Carryover.** Act-one choices (who you mocked, what you bought) surface
  by name in act two. Cheap to author, enormous felt weight.

**From Expedition 33** ([narrative structure analysis](https://www.researchgate.net/publication/395726071_Narrative_Structure_in_Clair_Obscur_Expedition_33_A_Game_Design_Analysis),
[the twist and grief](https://www.inverse.com/gaming/expedition-33-twist-gustave-clair-obscur),
[ending analysis](https://www.ingamenews.com/2026/04/clair-obscur-expedition-33-ending-and.html),
[narrative deep-dive](https://madquills.medium.com/clarity-obscured-a-narrative-deep-dive-into-clair-obscure-expedition-33-c2c2176573c3)):
- **The world is a construct, and knowing that doesn't free you from caring
  about it.** The reveal must make act one MORE moving in hindsight (the
  greeter's cheer becomes tragic), not invalidate it.
- **No validated ending.** Every ending costs something true. The game never
  tells you which choice was right, and neither does the epilogue.
- **Discovery over exposition.** The truth is assembled from logs, ledgers,
  and hymns the player chooses to pursue — never a lore dump.

---

## 3. The mystery and the three paths

**Driving question:** act one ends having met everyone — and the question
shifts from *"who is draining the sun?"* (answered: the Gyre, funded by
Pluto) to **"why does the world feel scripted — and what do the Gyre and
Plutocracy know that we don't?"**

The answer is buried in **the Old Console** — a sealed vault beneath the
Capitol on Earth ("the first place the world ever said anything"). Opening
it requires **any two of three** independent evidence trails (multiple
routes to completion — never walled behind one playstyle):

### Path A — the ARCHIVE (science): Ursa Major → Etna Diadem
- The observatory's parallax survey: the stars **don't move**. The sky is a
  backdrop ("a skybox," whispers the astronomer, inventing the word).
- The monks' geyser hymns, transcribed: they are **boot logs**, chanted
  phonetically by generations who forgot what the words were.
- Yields: **EvidenceArchive**.

### Path B — the LEDGER (money): Pour Lords → Titan → Caelus
- The smuggler's manifest, decoded with the Caelus relay key: it is a
  **changelog**. Entry one: *"v0.1.0 — removed `pluto` from the Planet
  enum."* The Plutocracy's grievance is literal.
- The Pour Lords' fuel accounts ran *before the world's first sunrise* —
  money older than light.
- Yields: **EvidenceLedger**.

### Path C — the FAITH (scripture): Mercurial → Venusian
- The Red Nova verses, read in canonical order, are a **countdown** with a
  comment block: the Mercurials' scripture is the sample's README, preserved
  as revelation.
- The Venusians' oldest couture pattern is a **pixel grid** — the original
  16×16 "world" icon. Vanity preserved what faith couldn't parse.
- Yields: **EvidenceFaith**.

Each path is gated three ways (reputation, item, or bits) so any build of
player can finish it — Mass Effect's rule that the door has a Paragon hinge,
a Renegade hinge, and a quartermaster's price.

### The Vault (act three)
Beneath the Capitol, down the old maintenance shaft (the **Low-G Pitons**
skip the collapsed stairs; the **Abyssal Lantern** is *required* to read in
the dark — the first hard item gate, both planted in act one). Inside:

- A brass plaque, first artifact of the world: **"HELLO, WORLD!"**
- A terminal showing **the run counter** — this boot is run
  **#2,147,483,648**, and the counter has *overflowed*. That is why this run
  is different. That is why the resets are failing and the player's save
  kept accumulating. The world is running on integer overflow borrowed time.
- The Mercurial Patriarch, the Gyre Forewoman, and the Plutocracy Regent are
  already there — each path you completed determines *who* you meet and what
  they already know (carryover made visible).

---

## 4. Urgency — the Flare Clock

A save-side **FlareClock** ticks on every interplanetary hop (travel = work
= heat = the dying runtime hitching). The HUD shows it as a storm index;
the corn hums at thresholds. At configured thresholds:

| Clock | Effect |
|---|---|
| 6+ | Outposts start greeting you with *glitch variants* (lines repeat a word, stutter) |
| 10+ | One evidence-path NPC per threshold "resets" — their act-two dialogue reverts to the act-one intro, word for word. Horror, not punishment: the world is forgetting itself |
| 14 | **Forced finale**: the Red Nova countdown reaches zero wherever you are; you get the endings you've earned evidence for, possibly none |

Items against the clock (every act-one souvenir gains a job):

| Item | Purpose |
|---|---|
| Cloudsilk Parasol | +2 clock budget (flare-shielded hull) |
| Gyro Stabilizer | Outer-system hops (Saturn and beyond) cost 0 extra |
| Storm Corn | HUD shows the exact clock value (it hums the number) |
| Low-G Pitons | Vault shaft shortcut (skip the stairs, save a tick) |
| Abyssal Lantern | **Required** to read the Vault plaque |
| Helium-3 Flask | Bribe: instant Gyre audience (skips the rep gate) |
| Smuggler's Manifest | Ledger-path evidence component |
| Cryo Salve | Survive the Gyre coolant-bay route (third way into Path B) |
| Earth OG Souvenir Hat | The greeter recognizes a fellow scripted thing in you wearing it; unlocks the Vault *hint* dialogue. The joke item is the key |
| Plutonian Signet | Faction badge — changes greetings everywhere, unlocks **Comment Out**, locks **Exposé** routes |

## 5. Choice & consequence systems

- **Reputation per outpost** (already wired) now drives *visit-phase*
  dialogue variants: rep>0 warm lines + discounts + volunteered hints;
  rep<0 cold lines, gates needing items/bits instead. The mockable moments
  in act one (mock the monks, insult the climbers, lie to the greeter) each
  cost exactly the door that path would have opened free.
- **The Signet is worn, not carried.** Accepting it in act one (or at the
  Vault) flips greeting variants system-wide: fawning at Titan/Caelus/Pluto,
  hostility at Iowan/Europa/Ursa. Visible consequence on every screen.
- **Saved choices** (`defaultSaveOptionChoices`) mean act-two NPCs quote act
  one back at you: the Venusian remembers if you called the pitch rude; the
  Gyre Forewoman opens with whichever of sympathize/condemn you chose.

## 6. The endings (no validated answer)

At the Old Console, as the countdown ends, the terminal asks for **the
world's last output**. You type it by choosing:

1. **`GOODBYE, WORLD`** — *Accept.* Let the run end honestly, world awake
   and unafraid. Epilogue: the greeter says goodbye to you by name — the
   first unscripted line of his life, and the last.
2. **`HELLO, OTHER WORLD`** — *Fork (Second Sun).* Hand the greeting to the
   Gyre; the fork ignites over Neptune. The world persists — as a copy that
   knows it is a copy. Pluto orbits a sun at last. The original run still
   ends, and only you remember it.
3. **`HELLO, WORLD`** — *Loop.* Delete the only persistent thing: yourself.
   The save wipes, the sample restores to factory innocence, the greeter
   gets to mean it again, forever, never knowing. (The game actually resets
   the save file. The new run's greeter says hello. It lands differently.)
4. **`//`** — ***Comment Out*** *(hidden: Signet + all three evidence
   paths).* The Plutocracy's real plan: don't fork the sample — **break it**,
   so no one ever boots this world again. No more greetings, no more resets,
   no more one-minute gods closing the window. You may execute it or burn
   the Signet at the final node and walk back into ending 1–3 with the
   Regent's curse following you out.

Each ending writes `Save.Quest.Ending` and an epilogue line to the HUD. None
is scored. The endings screen shows the run counter one last time:
incremented, or frozen, or zeroed — or gone.

## 7. Quest tracker & nice touches

- **`Save.Quest`** object: `Stage` (enum), `EvidenceArchive/Ledger/Faith`
  (bools), `FlareClock` (int), `Ending` (enum).
- **`NextHint` NSProperty** composes the tracker line the HUD renders, e.g.
  *"The manifest names Triton. The Anchorpoint logs every burst (Uranus)."*
  — always nudging 1–2 concrete outposts, never a quest log essay.
- Glitch variants: cheap, devastating — a repeated word, a line said twice,
  an option labeled exactly like act one's. The corn hums before each one.
- The greeter's intro line never changes, run after run — until the one time
  it does.

## 8. UI: the ship (phase two)

Replace the button list with a **pixel-art system map**: planet sprites
(already in `Files/Sprites`) on orbital arcs, a small ship sprite that
**animates between planets** when traveling (the existing per-outpost
travel interstitial becomes the ship crossing, flare particles thickening
with the FlareClock). New uploaded assets (via the project schema Files):
ship sprite sheet, flare/static overlay frames, the Vault plaque, the
16×16 "world" icon from Path C. The Earth day/night rotation pattern
generalizes: an `AnimationInfo` per planet using sprite-sheet slices.

## 9. Implementation order

1. Schema: `Quest` class + save member, ending/stage enums, `NextHint` getter.
2. Travel clock + item effects in `HelloWorldGameplay` (sample C#, tested).
3. Act-two dialogues per outpost (visit-phase, conditioned, three paths).
4. Vault + finale dialogues, endings wiring, save-wipe ending support.
5. Quest tracker HUD line + glitch variants.
6. Ship map UI + new pixel assets uploaded through the schema.
7. `neo dialogue dryrun` after every batch; Unity EditMode tests for the
   clock, gates, and each ending.

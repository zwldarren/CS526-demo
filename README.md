# FACET — Prototype

A 2D top-down tower-defense prototype where **circles are money and half-circles are ammunition** —
and both are physical items riding conveyor belts. There is no abstract currency: a circle mined at
the edge of the map is not spendable until a belt delivers it into the Core, and a cannon fires only
the half-circles that physically reach it. A wrong-shape delivery jams the belt segment it lands on
and silences everything downstream, so the answer to pressure is re-plumbing the line.

Unity 6 (`6000.3.23f1`), 2D, built for WebGL. Current scope: map 1 ("Quarry", 80×48) · 1 wave ·
1 mineral (○) · 1 ammo (◠, split 1:2 by the decomposer) · 1 turret (Cannon) · 1 enemy (Spike).
The map table in `Maps.cs` is the expansion seam: a second map — more waves, more minerals — is a
new entry there, not new systems. So is `Assets/Data/ContentDatabase.asset`: a new machine, enemy,
shape or recipe is a row plus the enum value that names it, not a `switch` that has to learn about it.

## Controls

| Input | Action |
|---|---|
| `WASD` / arrows | pan the camera |
| mouse wheel | zoom |
| **click the build bar** | select a building — the bottom bar is a row of buttons, each showing its hotkey, name, role and cost |
| `1`–`9` | select the same buildings by hotkey |
| `Q` / `E`, or the info card's **turn** buttons | turn the placement ghost (a splitter and every converter have no facing to turn) |
| left mouse | place the selected building — **drag** to lay a belt run or to aim a machine |
| right mouse | delete what is under the cursor for a full refund; on a jammed segment it **clears the jam** instead |
| `Space` / `P`, or the **pause** button | pause / resume |
| `N`, or the status card's **start wave** button | start the next wave now — the first wave only ever arrives this way. Once a map is cleared it carries on to the next map instead |
| `R`, or the **restart run** button | restart the run |

The HUD is four fixed regions, all clickable: a status card top-left (map and wave, the Core's
health, the stockpile, turret and jam diagnostics, the last second's events), a controls card
top-right, the build bar along the bottom, and an info card above it that describes whichever
building is hovered or selected - its cost, what it does, its numbers, and the facing its ghost will
be built with. The bar's tiles are tinted when the stockpile cannot cover them and outlined when
selected, so the palette doubles as an affordability readout and does not require the number keys.

## The rules the prototype is built on

* **Everything costs circles, and circles are only spendable once belted into the Core.** A run opens
  with 80 circles already banked — the bootstrap budget, the one delivery the player does not make
  themselves, and the only one that is not belted home. A drill
  mines one circle per second onto the belt it faces; the line home is a purchase (1 ○ per belt
  cell), so the map's distances are the economy. Removal refunds in full.
* **The decomposer splits ○ → ◠◠ in 1.5 s.** It eats only circles — feeding a half-circle back into
  it jams the feeding belt — and pushes its halves out through the belts pointing away from it, round
  robin, exactly as a splitter's ports work. One outlet caps it at that belt's 1/s; a second outlet
  lets it run at its own 1.33 halves/s, so two cannons at full rate still need two decomposers (or
  one, split and shared, at half rate each).
* **The second map opens a second mineral, and the second chain is the first one's shape.** Square
  patches yield □; a **cutter** runs □ → ▭▭ on its own recipe, and a **mortar** eats only ▭. The cutter
  is the decomposer's behaviour on another recipe and the mortar is the cannon's behaviour on another
  diet, so what the second chain buys is a choice rather than a bigger number: the mortar's cycle is
  half again as long - two-thirds the shot rate - for twice the damage, which is one item per kill
  against the cannon's two, so a thinner line can feed it and it pays for that with a slower reaction
  and a longer reach. Two guns also means two
  diets, so a line of the wrong ammunition jams at the wrong gun exactly as it does at the right one.
* **The cannon eats only ◠ and the mortar only ▭**, each at most one shot per interval. Their real fire
  rates are `min(1/interval, what their lines deliver)` — damage is a throughput property of the
  layout, not a purchase.
* **A wrong-shape delivery jams that segment** and it stays jammed, with the offending item on it.
  Right-clicking the jammed segment clears the jam and destroys that item, keeping the belt — so the
  line keeps its shape and is one click from running again. Turning the belt with `Q`/`E` does not
  clear a jam, and a jammed belt cannot be deleted until it has been cleared.
* **The pipe jumps one tile** — it swallows whatever is delivered and drops it on the belt two cells
  ahead, which is how two belts cross. It is transport, not a machine with a diet: it never jams.
* **A machine's ports are read off the belts around it**: a belt pointing in is an input, a belt
  pointing away is an output. That one rule gives the splitter 1-in-3-out, 3-in-1-out and 2-in-2-out
  as the same building on different streets, and gives the decomposer a second outlet the moment a
  player lays a second belt away from it. Splitters deal items round-robin, skip blocked outputs, and
  backpressure when full.
* **The sorter routes by shape** — the same hub as a splitter, with one setting: the shape its
  definition filters for leaves by the side it faces, and every other shape leaves by the other
  outlets. It ships filtering for the circle — the money shape — so one sorter facing the Core banks
  the money while the half-circles carry on down the line, which turns "keep money and ammo apart"
  from two separate lines into one split. Routing is strict: if the side a shape needs has no room, the
  sorter holds the item and the line backs up behind it. It never pushes a shape somewhere that will
  never want it, so a mis-wired outlet is a queue you can see rather than a jam four cells later.
* **The first wave waits for the player.** A run opens with as much time to build as they want: the
  first wave has no countdown and never arrives on its own — `N`, or the HUD's button, calls it, and
  only the defence that is actually finished is tested. Waves after it count down on the clock and
  walk in on their own, so the schedule is pressure the player meets later, not noise at the start.
* **Enemies walk over buildings.** Damage has to stay the only defence — but with build costs now
  real, whether buildings may block movement is worth revisiting on a later map.
* **Every drill needs a shape patch in the ground**, and every shape patch only accepts a drill — so a
  patch is what a chain is built around. **A belt, though, can be laid across ore**: transport crosses
  a vein, only machines are kept off it. That is what makes a wide vein workable at all, because a
  drill pushes onto the tile *beside* it and inside a 3x3 patch every neighbour is ore — so the middle
  cell costs you the belt lane crossing it (and the drill that would have stood on that lane's cell),
  rather than being dead ground. The ore is not consumed by the belt: it is recorded under it and comes
  back when the belt is removed, and the ore bed stays visible on both sides of the lane.
* **Clearing a map loads the next one without restarting.** The first map mines one mineral and holds
  one approach; the second is smaller, mines both minerals, and sends three times as many enemies in
  three waves, opening a new door on each one. `N` on a cleared map builds the next map in place — same
  views, same session — and what carries over between maps is only where the player is.

## Running it

Open the project in Unity `6000.3.23f1` and press Play with `Assets/Scenes/Main.unity` open. The scene
contains a `Main Camera` and one object with `SimulationDriver`; the driver builds every view at
runtime (terrain, machines, belts, items, enemies, shots, cursor, HUD), so there are no prefabs to
wire. The whole look lives in `Assets/Data/Palette.asset` — the shared material, the colours, the
screen-space line widths, and every view's geometry and layer depth — so a re-skin is a duplicate of
that asset, not a code change. The material itself is `Assets/Art/Materials/UnlitVertexColor.mat`.

The simulation itself (`Assets/Scripts/Core`) is engine-free and deterministic: it advances at exactly
30 Hz and reproduces identically from the same command stream, which is what the EditMode tests rely
on and what makes a balance run reproducible outside the Editor.

### Re-skinning without code

Every content's appearance can be overridden in the Inspector, in the asset the scene already wires.
Open `Assets/Data/Palette.asset` and unfold **Per-content visual overrides**. Each entry is off until
ticked, so an untouched palette draws exactly the shipped look - an override is purely additive.

* **Core** - tick *Override*, set *Source* to *Sprite*, drop in an image. The Core draws that image and
  still tints toward the damage colour as it takes hits, so the health readout survives the re-skin.
* **Items / Patches** - one entry per shape you want to re-skin; a shape with no entry, and every
  shape of a mineral added later, draws the built-in look. Give a shape a Sprite, or a different
  procedural silhouette, colour and size. The
  weakness icon on an enemy, the icon a machine draws for the shape it handles, and shots in flight
  all follow the same shape override, so a shape reads the same everywhere it appears.
* **Enemies / Machines** - one entry per building you want to re-skin, picked by kind. A Sprite
  replaces the body. A machine keeps its functional overlays (a turret's barrel and ammo icon, a
  decomposer's and splitter's port stubs), so its state stays legible. A building with no entry - and
  the mortar and the cutter are the shipped examples - draws the silhouette its behaviour already
  chooses, so it is the cannon's body a shade darker rather than a row in this list.

The shared colours, geometry and line widths above the override block still apply, and the material is
still `Assets/Art/Materials/UnlitVertexColor.mat`.

### Tuning and adding content without code

The scene also wires `Assets/Data/ContentDatabase.asset`: every cost, turret and enemy stat, shape
glyph and conversion recipe the simulation reads. Edit a value and it takes effect on the next Play -
no recompile. Every row is an **override**, so a kind with no row keeps the shipped value and the
asset is purely additive.

* **Retune** - change a cost, a fire interval, a drill's rate, an enemy's HP. The HUD prints its
  numbers from the same definitions, so what it claims and what the tick does cannot disagree. Each
  row also carries the content's stable `Id`, and a row that names one lands by that id rather than by
  the enum's ordinal - so inserting a value into `BuildKind` cannot silently retune a different
  building. Leave the `Id` empty on a hand-added row and it is keyed by the dropdown, as before.
* **Add a machine that reuses a behaviour** - add the enum value in `BuildKind`, add a row naming its
  `Id`, `Tile`, `Behavior` and `Cost`, and (for a converter) point its `Recipe Id` at a recipe row. No
  switch anywhere has to learn about it: the tick dispatches on the definition, and the appearance
  block needs no entry either - a building with no override draws the silhouette its behaviour chooses.
* **Add an enemy or a shape** - the same: an enum value plus a row.
* **Refresh the asset** - `FACET > Refresh Content Database from shipped table` rebuilds every row from
  the shipped numbers in place, which is how the committed asset learns about content added since it
  was made (and it keeps its GUID, so the scene's reference to it survives).
* Missing the asset? `FACET > Create Content Database` writes one filled with the shipped values, and
  `ContentDatabase.Default` is what the game runs on if the asset is empty or unwired.

The `FACET.Core` assembly still has `noEngineReferences`, so the ScriptableObject is only a carrier:
`ContentDatabaseAsset.ToCore()` converts it to the plain-C# `ContentDatabase` the simulation is handed,
which is what keeps a run deterministic and headless-testable.

### Maps and the campaign

`Maps.All` is the progression, in order, and it is still the one piece of content written in code -
a map is a list of rectangles, so a rectangle table in an asset would be more machinery than content.
Each map carries its own size, starting stockpile, mineral patches, entry points and wave table.

Clearing a map is not the end of a run any more: `SimulationDriver` builds the next map as a **new
`SimWorld`** and points every view at it (`MeshView.Rebind`), so the ground, the entry markers and the
camera bounds follow it without reloading the scene. The world is replaced rather than reset because a
world is one run: its fields are sized by its map, and it must stay a thing you can replay from its own
state. What carries over is one number, in `CampaignState` - which map, and how many are cleared.

That split is what makes "map 2" cheap to add: a new map is an entry in the table, and a new chain for
it is rows in the content table. `SimulationDriver`'s **Start Map Index** boots straight into any map
for testing.

### What just happened: the event stream

The simulation reports what it does on one stream (`SimWorld.Events`): a building went up or came
down, a segment jammed (and with which shape), a shot was fired, an enemy was hit or killed, a
circle banked, the Core took damage, a wave started or was cleared, the run ended. Nothing in the
simulation reads it back, so it cannot change the fight it describes - which is why every reaction to
the game hangs off it instead of off the simulation.

Two things use it today. **A kill bursts**: the enemy goes white-hot when a shot lands, and leaves an
expanding ring where it died - the first visual feedback in the project, and the reason to have a
stream at all. **The HUD reports the last second**: circles banked, shots, kills, and where the last
jam is, all read off the same stream, so the readout cannot disagree with the flash about what just
happened. A sound or a screen shake would be a third reader, not a third mechanism.

The buffer is a ring, so it always holds the most recent events and a reader needs no bookkeeping:
it reads the window it cares about and the present is guaranteed to be in there. `Dropped` counts
anything a very busy tick pushed out of the ring, so a reader can tell "nothing happened" from "I
fell behind" rather than guessing.

## Tests

| Suite | Where | What it covers |
|---|---|---|
| EditMode | `Assets/Tests/EditMode` | belts (27), the content table and its Editor carrier (19), production, drills and ore (11), waves and restart (9), the event stream (8), economy and the Core bank (8), turrets/jams (8), the sorter (7), the maps and the campaign (7), the second chain (7), splitters (6), pipes (5), determinism (6), the machine registry and port masks (5), a custom table driving the game (5), visual overrides (7), end-to-end balance (3), build settings (2) - 150 total |
| PlayMode smoke | `Assets/Tests/PlayMode` | the shipped scene boots with its Palette and content asset wired, the driver builds every view, advances the world across frames, and draws real geometry for what is on the map; a Sprite override reaches the view; a kill leaves a burst that then fades; clearing a map loads the next one under the same views - 9 total |

```bash
# EditMode + PlayMode, headless:
"D:/Program Files/Unity Editor/6000.3.23f1/Editor/Unity.exe" -batchmode -nographics \
  -projectPath . -runTests -testPlatform EditMode -testResults Logs/editmode.xml -logFile Logs/editmode.log
```

The balance tests are the ones to read first: `ScenarioTests.MapOne_HoldsItsWave_OnARealEconomy`
builds the reference defence on the real map inside the real budget — the starting stockpile plus
what an economy line physically belts home — and plays the wave to a win. The second test runs the
same layout with no decomposer: raw circles reach the cannon, the line jams, and the Core falls.

## Code map

```
Assets/Scripts/Core        engine-free simulation (no UnityEngine reference)
  TileGrid, BeltField      the map and the belt layer: one item per cell, hand-offs as a fixpoint
  Maps                     the map table: size, starting stockpile, patches, spawn points, waves
  ContentDatabase          the one table of what exists: shapes, recipes, machines, turrets, enemies, costs
  MachineField             the built things: where they are, what they hold, and the occupancy registry
  MachineDelivery          the belt-to-machine rule: which cells deliver in, which sides are outlets
  MachineSystem            one pass over the machines, dispatching each to its definition's behaviour
  DrillBehavior …          the behaviours: drill, converter (recipe-driven), pipe, splitter, sorter, turret
  SimTelemetry,            the tick's counters, and the context and interface a behaviour is handed - the
  MachineTickContext       seam that makes a new machine a row in the table rather than a new case
  SimEvents                the tick's event stream: what the simulation reports having done
  CampaignState            where the player is across maps: which map, and how many are cleared
  CoreSinkSystem           deliveries into the Core bank as spendable circles
  EnemyField               walks at the Core, or hits it; damage is the shape's business
  ProjectileField          shots in flight, so the shape that killed something is visible
  WaveDirector             the map's waves: the first held for the player, the countdowns after it, and what counts as a cleared wave
  BuildController          selection, drag-to-lay, machine aiming, jam-clearing delete, and the bill
  EconomyState             the stockpile: spend, refund, bank (costs come from the content table)
  Balance                  the global rules (core reach, projectile hit radius/lifetime) and a facade over the shipped table
  SimWorld                 the tick order and the run's state
Assets/Scripts/Unity       view layer (one renderer per concern, one mesh each, Palette for colour)
  Palette                  the whole look, including per-content Sprite/procedural overrides (VisualCatalog)
  VisualCatalog            the override types: VisualStyle, the kind-keyed per-content catalogs, and the shape helpers
  ContentDatabaseAsset     the Editor carrier for the content table; ToCore() converts it to the engine-free one
Assets/Scripts/Editor      the WebGL build tool and the content-asset creator
```

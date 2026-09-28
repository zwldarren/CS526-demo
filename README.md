# FACET — Prototype

A 2D top-down tower-defense prototype where **circles are money and half-circles are ammunition** —
and both are physical items riding conveyor belts. There is no abstract currency: a circle mined at
the edge of the map is not spendable until a belt delivers it into the Core, and a cannon fires only
the half-circles that physically reach it. A wrong-shape delivery jams the belt segment it lands on
and silences everything downstream, so the answer to pressure is re-plumbing the line.

Unity 6 (`6000.3.23f1`), 2D, built for WebGL. Current scope: map 1 ("Quarry", 80×48) · 1 wave ·
1 mineral (○) · 1 ammo (◠, split 1:2 by the decomposer) · 1 turret (Cannon) · 1 enemy (Spike).
The map table in `Maps.cs` is the expansion seam: a second map — more waves, more minerals — is a
new entry there, not new systems.

## Controls

| Input | Action |
|---|---|
| `WASD` / arrows | pan the camera |
| mouse wheel | zoom |
| `1`–`6` | select Belt / Drill / Decomposer / Pipe / Splitter / Cannon |
| `Q` / `E` | turn the placement ghost (the splitter and the decomposer have no facing to turn) |
| left mouse | place the selected building — **drag** to lay a belt run or to aim a machine |
| right mouse | delete what is under the cursor for a full refund; on a jammed segment it **clears the jam** instead |
| `Space` / `P` | pause / resume |
| `N` | start the next wave now — the first wave only ever arrives this way, and the HUD's button does the same thing |
| `R` | restart the run |

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
* **The cannon eats only ◠**, one shot per second at most. Its real fire rate is
  `min(1/s, what its lines deliver)` — damage is a throughput property of the layout, not a purchase.
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
* **The first wave waits for the player.** A run opens with as much time to build as they want: the
  first wave has no countdown and never arrives on its own — `N`, or the HUD's button, calls it, and
  only the defence that is actually finished is tested. Waves after it count down on the clock and
  walk in on their own, so the schedule is pressure the player meets later, not noise at the start.
* **Enemies walk over buildings.** Damage has to stay the only defence — but with build costs now
  real, whether buildings may block movement is worth revisiting on a later map.
* **Every drill needs a shape patch in the ground**, and every shape patch only accepts a drill.

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

## Tests

| Suite | Where | What it covers |
|---|---|---|
| EditMode | `Assets/Tests/EditMode` | belts (24), production and the decomposer (9), turrets/jams (8), economy and the Core bank (8), waves and restart (9), splitters (6), pipes (5), the simulation's determinism (6), end-to-end balance (3), build settings (2) |
| PlayMode smoke | `Assets/Tests/PlayMode` | the driver builds every view, advances the world across frames, and draws real geometry for what is on the map |

```bash
bash Tools/check.sh                 # compiles Core (engine-free) and Game without the Editor
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
  MachineField             the built things: where they are, which way they face
  MachineDelivery           the belt-to-machine rule: which cells deliver into a machine, which sides are outlets
  ProductionSystem         drills mine, decomposers split ○ → ◠◠ out through the belts pointing away
  TransitSystem            pipes carry over one tile, splitters balance by belt direction
  CoreSinkSystem           deliveries into the Core bank as spendable circles
  TurretSystem             eats its own shape, jams on any other, fires only what arrived
  EnemyField               walks at the Core, or hits it; damage is the shape's business
  ProjectileField          shots in flight, so the shape that killed something is visible
  WaveDirector             the map's waves: the first held for the player, the countdowns after it, and what counts as a cleared wave
  BuildController          selection, drag-to-lay, machine aiming, jam-clearing delete, and the bill
  EconomyState             the stockpile: spend, refund, bank
  Balance                  the numbers table: rates, costs, the turret and the enemy
  SimWorld                 the tick order and the run's state
Assets/Scripts/Unity       view layer (one renderer per concern, one mesh each, Palette for colour)
Assets/Scripts/Editor      the WebGL build tool
```

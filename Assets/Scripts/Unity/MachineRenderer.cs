using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every machine as one mesh. A turret's Aim and Armed state, a decomposer's split, a
    /// pipe's item in transit and a splitter's ports all change while the player is watching them,
    /// so this view rebuilds every frame rather than on a watched value. Because the mesh is
    /// rebuilt every frame, outline widths stay a true 2 px at any zoom.
    ///
    /// The readout each building owes the player:
    ///   drill       - a hollow frame, so the patch icon underneath stays the identity
    ///   decomposer  - the shape it holds: a circle mid-split, or the halves waiting to leave,
    ///                 plus a stub on every side a belt feeds or drains
    ///   pipe        - the tube across the jumped tile, and the item riding over it
    ///   splitter    - the hub, with a stub per live port read off the belts around it
    ///   turret      - the barrel, and the ammo icon whose grey-out is the "silenced" state
    /// </summary>
    [DefaultExecutionOrder(105)]
    public sealed class MachineRenderer : MeshView
    {
        private static readonly Dir[] Sides = { Dir.North, Dir.East, Dir.South, Dir.West };

        private readonly List<MachineSnapshot> _machines = new List<MachineSnapshot>();

        /// <summary>Scratch array: AppendPolygon wants a point list per shape, and newing one up
        /// per machine per frame is exactly the allocation the view layer must not do.</summary>
        private readonly Vector2[] _barrel = new Vector2[4];

        private Vector2[] _decomposerBody;
        private Vector2[] _turretBody;
        private Vector2[] _splitterHub;
        private ShapeOutlines _machineIcons;
        private ShapeOutlines _ammoIcons;
        private ShapeOutlines _pipeItemIcons;

        protected override Palette.Layer Layer => Colors.MachineLayer;

        protected override void OnInitialized()
        {
            // The decomposer's first vertex sits at 30 degrees so a flat edge faces up, matching
            // the shape patches the shape patch view lays in the ground.
            Palette.MachineLook look = Colors.Machines;
            _decomposerBody = ProcMesh.RegularPolygon(6, look.DecomposerRadius, 30f);
            _turretBody = ProcMesh.RegularPolygon(8, look.TurretRadius);
            _splitterHub = ProcMesh.RegularPolygon(4, look.SplitterHubHalfSize * 1.4143f, 45f);
            _machineIcons = ShapeOutlines.AtRadius(look.DecomposerIconRadius, Colors.CircleSides);
            _ammoIcons = ShapeOutlines.AtRadius(look.AmmoIconRadius, Colors.CircleSides);
            _pipeItemIcons = ShapeOutlines.AtRadius(look.PipeItemRadius, Colors.CircleSides);
        }

        /// <summary>Everything this view draws moves or changes state every tick, so it rebuilds
        /// every frame.</summary>
        protected override void Observe(in ViewFrame frame) => MarkDirty();

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Machines.GetMachines(_machines);
            float outlineWidth = OutlineWidth;

            for (int i = 0; i < _machines.Count; i++)
            {
                switch (_machines[i].Kind)
                {
                    case TileKind.Drill: AppendDrill(_machines[i]); break;
                    case TileKind.Decomposer: AppendDecomposer(_machines[i], outlineWidth); break;
                    case TileKind.Pipe: AppendPipe(_machines[i], outlineWidth); break;
                    case TileKind.Splitter: AppendSplitter(_machines[i], outlineWidth); break;
                    case TileKind.Turret: AppendTurret(_machines[i], outlineWidth); break;
                }
            }
        }

        /// <summary>A hollow frame plus an outlet chevron. No outline anywhere: an outline is baked
        /// at the current pixel width, but more importantly the patch icon underneath IS the drill's
        /// identity, so the frame only frames it.</summary>
        private void AppendDrill(in MachineSnapshot machine)
        {
            float x = machine.Cell.X;
            float y = machine.Cell.Y;
            Color body = Colors.BodyColor(TileKind.Drill);

            Palette.MachineLook look = Colors.Machines;
            float lo = look.FrameInset;
            float hiInner = 1f - look.FrameInset - look.FrameThickness;
            float hi = 1f - look.FrameInset;
            float thickness = look.FrameThickness;

            AppendRect(x + lo, y + lo, x + hi, y + lo + thickness, body);                    // bottom
            AppendRect(x + lo, y + hiInner, x + hi, y + hi, body);                            // top
            AppendRect(x + lo, y + lo + thickness, x + lo + thickness, y + hiInner, body);    // left
            AppendRect(x + hi - thickness, y + lo + thickness, x + hi, y + hiInner, body);    // right

            AppendChevron(machine.Cell, machine.Direction, body);
        }

        /// <summary>Hexagon body with the shape it holds as its icon - a circle while the split runs,
        /// the halves once they are waiting to leave, nothing - the idle state - when empty - plus a
        /// stub on every side the belts actually wired, so two outlets look like two outlets. There is
        /// no facing chevron: which sides are outlets is read off the belts, exactly as the tick reads
        /// them.</summary>
        private void AppendDecomposer(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = new Vector2(machine.Cell.X + 0.5f, machine.Cell.Y + 0.5f);
            Color body = Colors.BodyColor(TileKind.Decomposer);

            // 0.8 r is inside the hexagon's flat edge (the apothem is 0.866 r) and the cardinal sides are
            // mid-edge, so the body polygon drawn next covers each stub's inner end.
            AppendPortStubs(machine.Cell, centre, Colors.Machines.DecomposerRadius * 0.8f);

            AppendPolygon(_decomposerBody, centre, body, Colors.Outline, outlineWidth);

            Vector2[] icon = _machineIcons[machine.Shape];
            if (icon != null)
                AppendPolygon(icon, centre, Colors.ShapeColor(machine.Shape), Colors.Outline, outlineWidth);
        }

        /// <summary>The crossing piece: two rails running from this cell's entry edge across the
        /// jumped tile to the landing belt, with a collar at each end - the collars and the gap under
        /// the tube are what read as "over". The item in transit rides the tube at its work fraction,
        /// so a backed-up pipe shows its item parked over the landing belt.</summary>
        private void AppendPipe(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = new Vector2(machine.Cell.X + 0.5f, machine.Cell.Y + 0.5f);
            Vec2 forward2 = machine.Direction.ToVec();
            var forward = new Vector2(forward2.X, forward2.Y);
            var right = new Vector2(-forward.y, forward.x);
            Color body = Colors.BodyColor(TileKind.Pipe);

            Palette.MachineLook look = Colors.Machines;
            Vector2 entry = centre - forward * 0.5f;
            Vector2 landing = centre + forward * 1.5f;

            AppendLane(entry + right * look.PipeRailOffset, landing + right * look.PipeRailOffset,
                look.PipeRailWidth, body);
            AppendLane(entry - right * look.PipeRailOffset, landing - right * look.PipeRailOffset,
                look.PipeRailWidth, body);

            float ct = look.PipeCollarThickness;
            float cw = look.PipeCollarHalfWidth;
            AppendLane(entry - forward * ct + right * cw, entry - forward * ct - right * cw, ct, body);
            AppendLane(landing + right * cw, landing - right * cw, ct, body);

            AppendChevron(machine.Cell, machine.Direction, body);

            Vector2[] item = _pipeItemIcons[machine.Shape];
            if (item != null)
            {
                Vector2 at = entry + forward * (2f * machine.Work);
                AppendPolygon(item, at, Colors.ShapeColor(machine.Shape), Colors.Outline, outlineWidth);
            }
        }

        /// <summary>The hub plus one stub per live port, read off the belts around it exactly the
        /// way the simulation does. The buffered item sits at the hub's centre.</summary>
        private void AppendSplitter(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = new Vector2(machine.Cell.X + 0.5f, machine.Cell.Y + 0.5f);
            Color body = Colors.BodyColor(TileKind.Splitter);

            AppendPortStubs(machine.Cell, centre, Colors.Machines.SplitterHubHalfSize);

            AppendPolygon(_splitterHub, centre, body, Colors.Outline, outlineWidth);

            Vector2[] icon = _machineIcons[machine.Shape];
            if (icon != null)
                AppendPolygon(icon, centre, Colors.ShapeColor(machine.Shape), Colors.Outline, outlineWidth);
        }

        /// <summary>One stub per live port on a cell: a belt pointing in gets a stub with an inward
        /// chevron, a belt pointing away gets one with an outward chevron, a side with no belt (or a
        /// belt running past) gets nothing. Shared by the splitter and the decomposer - the two
        /// machines whose ports *are* the belts around them - so the picture cannot drift from the
        /// rule. Both draw their body after this, which is what hides each stub's inner end.
        ///
        /// SplitterStubHalfWidth is the palette's one stub width; it keeps its name because it is the
        /// same stub on both machines, and renaming a serialized field would silently drop the value
        /// out of Assets/Data/Palette.asset.</summary>
        private void AppendPortStubs(Int2 cell, Vector2 centre, float innerRadius)
        {
            Palette.MachineLook look = Colors.Machines;

            for (int i = 0; i < Sides.Length; i++)
            {
                if (!World.Belts.TryGet(cell + Sides[i].Offset(), out BeltState belt)) continue;

                Vec2 side2 = Sides[i].ToVec();
                var side = new Vector2(side2.X, side2.Y);
                var stubCentre = centre + side * 0.5f * (1f - innerRadius);

                if (belt.Direction == Sides[i].Opposite())          // in: belt points at the machine
                {
                    AppendLane(centre + side * 0.5f, centre + side * innerRadius,
                        look.SplitterStubHalfWidth, Colors.Belt);
                    AppendChevron(stubCentre, Sides[i].Opposite(), look.ChevronBack,
                        look.ChevronHalfWidth, look.ChevronLength, Colors.BeltArrow);
                }
                else if (belt.Direction == Sides[i])                // out: belt points away
                {
                    AppendLane(centre + side * innerRadius, centre + side * 0.5f,
                        look.SplitterStubHalfWidth, Colors.Belt);
                    AppendChevron(stubCentre, Sides[i], look.ChevronBack,
                        look.ChevronHalfWidth, look.ChevronLength, Colors.BeltArrow);
                }
            }
        }

        /// <summary>Octagon body, barrel along Aim, then the ammo icon. Draw order matters: barrel
        /// after body so it reads as the gun, icon last so the armed/silenced colour is never
        /// covered. The icon's grey is the "silenced" state - the single most important readout,
        /// because a turret with no ammo is a dead building.</summary>
        private void AppendTurret(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = new Vector2(machine.Cell.X + 0.5f, machine.Cell.Y + 0.5f);

            AppendPolygon(_turretBody, centre, Colors.TurretBody, Colors.Outline, outlineWidth);

            AppendBarrel(machine, centre, outlineWidth);

            // A turret's diet is always a real shape (Balance.TurretSpecs); the indexer is total, so
            // the null branch is written out rather than assumed away.
            Vector2[] ammo = _ammoIcons[machine.Shape];
            if (ammo != null)
            {
                Color ammoColor = machine.Armed ? Colors.ShapeColor(machine.Shape) : Colors.MachineIdle;
                AppendPolygon(ammo, centre, ammoColor, Colors.Outline, outlineWidth);
            }
        }

        /// <summary>One quad from the body centre out along the aim. Aim is already normalized by
        /// the tick; a zero Aim (a turret that has never seen an enemy) falls back to its rest
        /// direction so the barrel still points somewhere sane.</summary>
        private void AppendBarrel(in MachineSnapshot machine, Vector2 centre, float outlineWidth)
        {
            Vec2 aim = machine.Aim;
            Vector2 dir = new Vector2(aim.X, aim.Y);
            if (dir.sqrMagnitude < 1e-8f)
            {
                Vec2 rest = machine.Direction.ToVec();
                dir = new Vector2(rest.X, rest.Y);
            }
            var right = new Vector2(-dir.y, dir.x);

            Vector2 tip = centre + dir * Colors.Machines.BarrelLength;
            float halfWidth = Colors.Machines.BarrelHalfWidth;
            var nearRight = centre + right * halfWidth;
            var nearLeft = centre - right * halfWidth;
            var tipRight = tip + right * halfWidth;
            var tipLeft = tip - right * halfWidth;

            // Wound CCW: starting at the tip's left corner keeps every edge cross positive for
            // any aim angle, the same trick BeltRenderer's chevron uses.
            _barrel[0] = tipLeft;
            _barrel[1] = tipRight;
            _barrel[2] = nearRight;
            _barrel[3] = nearLeft;

            AppendPolygon(_barrel, Vector2.zero, Colors.TurretBody, Colors.Outline, outlineWidth);
        }

        private void AppendChevron(Int2 cell, Dir direction, Color color)
        {
            Vec2 forward2 = direction.ToVec();
            var forward = new Vector2(forward2.X, forward2.Y);
            var centre = new Vector2(cell.X + 0.5f, cell.Y + 0.5f) + forward * Colors.Machines.ChevronOffset;

            Palette.MachineLook look = Colors.Machines;
            AppendChevron(centre, direction, look.ChevronBack, look.ChevronHalfWidth,
                look.ChevronLength, color);
        }
    }
}

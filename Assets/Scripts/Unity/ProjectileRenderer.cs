using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every shot in flight as one mesh, rebuilt each frame because shots never stop moving.
    /// A kill should be visibly a shape that left a belt and flew to its target, so each shot is
    /// drawn as its ammunition shape at a small radius. Positions are interpolated between the
    /// last two simulation ticks with the frame's Alpha, so flight is smooth at any frame rate
    /// instead of stepping at 30 Hz.
    ///
    /// These shapes carry no outline, unlike the items on belts: at projectile size the outline
    /// would be more pixels than the shape itself, and the colour alone is the message here.
    /// </summary>
    [DefaultExecutionOrder(114)]
    public sealed class ProjectileRenderer : MeshView
    {
        private readonly List<ProjectileSnapshot> _shots = new List<ProjectileSnapshot>();
        private ShapeOutlines _outlines;

        protected override Palette.Layer Layer => Colors.ProjectileLayer;

        protected override void OnInitialized()
        {
            _outlines = ShapeOutlines.AtRadius(Colors.Projectiles.Radius, Colors.CircleSides);
        }

        /// <summary>Shots are always moving, so this view rebuilds every frame.</summary>
        protected override void Observe(in ViewFrame frame) => MarkDirty();

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Projectiles.GetProjectiles(_shots);

            for (int i = 0; i < _shots.Count; i++)
            {
                ProjectileSnapshot shot = _shots[i];
                Vector2[] points = _outlines[shot.Ammo];
                if (points == null) continue;

                // No outline: at 0.13 tiles the outline would be more pixels than the shape, and
                // the colour alone carries the message.
                Vec2 p = Vec2.Lerp(shot.PreviousPosition, shot.Position, frame.Alpha);
                AppendPolygon(points, new Vector2(p.X, p.Y),
                    Colors.ShapeColor(shot.Ammo), Colors.ShapeColor(shot.Ammo), 0f);
            }
        }
    }
}

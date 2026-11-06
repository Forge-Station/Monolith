using System.Numerics;
using Content.Client._WF.Lattice;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private static readonly Color WfPhantomColor = Color.FromHex("#c8c8c8");

    /// <summary>A contact's radar label, garbled if it or the viewer is out past the Lattice's hold.</summary>
    private string? WfLatticeGarble(string? label, EntityUid grid)
    {
        if (label == null)
            return null;

        var map = EntManager.GetComponent<TransformComponent>(grid).MapUid;
        return EntManager.System<WFLatticeViewSystem>().Garble(label, map, _transform.GetWorldPosition(grid), grid.Id);
    }

    private static readonly Color WfDeadZoneColor = Color.FromHex("#07020a").WithAlpha(0.93f);
    private static readonly Color WfDeadZoneStatic = Color.FromHex("#5a1030").WithAlpha(0.5f);

    private const int WfDeadZoneSteps = 16;
    private readonly Vector2[] _wfDeadZoneFan = new Vector2[WfDeadZoneSteps + 2];

    /// <summary>Seconds each radar dead zone lasts before it moves.</summary>
    private const float WfDeadZoneSeconds = 25f;

    /// <summary>In the Coda the radar goes blind in wedges that wander round the scope, hiding whatever is in them.</summary>
    private void DrawWfLatticeDeadZones(DrawingHandleScreen handle)
    {
        if (!EntManager.System<WFLatticeViewSystem>().InHazardBand())
            return;

        var time = (float) IoCManager.Resolve<IGameTiming>().RealTime.TotalSeconds;
        var dark = WfDeadZoneColor;
        var noise = WfDeadZoneStatic;
        var reach = PixelSize.Length;
        var centre = MidPointVector;
        for (var zone = 0; zone < 2; zone++)
        {
            var phase = time / WfDeadZoneSeconds + zone * 0.5f;
            var cycle = (int) MathF.Floor(phase);
            var t = phase - cycle;
            var fade = Math.Clamp(MathF.Min(t, 1f - t) * WfDeadZoneSeconds / 3f, 0f, 1f);
            if (fade <= 0f)
                continue;

            var seed = (uint) (cycle * 7919 + zone * 104729) * 2654435761u;
            var start = (seed % 3600) / 10f * MathF.PI / 180f + time * 0.05f;
            var width = (35f + (seed >> 12) % 45) * MathF.PI / 180f;
            const int steps = WfDeadZoneSteps;
            var fan = _wfDeadZoneFan;
            fan[0] = centre;
            for (var i = 0; i <= steps; i++)
            {
                var angle = start + width * i / steps;
                fan[i + 1] = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * reach;
            }

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, fan, dark.WithAlpha(dark.A * fade));

            // A few lines of static crawling through it.
            for (var line = 0; line < 6; line++)
            {
                var angle = start + width * (((seed >> (line * 3)) % 100) / 100f + MathF.Sin(time * 3f + line) * 0.02f);
                var from = 20f + ((seed >> line) % 60) * UIScale;
                var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                handle.DrawLine(centre + dir * from, centre + dir * (from + 40f * UIScale), noise.WithAlpha(noise.A * fade));
            }
        }
    }

    /// <summary>Radar contacts that only this player sees.</summary>
    private void DrawWfLatticePhantoms(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        if (map is not { } mapUid)
            return;

        var mapId = EntManager.GetComponent<TransformComponent>(mapUid).MapID;
        var view = EntManager.System<WFLatticeViewSystem>();
        foreach (var (position, _, seed) in EntManager.System<WFLatticeHallucinationSystem>().Blips)
        {
            if (position.MapId != mapId)
                continue;

            var at = Vector2.Transform(position.Position, worldToView);
            if (at.X < 0f || at.Y < 0f || at.X > PixelSize.X || at.Y > PixelSize.Y)
                continue;

            handle.DrawCircle(at, 3f * UIScale, WfPhantomColor);
            var label = view.Garble(Loc.GetString("wf-lattice-phantom-contact"), mapUid, position.Position, seed);
            handle.DrawString(Font, at + new Vector2(6f, -6f) * UIScale, label, UIScale * 0.8f, WfPhantomColor);
        }
    }
}

using Content.Shared._WF.Encounters;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;
using System.Numerics;
using Content.Client._WF.Encounters;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>The labels placed so far this frame, so ships of one encounter do not draw over each other.</summary>
    private readonly List<Box2> _encounterLabels = new();

    /// <summary>The ships of one side of one encounter, gathered for a frame.</summary>
    private readonly Dictionary<(NetEntity Encounter, string Side), (Vector2 Sum, int Count, bool Named, bool OnScope, Color Color, WFEncounterIcon Icon, WFEncounterCategory Category, float Warn, float Attack)> _encounterGroups = new();

    /// <summary>Where each side's ships were drawn this frame, for the spread of its zone envelope.</summary>
    private readonly Dictionary<(NetEntity Encounter, string Side), List<Vector2>> _encounterPoints = new();

    private static readonly ResPath MarkerGlyphs = new("/Textures/_WF/Encounters/markers.rsi");
    private readonly Dictionary<WFEncounterIcon, Texture> _encounterGlyphs = new();

    /// <summary>The texture for a glyph, loaded once.</summary>
    private Texture EncounterGlyph(WFEncounterIcon icon)
    {
        if (!_encounterGlyphs.TryGetValue(icon, out var texture))
        {
            texture = EntManager.System<SpriteSystem>().Frame0(new SpriteSpecifier.Rsi(MarkerGlyphs, icon.ToString().ToLowerInvariant()));
            _encounterGlyphs[icon] = texture;
        }

        return texture;
    }

    /// <summary>
    /// Marks the visible encounters in the sector. A ship on the scope gets a diamond; the ships of one side share a
    /// single name and a single pair of zone rings, drawn round the side as a whole: its widest ranges plus the spread
    /// of its ships, so a dozen hulls in a battle make one envelope and not a dozen overlapping circles. A side with
    /// no ship on the scope gets one bare arrow at the rim, however many ships it has, so far contacts don't crowd it.
    /// </summary>
    private void DrawEncounterMarkers(DrawingHandleScreen handle, TransformComponent consoleXform, Matrix3x2 worldToView)
    {
        var system = EntManager.System<WFEncounterMarkerClientSystem>();
        if (system.Markers.Count == 0)
            return;

        var centre = MidPointVector;
        var rim = MathF.Min(PixelSize.X, PixelSize.Y) / 2f - 20f * UIScale;
        var size = 6f * UIScale;
        _encounterLabels.Clear();
        _encounterGroups.Clear();
        _encounterPoints.Clear();
        foreach (var marker in system.Markers)
        {
            if (marker.Map != consoleXform.MapID)
                continue;

            var key = (marker.Encounter, marker.Side);
            var color = marker.Color ?? WFEncounterColors.Category(marker.Category);
            if (!_encounterGroups.TryGetValue(key, out var group))
                group = (Vector2.Zero, 0, false, false, color, WFEncounterIcon.Category, marker.Category, 0f, 0f);

            var point = Vector2.Transform(system.GetPosition(marker), worldToView);
            var onScope = (point - centre).Length() <= rim;
            // Only a ship with zones of its own widens its side's rings: a wreck the server sent without ranges does not.
            if (marker.WarnRange > 0f || marker.AttackRange > 0f)
            {
                if (!_encounterPoints.TryGetValue(key, out var points))
                    _encounterPoints[key] = points = new List<Vector2>();
                points.Add(point);
            }
            if (onScope)
            {
                handle.DrawLine(point + new Vector2(0f, -size), point + new Vector2(size, 0f), color);
                handle.DrawLine(point + new Vector2(size, 0f), point + new Vector2(0f, size), color);
                handle.DrawLine(point + new Vector2(0f, size), point + new Vector2(-size, 0f), color);
                handle.DrawLine(point + new Vector2(-size, 0f), point + new Vector2(0f, -size), color);
                if (!group.Named)
                {
                    // Above the ship: its own IFF label hangs below and to the right, and the admin crew tag sits between.
                    var at = PlaceEncounterLabel(point + new Vector2(10f, -54f) * UIScale, handle.GetDimensions(Font, marker.Name, UIScale * 0.8f));
                    handle.DrawString(Font, at, marker.Name, UIScale * 0.8f, color);
                }
            }

            // Any ship of the side with a glyph of its own names the side's; the category's glyph is only a fallback at drawing.
            var icon = group.Icon != WFEncounterIcon.Category ? group.Icon : marker.Icon;
            _encounterGroups[key] = (group.Sum + point, group.Count + 1, group.Named || onScope, group.OnScope || onScope, color, icon, group.Category,
                MathF.Max(group.Warn, marker.WarnRange), MathF.Max(group.Attack, marker.AttackRange));
        }

        foreach (var (key, group) in _encounterGroups)
        {
            // The side's zones as one envelope: its widest ranges out from the middle of its armed ships, plus their spread.
            if (!_encounterPoints.TryGetValue(key, out var zoned) || zoned.Count == 0)
                continue;

            var middle = Vector2.Zero;
            foreach (var point in zoned)
            {
                middle += point;
            }

            middle /= zoned.Count;
            var spread = 0f;
            foreach (var point in zoned)
            {
                spread = MathF.Max(spread, (point - middle).Length());
            }

            DrawEncounterZone(handle, middle, group.Warn, spread, Color.FromHex("#ffd23f"), system.WarnLabel);
            DrawEncounterZone(handle, middle, group.Attack, spread, Color.FromHex("#ff4b4b"), system.AttackLabel);
        }

        foreach (var group in _encounterGroups.Values)
        {
            if (group.OnScope)
                continue;

            var offset = group.Sum / group.Count - centre;
            if (offset.LengthSquared() < 1f)
                continue;

            var direction = Vector2.Normalize(offset);
            var side = new Vector2(-direction.Y, direction.X);
            var tip = centre + direction * rim;
            var back = tip - direction * size * 2f;
            handle.DrawLine(tip, back + side * size, group.Color);
            handle.DrawLine(tip, back - side * size, group.Color);
            handle.DrawLine(back + side * size, back - side * size, group.Color);
            // The side's glyph just inside the arrow, so a skull at the rim is a raider and a crate a freighter.
            var glyph = 16f * UIScale;
            var glyphCentre = back - direction * (glyph * 0.5f + 2f * UIScale);
            var icon = group.Icon != WFEncounterIcon.Category ? group.Icon : EncounterGlyphFor(group.Category);
            handle.DrawTextureRect(EncounterGlyph(icon), UIBox2.FromDimensions(glyphCentre - new Vector2(glyph / 2f), new Vector2(glyph)), group.Color);
        }
    }

    /// <summary>A category's own glyph.</summary>
    private static WFEncounterIcon EncounterGlyphFor(WFEncounterCategory category)
    {
        return category switch
        {
            WFEncounterCategory.Patrol => WFEncounterIcon.Shield,
            WFEncounterCategory.Threat => WFEncounterIcon.Skull,
            WFEncounterCategory.Distress => WFEncounterIcon.Cross,
            _ => WFEncounterIcon.Crate,
        };
    }

    /// <summary>Moves a label down until it clears the ones already placed, and records where it ends up.</summary>
    private Vector2 PlaceEncounterLabel(Vector2 position, Vector2 size)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var box = Box2.FromDimensions(position, size);
            var clear = true;
            foreach (var placed in _encounterLabels)
            {
                if (!placed.Intersects(box))
                    continue;

                clear = false;
                break;
            }

            if (clear)
                break;

            position.Y += size.Y + 2f * UIScale;
        }

        _encounterLabels.Add(Box2.FromDimensions(position, size));
        return position;
    }

    /// <summary>
    /// A ring round a side at a zone's range, widened by the spread of its ships so it wraps every one of their zones,
    /// named at its top when any of it is on the scope.
    /// </summary>
    private void DrawEncounterZone(DrawingHandleScreen handle, Vector2 centre, float range, float spread, Color color, string label)
    {
        if (range <= 0f)
            return;

        var radius = range * MinimapScale + spread;
        if (centre.X + radius < 0f || centre.Y + radius < 0f || centre.X - radius > PixelSize.X || centre.Y - radius > PixelSize.Y)
            return;

        handle.DrawCircle(centre, radius, color.WithAlpha(0.8f), false);
        if (label.Length == 0)
            return;

        var dimensions = handle.GetDimensions(Font, label, UIScale * 0.7f);
        handle.DrawString(Font, centre + new Vector2(-dimensions.X / 2f, -radius - dimensions.Y - 2f * UIScale), label, UIScale * 0.7f, color);
    }
}

using System.Numerics;
using Content.Client.Pinpointer.UI;
using Content.Shared._Forge.Turrets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._Forge.Turrets;

/// <summary>
/// Station nav map with markers for this faction's turrets. A click opens that turret's actions.
/// People are not drawn.
/// </summary>
public sealed class TurretNavMapControl : NavMapControl
{
    public event Action<TurretMapMarker, Vector2>? OnTurretSelected;

    private readonly List<TurretMapMarker> _markers = new();
    private Vector2 _leftPress;
    private bool _leftPressed;

    public void ApplyAccent(Color accent)
    {
        WallColor = accent;
        TileColor = new Color(accent.R * 0.22f, accent.G * 0.18f, accent.B * 0.14f);
        BackgroundColor = Color.FromSrgb(TileColor.WithAlpha(0.9f));
    }

    public void SetMarkers(IEnumerable<TurretMapMarker> markers)
    {
        _markers.Clear();
        _markers.AddRange(markers);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _leftPressed = true;
        _leftPress = args.PointerLocation.Position;
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick || !_leftPressed)
            return;

        _leftPressed = false;
        if ((args.PointerLocation.Position - _leftPress).Length() > 6f)
            return;

        var local = args.PointerLocation.Position - GlobalPixelPosition;
        var offset = GetOffset();
        var hitRadius = MathF.Max(16f, MarkerRadius() * 2.4f);
        TurretMapMarker? hit = null;
        var best = hitRadius;

        foreach (var marker in _markers)
        {
            var position = ScalePosition(new Vector2(marker.Position.X - offset.X, -(marker.Position.Y - offset.Y)));
            var distance = (position - local).Length();
            if (distance >= best)
                continue;

            best = distance;
            hit = marker;
        }

        if (hit != null)
            OnTurretSelected?.Invoke(hit.Value, MarkerUiPosition(hit.Value));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (MapUid == null)
            return;

        var offset = GetOffset();
        var radius = MarkerRadius();
        foreach (var marker in _markers)
        {
            var position = ScalePosition(new Vector2(marker.Position.X - offset.X, -(marker.Position.Y - offset.Y)));
            handle.DrawCircle(position, radius + 1.6f, Color.Black);
            handle.DrawCircle(position, radius, marker.Color);
        }
    }

    private Vector2 MarkerUiPosition(TurretMapMarker marker)
    {
        var offset = GetOffset();
        var pixelLocal = ScalePosition(new Vector2(marker.Position.X - offset.X, -(marker.Position.Y - offset.Y)));
        return GlobalPosition + pixelLocal / UIScale;
    }

    private float MarkerRadius()
    {
        return MathF.Max(3.5f, MathF.Sqrt(MinimapScale) * 3.2f);
    }
}

public readonly record struct TurretMapMarker(NetEntity Entity, string Name, Vector2 Position, bool Enabled, TurretDoctrine Doctrine, Color Color);

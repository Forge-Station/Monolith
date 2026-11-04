using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>What a round dashboard button draws when it has no texture of its own.</summary>
public enum WFVehicleGlyph : byte
{
    None,
    Key,
    Eject,
    Headlights,
    Brake,
    Horn,
    Parts,
    Condition,
    Crosshair,
    Ammo,
    TurretLock,
    Power,
}

/// <summary>A round dashboard button: a texture or a drawn glyph inside a ring that shows its state.</summary>
public sealed class WFVehicleRoundButton : ContainerButton
{
    /// <summary>Diameter in UI units.</summary>
    public const float Size = 44;

    public WFVehicleGlyph Glyph;
    public Texture? Icon;

    /// <summary>Lit: the ring and glyph glow in <see cref="LitColor"/>, for a switch that is on.</summary>
    public bool Lit;
    public Color? LitColor;

    /// <summary>For the key: whether one is in, and turned to on.</summary>
    public bool KeyInserted;
    public bool KeyOn;

    public WFVehicleRoundButton(WFVehicleGlyph glyph = WFVehicleGlyph.None, string? tooltip = null)
    {
        Glyph = glyph;
        SetSize = new Vector2(Size, Size);
        if (tooltip != null)
            ToolTip = Loc.GetString(tooltip);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var skin = WFInstrumentTheme.Skin;
        var center = PixelSizeBox.Center;
        var radius = MathF.Min(PixelWidth, PixelHeight) / 2f - UIScale;
        var lit = LitColor ?? skin.Good;
        var ring = Disabled ? skin.TextMuted.WithAlpha(0.5f)
            : Lit || Pressed ? lit
            : DrawMode == DrawModeEnum.Hover ? skin.Accent
            : skin.EdgeLight;

        handle.DrawCircle(center + new Vector2(0, 2 * UIScale), radius, Color.Black.WithAlpha(0.6f));
        handle.DrawCircle(center, radius, ring);
        handle.DrawCircle(center, radius - 2 * UIScale, DrawMode == DrawModeEnum.Pressed ? skin.EdgeSoft : skin.Ink);

        var ink = Disabled ? skin.TextMuted.WithAlpha(0.6f) : Lit || Pressed ? lit : skin.Text;
        if (Icon != null)
        {
            var side = radius * 1.25f;
            var box = UIBox2.FromDimensions(center - new Vector2(side / 2), new Vector2(side));
            handle.DrawTextureRect(Icon, box, Disabled ? Color.White.WithAlpha(0.4f) : Color.White);
            return;
        }

        var unit = radius / 10f;
        switch (Glyph)
        {
            case WFVehicleGlyph.Key:
                DrawKey(handle, center, unit, ink, skin);
                break;
            case WFVehicleGlyph.Eject:
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, new[]
                {
                    center + new Vector2(-5, 1) * unit, center + new Vector2(5, 1) * unit, center + new Vector2(0, -5) * unit,
                }, ink);
                handle.DrawRect(UIBox2.FromDimensions(center + new Vector2(-5, 3) * unit, new Vector2(10, 2) * unit), ink);
                break;
            case WFVehicleGlyph.Power:
                // The power symbol: a ring broken at the top, a bar through the gap.
                for (var i = 0; i < 20; i++)
                {
                    var from = MathF.PI * (0.65f + i * 1.7f / 20f);
                    var to = MathF.PI * (0.65f + (i + 1) * 1.7f / 20f);
                    handle.DrawLine(center + new Vector2(MathF.Sin(from), -MathF.Cos(from)) * 5 * unit,
                        center + new Vector2(MathF.Sin(to), -MathF.Cos(to)) * 5 * unit, ink);
                }
                handle.DrawRect(UIBox2.FromDimensions(center + new Vector2(-0.75f, -6.5f) * unit, new Vector2(1.5f, 6) * unit), ink);
                break;
            case WFVehicleGlyph.Headlights:
                // A lamp facing right, with three beams.
                handle.DrawCircle(center + new Vector2(-2, 0) * unit, 4 * unit, ink);
                handle.DrawRect(UIBox2.FromDimensions(center + new Vector2(-6, -4) * unit, new Vector2(4, 8) * unit), skin.Ink);
                for (var i = -1; i <= 1; i++)
                    handle.DrawLine(center + new Vector2(3, i * 3) * unit, center + new Vector2(7, i * 4) * unit, ink);
                break;
            case WFVehicleGlyph.Brake:
                Letter(handle, "P", center, radius, ink);
                handle.DrawCircle(center, 6.5f * unit, ink, false);
                break;
            case WFVehicleGlyph.Horn:
                // A trumpet's bell and mouthpiece.
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, new[]
                {
                    center + new Vector2(-4, -1.5f) * unit, center + new Vector2(5, -5) * unit, center + new Vector2(5, 5) * unit,
                    center + new Vector2(-4, -1.5f) * unit, center + new Vector2(5, 5) * unit, center + new Vector2(-4, 1.5f) * unit,
                }, ink);
                handle.DrawRect(UIBox2.FromDimensions(center + new Vector2(-7, -1) * unit, new Vector2(3, 2) * unit), ink);
                break;
            case WFVehicleGlyph.Condition:
                // A cross on a plate: how damaged the vehicle is.
                handle.DrawRect(UIBox2.FromDimensions(center + new Vector2(-1.5f, -6) * unit, new Vector2(3, 12) * unit), ink);
                handle.DrawRect(UIBox2.FromDimensions(center + new Vector2(-6, -1.5f) * unit, new Vector2(12, 3) * unit), ink);
                break;
            case WFVehicleGlyph.Crosshair:
                handle.DrawCircle(center, 5 * unit, ink, false);
                handle.DrawLine(center + new Vector2(-7, 0) * unit, center + new Vector2(-3, 0) * unit, ink);
                handle.DrawLine(center + new Vector2(3, 0) * unit, center + new Vector2(7, 0) * unit, ink);
                handle.DrawLine(center + new Vector2(0, -7) * unit, center + new Vector2(0, -3) * unit, ink);
                handle.DrawLine(center + new Vector2(0, 3) * unit, center + new Vector2(0, 7) * unit, ink);
                break;
            case WFVehicleGlyph.Ammo:
                // Two rounds standing side by side.
                for (var i = -1; i <= 1; i += 2)
                {
                    var x = i * 2.5f;
                    handle.DrawRect(UIBox2.FromDimensions(center + new Vector2(x - 1.5f, -2) * unit, new Vector2(3, 7) * unit), ink);
                    handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, new[]
                    {
                        center + new Vector2(x - 1.5f, -2) * unit, center + new Vector2(x + 1.5f, -2) * unit, center + new Vector2(x, -6) * unit,
                    }, ink);
                }
                break;
            case WFVehicleGlyph.TurretLock:
                // A padlock: shut while the turret holds its bearing, open while it turns with the hull.
                handle.DrawRect(UIBox2.FromDimensions(center + new Vector2(-4.5f, -1) * unit, new Vector2(9, 7) * unit), ink);
                var shackle = Lit ? 0f : -1.5f;
                handle.DrawLine(center + new Vector2(-3, -1) * unit, center + new Vector2(-3, -4 + shackle) * unit, ink);
                handle.DrawLine(center + new Vector2(3, -1 + (Lit ? 0f : -2f)) * unit, center + new Vector2(3, -4 + shackle) * unit, ink);
                handle.DrawLine(center + new Vector2(-3, -4 + shackle) * unit, center + new Vector2(0, -6 + shackle) * unit, ink);
                handle.DrawLine(center + new Vector2(0, -6 + shackle) * unit, center + new Vector2(3, -4 + shackle) * unit, ink);
                handle.DrawRect(UIBox2.FromDimensions(center + new Vector2(-0.75f, 1.5f) * unit, new Vector2(1.5f, 2.5f) * unit), skin.Ink);
                break;
            case WFVehicleGlyph.Parts:
                // A wrench.
                handle.DrawLine(center + new Vector2(-5, 5) * unit, center + new Vector2(3, -3) * unit, ink);
                handle.DrawLine(center + new Vector2(-4, 6) * unit, center + new Vector2(4, -2) * unit, ink);
                handle.DrawCircle(center + new Vector2(4, -4) * unit, 3 * unit, ink, false);
                break;
        }
    }

    private void DrawKey(DrawingHandleScreen handle, Vector2 center, float unit, Color ink, WolfgateSkin skin)
    {
        if (!KeyInserted)
        {
            // An empty slot.
            handle.DrawRect(UIBox2.FromDimensions(center + new Vector2(-1, -5) * unit, new Vector2(2, 10) * unit), ink.WithAlpha(0.7f));
            return;
        }

        // Upright when off, a quarter turn clockwise when on.
        var along = KeyOn ? Vector2.UnitX : -Vector2.UnitY;
        var side = new Vector2(-along.Y, along.X);
        var head = center + along * 3 * unit;
        handle.DrawCircle(head, 3.5f * unit, ink);
        handle.DrawCircle(head, 1.5f * unit, skin.Ink);
        var tip = center - along * 6 * unit;
        handle.DrawLine(head, tip, ink);
        handle.DrawLine(head + side * unit, tip + side * unit, ink);
        for (var i = 1; i <= 2; i++)
        {
            var tooth = tip + along * i * 2 * unit;
            handle.DrawLine(tooth, tooth + side * 2.5f * unit, ink);
        }
    }

    private void Letter(DrawingHandleScreen handle, string text, Vector2 center, float radius, Color ink)
    {
        var font = IoCManager.Resolve<IResourceCache>().GetFont(WFInstrumentTheme.Skin.MenuFonts, 13);
        var box = UIBox2.FromDimensions(center - new Vector2(radius * 0.6f), new Vector2(radius * 1.2f));
        WFInstrumentText.DrawFitted(handle, font, text, box, UIScale, ink);
    }
}

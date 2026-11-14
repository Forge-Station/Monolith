using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.CombatConsole;

/// <summary>Draws a machined key cap with an optional recessed rocker switch.</summary>
public sealed class WFConsoleStyleBox : StyleBox
{
    private readonly bool _toggle;
    private readonly bool _pressed;
    private readonly bool _disabled;
    private readonly bool _danger;
    private readonly bool _hover;
    private readonly bool _accent;
    private readonly bool _compact;
    private readonly BaseButton? _disabledSwitch;

    public WFConsoleStyleBox(string face, bool toggle, bool pressed, bool disabled = false, BaseButton? disabledSwitch = null, bool compact = false)
    {
        _compact = compact;
        _pressed = pressed;
        _disabled = disabled;
        _danger = face.Contains("red");
        _hover = face.Contains("hover");
        _accent = face.Contains("amber");
        _disabledSwitch = disabledSwitch;
        _toggle = toggle;
        SetContentMarginOverride(Margin.All, compact ? 3 : 7);
        if (toggle)
            SetContentMarginOverride(Margin.Left, 30);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var skin = WFInstrumentTheme.Skin;
        var pressed = _disabledSwitch?.Pressed ?? _pressed;
        var accent = _danger ? skin.Danger : skin.Accent;
        if (WFInstrumentTheme.Digital)
        {
            var fill = _disabled ? skin.ButtonDisabled : pressed ? skin.ButtonPressed :
                _hover ? skin.ButtonHovered : skin.ButtonDefault;
            // A key shows its state with fill and one outline: quiet at rest, lit on hover, accent when on.
            var edge = _disabled ? skin.EdgeSoft : pressed || _danger ? accent : _hover ? skin.EdgeLight : skin.EdgeSoft;
            WFConsoleDigital.Panel(handle, box, uiScale, fill, edge, brackets: false, corner: 4);
            if (_toggle)
            {
                var center = new Vector2(box.Left + 17 * uiScale, box.Top + box.Height / 2);
                handle.DrawCircle(center, 5 * uiScale, pressed ? accent : skin.EdgeLight, false);
                if (pressed)
                    handle.DrawCircle(center, 2 * uiScale, accent);
            }
            return;
        }

        if (box.Width < 6 * uiScale || box.Height < 6 * uiScale)
            return;
        var metal = _disabled ? skin.EdgeSoft : _hover ? skin.TextMuted : skin.EdgeLight;
        var face = _disabled ? skin.ButtonDisabled :
            _danger ? pressed ? skin.ButtonCautionPressed : Color.InterpolateBetween(skin.ButtonDangerDefault, skin.Ink, 0.08f) :
            _accent ? pressed ? Color.InterpolateBetween(skin.AccentDim, skin.Accent, 0.2f) : skin.Accent :
            pressed ? skin.ButtonPressed : _hover ? skin.ButtonHovered : skin.ButtonDefault;
        WFConsoleMetal.Bevel(handle, box, uiScale, skin.Ink, metal);
        var inset = new Vector2(3 * uiScale);
        var cap = new UIBox2(box.TopLeft + inset, box.BottomRight - inset);
        WFConsoleMetal.MetalPanel(handle, cap, uiScale, face);
        if (pressed)
        {
            handle.DrawLine(cap.TopLeft + Vector2.One * uiScale, new Vector2(cap.Right - uiScale, cap.Top + uiScale), skin.Ink);
            handle.DrawLine(cap.TopLeft + Vector2.One * uiScale, new Vector2(cap.Left + uiScale, cap.Bottom - uiScale), skin.Ink);
            handle.DrawLine(new Vector2(cap.Left + uiScale, cap.Bottom - uiScale), cap.BottomRight - Vector2.One * uiScale, metal);
        }

        // The amber lens sits below the engraving, clear of even compact button labels.
        var lamp = _disabled ? skin.EdgeSoft : pressed ? accent : metal;
        var length = MathF.Min(20 * uiScale, cap.Width / 3);
        var centerX = (cap.Left + cap.Right) / 2;
        handle.DrawLine(new Vector2(centerX - length / 2, cap.Bottom - uiScale),
            new Vector2(centerX + length / 2, cap.Bottom - uiScale), lamp);
        if (_toggle)
            DrawRocker(handle, box, uiScale, pressed, accent);
    }

    /// <summary>Draws a ribbed rocker with a lit on-position slot inside a fixed mounting recess.</summary>
    private void DrawRocker(DrawingHandleScreen handle, UIBox2 box, float scale, bool pressed, Color accent)
    {
        var skin = WFInstrumentTheme.Skin;
        var center = new Vector2(box.Left + 16 * scale, box.Top + box.Height / 2);
        var half = new Vector2(7 * scale, MathF.Min(13 * scale, box.Height / 2 - 3 * scale));
        var socket = new UIBox2(center - half, center + half);
        WFConsoleMetal.Bevel(handle, socket, scale, skin.Ink, skin.EdgeLight, true);
        var rock = new UIBox2(socket.TopLeft + new Vector2(2 * scale), socket.BottomRight - new Vector2(2 * scale));
        var hingeY = center.Y + (pressed ? -2 : 2) * scale;
        handle.DrawRect(new UIBox2(rock.Left, rock.Top, rock.Right, hingeY),
            _disabled ? skin.ButtonDisabled : pressed ? skin.GlassRaised : skin.TextMuted);
        handle.DrawRect(new UIBox2(rock.Left, hingeY, rock.Right, rock.Bottom),
            _disabled ? skin.ButtonDisabled : pressed ? skin.TextMuted : skin.GlassRaised);
        handle.DrawLine(new Vector2(rock.Left, hingeY), new Vector2(rock.Right, hingeY), skin.Ink);
        var ribY = pressed ? rock.Bottom - 5 * scale : rock.Top + 2 * scale;
        for (var i = 0; i < 3; i++)
        {
            var y = ribY + i * 1.5f * scale;
            handle.DrawLine(new Vector2(rock.Left + scale, y), new Vector2(rock.Right - scale, y),
                _disabled ? skin.EdgeSoft : skin.EdgeLight);
        }
        var indicatorY = pressed ? rock.Top + 2 * scale : rock.Bottom - 3 * scale;
        handle.DrawRect(new UIBox2(rock.Left + 2 * scale, indicatorY, rock.Right - 2 * scale, indicatorY + scale),
            _disabled ? skin.EdgeSoft : pressed ? accent : skin.EdgeLight);
    }
}

/// <summary>Draws a fluted rotary grip while a slider moves it along its track.</summary>
public sealed class WFKnobStyleBox : StyleBox
{
    private DrawVertexUV2DColor[] _vertices = Array.Empty<DrawVertexUV2DColor>();

    public WFKnobStyleBox()
    {
        SetContentMarginOverride(Margin.All, 14);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var side = Math.Min(box.Width, box.Height);
        var origin = box.TopLeft + (box.Size - new Vector2(side)) / 2;
        if (WFInstrumentTheme.Digital)
        {
            var center = origin + new Vector2(side / 2);
            var skin = WFInstrumentTheme.Skin;
            handle.DrawRect(UIBox2.FromDimensions(center - new Vector2(3, side / (2 * uiScale)) * uiScale,
                new Vector2(6 * uiScale, side)), skin.Accent.WithAlpha(0.15f));
            handle.DrawLine(center - new Vector2(0, side / 2), center + new Vector2(0, side / 2), skin.Accent);
            handle.DrawCircle(center, 3 * uiScale, skin.Accent);
            return;
        }
        if (side < 4 * uiScale)
            return;
        var palette = WFInstrumentTheme.Skin;
        var hub = origin + new Vector2(side / 2);
        var radius = side / 2 - uiScale;
        WFConsoleDigital.Dot(handle, hub, radius, palette.Ink, ref _vertices);
        WFConsoleDigital.Arc(handle, hub, radius, uiScale, 0, MathF.Tau, palette.EdgeLight, ref _vertices);
        WFConsoleDigital.Dot(handle, hub, radius * 0.84f, palette.GlassLight, ref _vertices);
        for (var i = 0; i < 20; i++)
        {
            var angle = MathF.Tau * i / 20;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            handle.DrawLine(hub + direction * radius * 0.69f, hub + direction * radius * 0.91f,
                i is >= 10 and <= 16 ? palette.TextMuted : palette.Ink);
        }
        WFConsoleDigital.Dot(handle, hub, radius * 0.63f, palette.ButtonDefault, ref _vertices);
        WFConsoleDigital.Arc(handle, hub, radius * 0.65f, uiScale, MathF.PI, MathF.Tau,
            palette.EdgeLight, ref _vertices);
        WFConsoleDigital.Arc(handle, hub, radius * 0.52f, uiScale * 0.65f, MathF.PI * 1.1f, MathF.PI * 1.65f,
            palette.TextMuted.WithAlpha(0.4f), ref _vertices);
        handle.DrawLine(hub - Vector2.UnitY * radius * 0.28f, hub - Vector2.UnitY * radius * 0.57f, palette.Accent);
        WFConsoleDigital.Dot(handle, hub, uiScale, palette.Ink, ref _vertices);
    }
}

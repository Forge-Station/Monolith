using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// The rock under the mining lasers on a dial: a needle on its heat against the cold, window, hot and overcharge bands,
/// crack progress round the rim, and the heat in figures. With no rock it shows a bare scale.
/// </summary>
public sealed class WFMiningHeatDial : Control
{
    /// <summary>The scale runs clockwise from lower left round to lower right, leaving the bottom quarter for figures.</summary>
    private const float Start = MathF.PI * 0.75f;
    private const float Sweep = MathF.PI * 1.5f;

    private readonly Font _small;
    private readonly Font _digits;
    private readonly string _caption = Loc.GetString("wf-mining-dial-heat");
    private readonly string _idle = Loc.GetString("wf-mining-dial-idle");
    private readonly string _overcharge = Loc.GetString("wf-mining-dial-overcharge");
    private readonly string _empty = Loc.GetString("wf-mining-dial-empty");
    private readonly string _unit = Loc.GetString("wf-mining-dial-unit");
    private DrawVertexUV2DColor[] _vertices = Array.Empty<DrawVertexUV2DColor>();
    private readonly Vector2[] _needleShape = new Vector2[3];
    private WFMiningBeamTargetEntry? _target;
    private float? _needle;
    private float _progress;
    private float _time;
    private string _heat = string.Empty;

    public WFMiningHeatDial()
    {
        MinSize = new Vector2(150, 170);
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Pass;
        ToolTip = Loc.GetString("wf-mining-gunnery-meter-help");
        var cache = IoCManager.Resolve<IResourceCache>();
        _small = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 10);
        _digits = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 24);
    }

    /// <summary>Shows a rock's scan, or the bare scale for none; the figure is formatted here rather than every frame.</summary>
    public void SetTarget(WFMiningBeamTargetEntry? target)
    {
        _target = target;
        if (target is { } rock)
            _heat = ((int) MathF.Round(rock.Charge * 100)).ToString();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
        var ease = MathF.Min(1f, args.DeltaSeconds * 10f);
        if (_target is { } rock)
        {
            _needle = _needle is { } needle ? needle + (rock.Charge - needle) * ease : rock.Charge;
            _progress += (rock.Progress - _progress) * ease;
        }
        else
        {
            _needle = null;
            _progress = 0f;
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        var digital = WFInstrumentTheme.Digital;
        var size = MathF.Min(PixelWidth, PixelHeight);
        if (size <= 0)
            return;

        var center = new Vector2(PixelWidth / 2f, PixelHeight / 2f + size * 0.04f);
        var radius = size / 2f - 2f * UIScale;
        if (digital)
        {
            WFConsoleDigital.Panel(handle, PixelSizeBox, UIScale, skin.Glass, skin.EdgeSoft);
        }
        else
        {
            handle.DrawCircle(center + new Vector2(0, 2 * UIScale), radius, Color.Black.WithAlpha(0.8f));
            handle.DrawCircle(center, radius, skin.EdgeLight);
            handle.DrawCircle(center, radius - 2 * UIScale, skin.EdgeSoft);
            handle.DrawCircle(center, radius - 4 * UIScale, skin.TextMuted);
            handle.DrawCircle(center, radius - 5 * UIScale, skin.Ink);
        }

        var face = MathF.Max(1f, radius - 8 * UIScale);
        if (!digital)
            handle.DrawCircle(center, face, skin.Glass);

        var rock = _target;
        var overcharging = rock is { Overcharge: > 0f };
        var flash = overcharging && _time * 4f % 1f < 0.5f;
        if (overcharging)
            WFConsoleDigital.Arc(handle, center, face + 4 * UIScale, 3 * UIScale, 0f, MathF.Tau,
                skin.Danger.WithAlpha(flash ? 0.9f : 0.3f), ref _vertices);

        // Crack progress round the rim, over the same sweep as the heat.
        var rim = face - 2 * UIScale;
        WFConsoleDigital.Arc(handle, center, rim, 3 * UIScale, Start, Start + Sweep, skin.GlassLight.WithAlpha(0.6f), ref _vertices);
        if (_progress > 0.001f)
            WFConsoleDigital.Arc(handle, center, rim, 3 * UIScale, Start, Start + Sweep * Math.Clamp(_progress, 0f, 1f),
                skin.Accent, ref _vertices);

        // With no rock there is no window: everything below the overcharge line reads cold.
        var low = rock?.WindowLow ?? WFAsteroidHeat.OverchargeLine;
        var high = rock?.WindowHigh ?? WFAsteroidHeat.OverchargeLine;
        var band = face - 8 * UIScale;
        WFMiningGauges.HeatArc(handle, center, band, 7 * UIScale, Start, Sweep, low, high, rock == null ? 0.45f : 1f, flash,
            ref _vertices);

        var ticks = face - 17 * UIScale;
        for (var i = 0; i <= 11; i++)
        {
            var angle = WFMiningGauges.HeatAngle(i * 0.1f, Start, Sweep);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var major = i % 5 == 0;
            handle.DrawLine(center + direction * (ticks - (major ? 7 : 3) * UIScale), center + direction * ticks,
                major ? skin.Text : skin.TextMuted);
            if (major)
                WFInstrumentText.DrawFitted(handle, _small, (i * 10).ToString(),
                    UIBox2.FromDimensions(center + direction * (ticks - 16 * UIScale) - new Vector2(10, 6) * UIScale,
                        new Vector2(20, 12) * UIScale), UIScale, skin.TextMuted);
        }

        if (_needle is { } needle && rock is { } scan)
        {
            var angle = WFMiningGauges.HeatAngle(needle, Start, Sweep);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var color = WFMiningGauges.Band(needle, scan.WindowLow, scan.WindowHigh);
            if (digital)
            {
                handle.DrawLine(center + direction * face * 0.3f, center + direction * (band - 2 * UIScale), color);
                WFConsoleDigital.Dot(handle, center + direction * (band - 3.5f * UIScale), 4 * UIScale, color, ref _vertices);
            }
            else
            {
                var side = new Vector2(-direction.Y, direction.X);
                handle.DrawLine(center + new Vector2(2 * UIScale) - direction * face * 0.14f,
                    center + new Vector2(2 * UIScale) + direction * face * 0.86f, Color.Black);
                _needleShape[0] = center - direction * face * 0.16f + side * 2f * UIScale;
                _needleShape[1] = center + direction * face * 0.9f;
                _needleShape[2] = center - direction * face * 0.16f - side * 2f * UIScale;
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _needleShape, color);
                handle.DrawCircle(center, 5 * UIScale, skin.TextMuted);
                handle.DrawCircle(center, 3 * UIScale, skin.Edge);
            }
        }

        // The figure sits under the hub, narrow enough to clear the 0 and 100 numerals, and the caption in the gap at the
        // bottom of the scale; the window, crack and yield figures are in the strip under the dial.
        var readout = UIBox2.FromDimensions(new Vector2(center.X - face * 0.32f, center.Y + face * 0.12f),
            new Vector2(face * 0.64f, face * 0.28f));
        var inner = new UIBox2(readout.TopLeft + new Vector2(UIScale), readout.BottomRight - new Vector2(UIScale));
        if (!digital)
            handle.DrawRect(readout, skin.Ink);
        if (rock is { } target)
            WFInstrumentText.DrawReading(handle, _digits, _heat, _unit, inner, UIScale,
                WFMiningGauges.Band(target.Charge, target.WindowLow, target.WindowHigh), skin.TextMuted);
        else
            WFInstrumentText.DrawFitted(handle, _digits, _empty, inner, UIScale, skin.TextMuted);

        var caption = overcharging ? _overcharge : rock == null ? _idle : _caption;
        var captionColor = overcharging ? skin.Danger.WithAlpha(flash ? 1f : 0.4f) : rock == null ? skin.TextMuted : skin.Text;
        Fitted(_small, caption, center.Y + face * 0.68f, face * 0.9f, face * 0.17f, captionColor);

        if (!digital)
        {
            WFInstrumentGlass.Window(handle, readout, UIScale);
            WFInstrumentGlass.Round(handle, center, face + 2 * UIScale, UIScale);
        }

        void Fitted(Font font, string text, float top, float width, float height, Color color)
        {
            WFInstrumentText.DrawFitted(handle, font, text,
                UIBox2.FromDimensions(new Vector2(center.X - width / 2f, top), new Vector2(width, height)), UIScale, color);
        }
    }
}

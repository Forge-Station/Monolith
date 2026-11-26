using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// The cockpit's asteroid scanner over one view, the ship cam or the NAV plot. Pointing at a rock locks brackets onto it
/// and opens a callout with what it holds and how it will take a mining laser; a warm rock also gets a heat ring. A rock
/// out of view range only gives a signal, since what it holds hasn't reached the client.
/// </summary>
public sealed partial class WFAsteroidScanner : Control
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;

    private const float RefreshInterval = 0.05f;
    private const float LockTime = 0.25f;
    private const float FadeTime = 0.15f;
    /// <summary>Callout lines revealed per second once the brackets have locked.</summary>
    private const float RevealRate = 45f;
    /// <summary>How many pixels the pointer may miss a rock by.</summary>
    private const float PointerSlack = 10f;
    private const float CalloutWidth = 214f;
    private const float Padding = 7f;
    private const float LineHeight = 14f;

    private readonly Control _surface;
    private readonly Func<WFAsteroidScanner, (Matrix3x2 WorldToLocal, MapId Map)?> _projection;
    private readonly WFAsteroidScanReader _reader;
    private readonly WFAsteroidScanReading _reading = new();
    private readonly List<Line> _lines = new();
    private readonly Font _title;
    private readonly Font _text;
    private DrawVertexUV2DColor[] _vertices = Array.Empty<DrawVertexUV2DColor>();
    private EntityUid? _locked;
    private bool _hovering;
    private bool _resolved;
    private float _lockAge;
    private float _fade;
    private float _refresh;
    private float _time;

    private enum LineKind : byte
    {
        Title,
        Text,
        Composition,
        Ore,
        Pair,
        Warning,
        Divider,
    }

    /// <summary>One line of the callout; <see cref="Right"/> is drawn against the right edge.</summary>
    private readonly record struct Line(LineKind Kind, string Left, string Right = "", Color? Color = null);

    /// <param name="surface">The control the pointer must be over for the scanner to look.</param>
    /// <param name="projection">World to this scanner's pixels and the map shown, or null while the view shows nothing.</param>
    public WFAsteroidScanner(Control surface, Func<WFAsteroidScanner, (Matrix3x2 WorldToLocal, MapId Map)?> projection)
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = true;
        _surface = surface;
        _projection = projection;
        _reader = new WFAsteroidScanReader(_entities, _prototypes);
        var cache = IoCManager.Resolve<IResourceCache>();
        _title = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 11);
        _text = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 10);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
        _lockAge += args.DeltaSeconds;
        _refresh += args.DeltaSeconds;
        if (_refresh >= RefreshInterval)
        {
            _refresh = 0f;
            Look();
        }

        var step = args.DeltaSeconds / FadeTime;
        _fade = Math.Clamp(_fade + (_hovering ? step : -step), 0f, 1f);
        if (_fade <= 0f)
            _locked = null;
    }

    /// <summary>Finds the rock under the pointer, locks onto it if it is new, and rereads whichever rock is locked.</summary>
    private void Look()
    {
        _hovering = false;
        var pointer = _input.MouseScreenPosition;
        if (pointer.IsValid && _surface.VisibleInTree && pointer.Window == _surface.Window?.Id &&
            UserInterfaceManager.MouseGetControl(pointer) == _surface && _projection(this) is { } view &&
            Matrix3x2.Invert(view.WorldToLocal, out var localToWorld))
        {
            var point = Vector2.Transform(pointer.Position - GlobalPixelPosition, localToWorld);
            var slack = PointerSlack * UIScale / MathF.Max(0.001f, PixelsPerMetre(view.WorldToLocal));
            if (_reader.TryFind(view.Map, point, slack, out var key))
            {
                _hovering = true;
                if (_locked != key)
                {
                    _locked = key;
                    _lockAge = 0f;
                }
            }
        }

        var pilot = _player.LocalEntity is { } local ? _entities.System<SharedTransformSystem>().GetWorldPosition(local) : Vector2.Zero;
        if (_locked is not { } locked || !_reader.TryRead(locked, pilot, _reading))
        {
            _locked = null;
            _hovering = false;
            return;
        }

        // A signal that resolves as the rock comes into view range types its scan out afresh.
        if (_reading.Resolved && !_resolved)
            _lockAge = MathF.Min(_lockAge, LockTime);
        _resolved = _reading.Resolved;
        BuildLines();
    }

    private static float PixelsPerMetre(Matrix3x2 worldToLocal)
    {
        return new Vector2(worldToLocal.M11, worldToLocal.M12).Length();
    }

    private void BuildLines()
    {
        var reading = _reading;
        _lines.Clear();
        _lines.Add(new Line(LineKind.Title, reading.Name.ToUpperInvariant(),
            Loc.GetString(reading.Resolved ? "wf-mining-scan-tag" : "wf-mining-scan-tag-far")));
        _lines.Add(new Line(LineKind.Text, Loc.GetString("wf-mining-scan-range", ("range", (int) MathF.Round(reading.Range))),
            Loc.GetString("wf-mining-scan-size", ("size", (int) MathF.Round(reading.Radius * 2)))));
        if (!reading.Resolved)
        {
            _lines.Add(new Line(LineKind.Warning, Loc.GetString("wf-mining-scan-far"), Color: WFInstrumentTheme.Skin.TextMuted));
            return;
        }

        _lines.Add(new Line(LineKind.Text, Loc.GetString("wf-mining-scan-veins", ("mass", (int) MathF.Round(reading.Mass)))));
        _lines.Add(new Line(LineKind.Composition, string.Empty));
        foreach (var ore in reading.Ores)
        {
            _lines.Add(new Line(LineKind.Ore, Loc.GetString("wf-mining-scan-ore", ("name", ore.Name)), Percent(ore.Fraction), ore.Color));
        }

        _lines.Add(new Line(LineKind.Ore, Loc.GetString("wf-mining-scan-inert"), Percent(reading.Inert),
            WFInstrumentTheme.Skin.TextMuted));
        _lines.Add(new Line(LineKind.Divider, string.Empty));
        _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-mining-scan-resistance"), Percent(reading.Resistance)));
        _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-mining-scan-window"),
            Loc.GetString("wf-mining-scan-window-range", ("low", Whole(reading.WindowLow)), ("high", Whole(reading.WindowHigh))),
            WFInstrumentTheme.Skin.Good));
        _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-mining-scan-stability"),
            Loc.GetString("wf-mining-scan-stability-band", ("band", reading.Stability)),
            reading.Stability == "steady" ? null : WFInstrumentTheme.Skin.Caution));
        _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-mining-scan-blast"),
            Loc.GetString("wf-mining-scan-blast-band", ("band", reading.Blast)),
            reading.Blast == "low" ? null : reading.Blast == "high" ? WFInstrumentTheme.Skin.Caution : WFInstrumentTheme.Skin.Danger));
        if (!reading.Overcharging && reading.Charge <= 0.01f && reading.Progress <= 0.001f)
            return;

        _lines.Add(new Line(LineKind.Divider, string.Empty));
        if (reading.Overcharging)
        {
            _lines.Add(new Line(LineKind.Warning, Loc.GetString("wf-mining-scan-overcharge"), Color: WFInstrumentTheme.Skin.Danger));
            return;
        }

        _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-mining-scan-heat"), Percent(reading.Charge),
            WFMiningGauges.Band(reading.Charge, reading.WindowLow, reading.WindowHigh)));
        _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-mining-scan-crack"), Percent(reading.Progress),
            WFInstrumentTheme.Skin.Accent));
    }

    private static int Whole(float fraction)
    {
        return (int) MathF.Round(fraction * 100);
    }

    private static string Percent(float fraction)
    {
        return Loc.GetString("wf-mining-scan-percent", ("percent", Whole(fraction)));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_fade <= 0f || _locked == null || _lines.Count == 0 || _projection(this) is not { } view || view.Map != _reading.Map)
            return;

        var skin = WFInstrumentTheme.Skin;
        var center = Vector2.Transform(_reading.Center, view.WorldToLocal);
        var radius = MathF.Max(_reading.Radius * PixelsPerMetre(view.WorldToLocal), 8f * UIScale);
        var lockIn = Ease(Math.Clamp(_lockAge / LockTime, 0f, 1f));
        var half = radius * (1.12f + 0.9f * (1f - lockIn)) + 4f * UIScale;
        var color = (_reading.Resolved ? skin.Accent : skin.TextMuted).WithAlpha(_fade * (0.4f + 0.6f * lockIn));
        WFMiningGauges.Brackets(handle, center, half, (1f - lockIn) * MathF.PI / 4f, color, UIScale);

        if (_reading.Resolved && (_reading.Charge > 0.01f || _reading.Progress > 0.001f))
            DrawHeatRing(handle, center, half + 5f * UIScale);

        if (lockIn < 0.6f)
            return;

        var shown = Math.Min(_lines.Count, (int) ((_lockAge - LockTime * 0.6f) * RevealRate) + 1);
        DrawCallout(handle, center, half, shown);
    }

    private static float Ease(float t)
    {
        return 1f - (1f - t) * (1f - t) * (1f - t);
    }

    /// <summary>The rock's heat as a ring round its brackets, against its bands, with crack progress inside it.</summary>
    private void DrawHeatRing(DrawingHandleScreen handle, Vector2 center, float radius)
    {
        const float start = -MathF.PI / 2f;
        var skin = WFInstrumentTheme.Skin;
        var flash = _reading.Overcharging && _time * 4f % 1f < 0.5f;
        WFMiningGauges.HeatArc(handle, center, radius, 3f * UIScale, start, MathF.Tau, _reading.WindowLow, _reading.WindowHigh,
            _fade * 0.85f, flash, ref _vertices);
        var heat = WFMiningGauges.HeatAngle(_reading.Charge, start, MathF.Tau);
        WFConsoleDigital.Dot(handle, center + new Vector2(MathF.Cos(heat), MathF.Sin(heat)) * (radius - 1.5f * UIScale),
            3.5f * UIScale, Color.White.WithAlpha(_fade), ref _vertices);
        if (_reading.Progress > 0.001f)
            WFConsoleDigital.Arc(handle, center, radius - 5f * UIScale, 2f * UIScale, start,
                start + MathF.Tau * Math.Clamp(_reading.Progress, 0f, 1f), skin.Accent.WithAlpha(_fade), ref _vertices);
    }

    /// <summary>The callout beside the brackets, on whichever side has room, with a leader line back to the rock.</summary>
    private void DrawCallout(DrawingHandleScreen handle, Vector2 center, float half, int shown)
    {
        var skin = WFInstrumentTheme.Skin;
        var scale = UIScale;
        var width = CalloutWidth * scale;
        var height = Padding * 2f * scale;
        foreach (var line in _lines)
        {
            height += LineSpan(line.Kind) * scale;
        }

        var gap = 18f * scale;
        var right = center.X + half + gap + width <= PixelWidth - 4f * scale || center.X < PixelWidth / 2f;
        var left = right ? center.X + half + gap : center.X - half - gap - width;
        left = Math.Clamp(left, 4f * scale, MathF.Max(4f * scale, PixelWidth - width - 4f * scale));
        var top = Math.Clamp(center.Y - half, 4f * scale, MathF.Max(4f * scale, PixelHeight - height - 4f * scale));
        var box = UIBox2.FromDimensions(new Vector2(left, top), new Vector2(width, height));
        var alpha = _fade;

        // The leader leaves the tip of a bracket's upper corner, outside the heat ring, then runs level into the callout.
        var corner = center + new Vector2(right ? half : -half, -half);
        var elbow = new Vector2(right ? box.Left - 6f * scale : box.Right + 6f * scale, box.Top + 10f * scale);
        var leader = skin.Accent.WithAlpha(0.7f * alpha);
        handle.DrawLine(corner, elbow, leader);
        handle.DrawLine(elbow, new Vector2(right ? box.Left : box.Right, elbow.Y), leader);

        if (WFInstrumentTheme.Digital)
            WFConsoleDigital.Panel(handle, box, scale, skin.Glass.WithAlpha(0.88f * alpha), skin.EdgeSoft.WithAlpha(alpha));
        else
        {
            handle.DrawRect(box, skin.Ink.WithAlpha(0.86f * alpha));
            handle.DrawRect(box, skin.Accent.WithAlpha(0.55f * alpha), false);
        }

        var x = box.Left + Padding * scale;
        var inner = width - Padding * 2f * scale;
        var edge = box.Right - Padding * scale;
        var spacing = 6f * scale;
        var y = box.Top + Padding * scale;
        for (var i = 0; i < shown; i++)
        {
            var line = _lines[i];
            var lineHeight = LineSpan(line.Kind) * scale;
            var color = (line.Color ?? skin.Text).WithAlpha(alpha);
            switch (line.Kind)
            {
                case LineKind.Title:
                    WFMiningGauges.TextLeft(handle, _title, line.Left, new Vector2(x, y), inner * 0.74f, scale, skin.Accent.WithAlpha(alpha));
                    WFMiningGauges.TextRight(handle, _text, line.Right, edge, y + scale, inner * 0.24f, scale,
                        (_reading.Resolved ? skin.Good : skin.TextMuted).WithAlpha(alpha));
                    handle.DrawLine(new Vector2(x, y + lineHeight - 3f * scale), new Vector2(edge, y + lineHeight - 3f * scale),
                        skin.EdgeSoft.WithAlpha(alpha));
                    break;
                case LineKind.Composition:
                    WFMiningGauges.Composition(handle, UIBox2.FromDimensions(new Vector2(x, y + 4f * scale), new Vector2(inner, lineHeight - 8f * scale)),
                        _reading.Ores, scale, alpha);
                    break;
                case LineKind.Divider:
                    handle.DrawLine(new Vector2(x, y + lineHeight / 2f), new Vector2(edge, y + lineHeight / 2f), skin.EdgeSoft.WithAlpha(alpha));
                    break;
                case LineKind.Warning:
                    var lit = line.Color != skin.Danger || _time * 4f % 1f < 0.6f;
                    WFMiningGauges.TextLeft(handle, _title, line.Left, new Vector2(x, y), inner, scale, lit ? color : color.WithAlpha(0.3f * alpha));
                    break;
                default:
                    // The figure is drawn first, so the name gets whatever width it leaves.
                    var muted = skin.TextMuted.WithAlpha(alpha);
                    var figure = WFMiningGauges.TextRight(handle, _text, line.Right, edge, y, inner * 0.5f, scale,
                        line.Kind == LineKind.Text ? muted : color);
                    var indent = line.Kind == LineKind.Ore ? 11f * scale : 0f;
                    if (line.Kind == LineKind.Ore)
                        handle.DrawRect(UIBox2.FromDimensions(new Vector2(x, y + 3f * scale), new Vector2(7f * scale, 7f * scale)), color);
                    WFMiningGauges.TextLeft(handle, _text, line.Left, new Vector2(x + indent, y),
                        inner - indent - (figure > 0f ? figure + spacing : 0f), scale, line.Kind == LineKind.Ore ? skin.Text.WithAlpha(alpha) : muted);
                    break;
            }

            y += lineHeight;
        }

        // A scan line sweeps down the callout while it types out.
        if (shown < _lines.Count)
            handle.DrawLine(new Vector2(box.Left + 2f * scale, y), new Vector2(box.Right - 2f * scale, y), skin.Accent.WithAlpha(0.8f * alpha));
        else if (!WFInstrumentTheme.Digital && alpha >= 1f)
            WFInstrumentGlass.Window(handle, box, scale);
    }

    private static float LineSpan(LineKind kind)
    {
        return kind switch
        {
            LineKind.Title => LineHeight + 5f,
            LineKind.Composition => LineHeight + 2f,
            LineKind.Divider => 7f,
            _ => LineHeight,
        };
    }
}

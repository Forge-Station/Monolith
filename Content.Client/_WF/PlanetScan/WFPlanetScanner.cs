using System.Numerics;
using Content.Client._WF.AsteroidMining;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.PlanetCargoHubs;
using Content.Shared._WF.PlanetMap;
using Content.Shared._WF.PlanetScan;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.PlanetScan;

/// <summary>
/// The planet scan over a radar plot, built like the cockpit's asteroid scanner: pointing at a world locks brackets
/// onto it and types out a callout with its climate, gravity, air, hazards, known sites, landing zone and range.
/// </summary>
public sealed class WFPlanetScanner : Control
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
    /// <summary>How many pixels the pointer may miss a world by.</summary>
    private const float PointerSlack = 6f;
    private const float CalloutWidth = 236f;
    private const float Padding = 7f;
    private const float LineHeight = 14f;
    /// <summary>Share of the plot's short side past which a world's brackets give way to the pointer's.</summary>
    private const float LargeShare = 0.35f;

    private readonly Control _surface;
    private readonly Func<(Matrix3x2 WorldToLocal, MapId Map)?> _projection;
    private readonly float _minRadius;
    private readonly WFPlanetScanReader _reader;
    private readonly WFPlanetScanReading _reading = new();
    private readonly List<Line> _lines = new();
    private readonly Font _title;
    private readonly Font _text;
    private EntityUid? _locked;
    private Vector2 _pointer;
    private bool _hovering;
    private float _lockAge;
    private float _fade;
    private float _refresh;

    private enum LineKind : byte
    {
        Title,
        Text,
        Pair,
        Item,
        Warning,
        Divider,
    }

    /// <summary>One line of the callout; <see cref="Right"/> is drawn against the right edge.</summary>
    private readonly record struct Line(LineKind Kind, string Left, string Right = "", Color? Color = null);

    /// <param name="surface">The control the pointer must be over for the scan to look.</param>
    /// <param name="projection">World to this control's pixels and the map shown, or null while the plot shows nothing.</param>
    /// <param name="minRadius">The smallest radius the plot draws a world at, in virtual pixels.</param>
    public WFPlanetScanner(Control surface, Func<(Matrix3x2 WorldToLocal, MapId Map)?> projection, float minRadius)
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = true;
        _surface = surface;
        _projection = projection;
        _minRadius = minRadius;
        _reader = new WFPlanetScanReader(_entities, _prototypes);
        var cache = IoCManager.Resolve<IResourceCache>();
        _title = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 11);
        _text = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 10);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
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

    /// <summary>Finds the world under the pointer, locks onto it if it is new, and rereads whichever world is locked.</summary>
    private void Look()
    {
        _hovering = false;
        var pointer = _input.MouseScreenPosition;
        if (pointer.IsValid && _surface.VisibleInTree && pointer.Window == _surface.Window?.Id &&
            UserInterfaceManager.MouseGetControl(pointer) == _surface && _projection() is { } view &&
            Matrix3x2.Invert(view.WorldToLocal, out var localToWorld))
        {
            _pointer = pointer.Position - GlobalPixelPosition;
            var point = Vector2.Transform(_pointer, localToWorld);
            var perMetre = MathF.Max(0.0001f, new Vector2(view.WorldToLocal.M11, view.WorldToLocal.M12).Length());
            if (_reader.TryFind(view.Map, point, PointerSlack * UIScale / perMetre, _minRadius * UIScale / perMetre, out var body))
            {
                _hovering = true;
                if (_locked != body)
                {
                    _locked = body;
                    _lockAge = 0f;
                }
            }
        }

        var pilot = _player.LocalEntity is { } local
            ? _entities.System<SharedTransformSystem>().GetMapCoordinates(local)
            : (MapCoordinates?) null;
        if (_locked is not { } locked || !_reader.TryRead(locked, pilot, _reading))
        {
            _locked = null;
            _hovering = false;
            return;
        }

        BuildLines();
    }

    private void BuildLines()
    {
        var reading = _reading;
        var skin = WFInstrumentTheme.Skin;
        _lines.Clear();
        _lines.Add(new Line(LineKind.Title, reading.Name.ToUpperInvariant(), Loc.GetString(!reading.Surface
            ? "wf-planet-scan-tag-body"
            : reading.Sanctioned ? "wf-planet-scan-tag" : "wf-planet-scan-tag-unsanctioned")));
        if (reading.Range is { } range)
            _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-planet-scan-range"), Distance(range)));
        foreach (var depot in reading.Depots)
        {
            _lines.Add(new Line(LineKind.Warning, Loc.GetString("wf-planet-scan-depot", ("depot", Loc.GetString(depot))),
                Color: WFPlanetCargoHubSites.Colour));
        }

        if (!reading.Surface)
        {
            _lines.Add(new Line(LineKind.Warning, Loc.GetString("wf-planet-scan-no-surface"), Color: skin.TextMuted));
            return;
        }

        if (reading.Climate is { } climate)
            _lines.Add(new Line(LineKind.Text, Loc.GetString(climate)));
        _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-planet-scan-gravity"),
            Loc.GetString("wf-planet-scan-gravity-value", ("gravity", reading.Gravity.ToString("0.0#"))),
            reading.Gravity >= 1.5f ? skin.Caution : null));
        _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-planet-scan-air"),
            Loc.GetString(reading.Breathable ? "wf-planet-scan-air-breathable" : "wf-planet-scan-air-unbreathable",
                ("pressure", (int) MathF.Round(reading.Pressure))),
            reading.Breathable ? skin.Good : skin.Caution));
        _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-planet-scan-temperature"),
            Loc.GetString("wf-planet-scan-temperature-value", ("celsius", (int) MathF.Round(reading.Temperature - 273.15f)))));

        _lines.Add(new Line(LineKind.Divider, string.Empty));
        if (reading.Hazards.Count == 0)
            _lines.Add(new Line(LineKind.Text, Loc.GetString("wf-planet-scan-no-hazards")));
        foreach (var hazard in reading.Hazards)
        {
            _lines.Add(new Line(LineKind.Warning, Loc.GetString(hazard), Color: skin.Caution));
        }

        _lines.Add(new Line(LineKind.Divider, string.Empty));
        if (!reading.Surveyed)
        {
            _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-planet-scan-sites"), Loc.GetString("wf-planet-scan-unsurveyed"),
                skin.TextMuted));
        }
        else
        {
            _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-planet-scan-sites"),
                Loc.GetString("wf-planet-scan-sites-count", ("count", reading.Sites))));
            for (var kind = 0; kind < WFPlanetScanReader.SiteKindCount; kind++)
            {
                if (reading.SiteKinds[kind] == 0)
                    continue;
                _lines.Add(new Line(LineKind.Item, Loc.GetString("wf-planet-scan-site-kind", ("kind", ((WFPlanetMapSiteKind) kind).ToString())),
                    Loc.GetString("wf-planet-scan-site-kind-count", ("count", reading.SiteKinds[kind]))));
            }

            _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-planet-scan-landing"), reading.LandingRadius > 0f
                ? Loc.GetString("wf-planet-scan-landing-clear", ("radius", (int) MathF.Round(reading.LandingRadius)))
                : Loc.GetString("wf-planet-scan-landing-none")));
        }

        if (reading.Vessels > 0 || reading.People > 0)
        {
            _lines.Add(new Line(LineKind.Pair, Loc.GetString("wf-planet-scan-contacts"),
                Loc.GetString("wf-planet-scan-contacts-value", ("vessels", reading.Vessels), ("people", reading.People))));
        }
    }

    private static string Distance(float metres)
    {
        return metres < 1000f
            ? Loc.GetString("wf-planet-scan-metres", ("metres", (int) MathF.Round(metres)))
            : Loc.GetString("wf-planet-scan-kilometres", ("kilometres", (metres / 1000f).ToString("0.0")));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_fade <= 0f || _locked == null || _lines.Count == 0 || _projection() is not { } view || view.Map != _reading.Map)
            return;

        var skin = WFInstrumentTheme.Skin;
        var perMetre = new Vector2(view.WorldToLocal.M11, view.WorldToLocal.M12).Length();
        var center = Vector2.Transform(_reading.Centre, view.WorldToLocal);
        var radius = MathF.Max(_reading.Radius * perMetre, _minRadius * UIScale);
        var lockIn = Ease(Math.Clamp(_lockAge / LockTime, 0f, 1f));
        var color = skin.Accent.WithAlpha(_fade * (0.4f + 0.6f * lockIn));

        // A world filling much of the plot is bracketed where the pointer is, so the callout stays by it.
        var anchor = center;
        var size = radius;
        if (radius > MathF.Min(PixelWidth, PixelHeight) * LargeShare)
        {
            anchor = _pointer;
            size = 14f * UIScale;
        }

        var half = size * (1.12f + 0.9f * (1f - lockIn)) + 4f * UIScale;
        WFMiningGauges.Brackets(handle, anchor, half, (1f - lockIn) * MathF.PI / 4f, color, UIScale);

        if (lockIn < 0.6f)
            return;

        var shown = Math.Min(_lines.Count, (int) ((_lockAge - LockTime * 0.6f) * RevealRate) + 1);
        DrawCallout(handle, anchor, half, shown);
    }

    private static float Ease(float t)
    {
        return 1f - (1f - t) * (1f - t) * (1f - t);
    }

    /// <summary>The callout beside the brackets, on whichever side has room, with a leader line back to the world.</summary>
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
            var muted = skin.TextMuted.WithAlpha(alpha);
            switch (line.Kind)
            {
                case LineKind.Title:
                    WFMiningGauges.TextLeft(handle, _title, line.Left, new Vector2(x, y), inner * 0.6f, scale, skin.Accent.WithAlpha(alpha));
                    WFMiningGauges.TextRight(handle, _text, line.Right, edge, y + scale, inner * 0.38f, scale,
                        (_reading.Sanctioned ? skin.Good : skin.Danger).WithAlpha(alpha));
                    handle.DrawLine(new Vector2(x, y + lineHeight - 3f * scale), new Vector2(edge, y + lineHeight - 3f * scale),
                        skin.EdgeSoft.WithAlpha(alpha));
                    break;
                case LineKind.Divider:
                    handle.DrawLine(new Vector2(x, y + lineHeight / 2f), new Vector2(edge, y + lineHeight / 2f), skin.EdgeSoft.WithAlpha(alpha));
                    break;
                case LineKind.Warning:
                    WFMiningGauges.TextLeft(handle, _text, line.Left, new Vector2(x, y), inner, scale, color);
                    break;
                case LineKind.Text:
                    WFMiningGauges.TextLeft(handle, _text, line.Left, new Vector2(x, y), inner, scale, skin.Text.WithAlpha(alpha));
                    break;
                default:
                    // The figure is drawn first, so the name gets whatever width it leaves.
                    var figure = WFMiningGauges.TextRight(handle, _text, line.Right, edge, y, inner * 0.6f, scale, color);
                    var indent = line.Kind == LineKind.Item ? 11f * scale : 0f;
                    WFMiningGauges.TextLeft(handle, _text, line.Left, new Vector2(x + indent, y),
                        inner - indent - (figure > 0f ? figure + spacing : 0f), scale, muted);
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
            LineKind.Divider => 7f,
            _ => LineHeight,
        };
    }
}

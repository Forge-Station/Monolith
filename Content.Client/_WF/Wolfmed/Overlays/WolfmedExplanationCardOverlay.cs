using System.Linq;
using System.Numerics;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Alert;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>
/// M2 (plan §5.2): the explanation card, a small text panel low on the unconscious screen. The words come from
/// <see cref="WolfmedExplanationCard"/>; this only lays them out.
/// </summary>
/// <remarks>
/// Playtest 3: a dark translucent panel with the cause's accent stripe down the left. The cause's alert icon beside a
/// caps title; under it the countdown ("COMING ROUND IN 22 S", or ∞) and a ten-cell bar that drains every frame, or
/// the brain bar while Dying; then the help, the blockers after their icons, the symptom, the felt states and the
/// rescue lines.
/// </remarks>
public sealed class WolfmedExplanationCardOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const string BoldFont = "/Fonts/NotoSans/NotoSans-Bold.ttf";
    private const string RegularFont = "/Fonts/NotoSans/NotoSans-Regular.ttf";

    private const float TitleSize = 17f;
    private const float CountdownSize = 24f;
    private const float BodySize = 14f;
    private const float Padding = 12f;
    private const float Gap = 8f;
    private const float BarHeight = 10f;
    private const float CellGap = 3f;
    private const int Cells = 10;

    /// <summary>The alerts bar draws a 32 px icon at twice its size; the title's icon matches it, the blockers' are half.</summary>
    private const float AlertIconSize = 64f;

    private const float BlockerIconSize = AlertIconSize / 2f;
    private const float StripeWidth = 3f;

    private static readonly Color Panel = Color.FromHex("#0d0f14").WithAlpha(0.8f);
    private static readonly Color TitleColor = Color.FromHex("#e8d9a6");
    private static readonly Color CountdownColor = Color.FromHex("#f4efe6");
    private static readonly Color HelpColor = Color.FromHex("#e3ddd2");
    private static readonly Color BodyColor = Color.FromHex("#c9c3b8");
    private static readonly Color RescueColor = Color.FromHex("#9fd39a");
    private static readonly Color BarColor = Color.FromHex("#b3121a");
    private static readonly Color EmptyCell = Color.DimGray.WithAlpha(0.4f);

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly Dictionary<(bool Bold, int Size), Font> _fonts = new();
    private readonly Dictionary<string, (Texture[] Frames, float[] Delays, float Total)?> _icons = new();

    /// <summary>Opacity of the whole card; the system fades it in and out.</summary>
    public float Alpha;

    public WolfmedExplanationCardOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 210; // over the faint's white-out and the dying view
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args) =>
        Alpha > 0.001f && _entities.HasComponent<WolfmedConsciousnessComponent>(_player.LocalEntity);

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } local ||
            !_entities.TryGetComponent(local, out WolfmedConsciousnessComponent? consciousness))
            return;

        _entities.TryGetComponent(local, out WolfmedCardComponent? card);
        var mechanical = _entities.HasComponent<WolfmedSyntheticHudComponent>(local);
        var rows = WolfmedExplanationCard.Rows(_prototypes, consciousness, card, mechanical);
        if (rows.Count == 0)
            return;

        var countdown = WolfmedExplanationCard.Countdown(_prototypes, consciousness, card, _timing.CurTime);
        var bar = WolfmedExplanationCard.Bar(_prototypes, consciousness, card);
        var accent = WolfmedExplanationCard.Colour(_prototypes, consciousness);

        var handle = args.ScreenHandle;
        var control = args.ViewportControl as Control;
        var bounds = WolfmedSyntheticHudLayout.Screen(args.ViewportBounds, control?.GlobalPixelPosition ?? Vector2i.Zero);
        var ui = MathF.Max(0.1f, control?.UIScale ?? 1f);
        var scale = WolfmedExplanationCardLayout.Scale(bounds, ui);
        var width = WolfmedExplanationCardLayout.Width(bounds, scale);
        var padding = Padding * scale;
        var gap = Gap * scale;
        var stripe = StripeWidth * ui;
        var inner = width - stripe - padding * 2f;

        var titleFont = Font(true, TitleSize * scale);
        var countdownFont = Font(true, CountdownSize * scale);
        var bodyFont = Font(false, BodySize * scale);

        // Header: the cause's icon, the title in caps, and the countdown (or the brain bar's label) under it.
        var icon = Icon(WolfmedExplanationCard.CauseAlert(_prototypes, consciousness));
        var iconSize = AlertIconSize * ui;
        var headerLeft = icon != null ? iconSize + gap : 0f;
        var titleRows = Wrap(handle, titleFont, rows[0].Text.ToUpperInvariant(), inner - headerLeft);
        var headlineFont = countdown != null ? countdownFont : bodyFont;
        var headline = countdown?.Text ?? bar?.Label;
        var headlineRows = headline != null ? Wrap(handle, headlineFont, headline, inner - headerLeft) : new List<string>();
        var headerText = titleRows.Count * titleFont.GetLineHeight(1f) + headlineRows.Count * headlineFont.GetLineHeight(1f);
        var headerHeight = MathF.Max(icon != null ? iconSize : 0f, headerText);
        var barHeight = countdown != null || bar != null ? BarHeight * scale : 0f;

        // Body: every row after the title; the blockers' row starts with their icons.
        var blockerIcons = WolfmedExplanationCard.BlockerAlerts(_prototypes, consciousness)
            .Select(alert => Icon(alert)).OfType<Texture>().ToList();
        var blockerSize = BlockerIconSize * ui;
        var iconGap = 2f * ui;
        var body = new List<(WolfmedCardRowKind Kind, List<string> Text, float Indent, float Height)>();
        var bodyHeight = 0f;
        foreach (var row in rows.Skip(1))
        {
            var indent = row.Kind == WolfmedCardRowKind.Blockers && blockerIcons.Count > 0
                ? blockerIcons.Count * (blockerSize + iconGap) + gap / 2f
                : 0f;
            var text = Wrap(handle, bodyFont, row.Text, inner - indent);
            float rowHeight = text.Count * bodyFont.GetLineHeight(1f);
            if (indent > 0f)
                rowHeight = MathF.Max(rowHeight, blockerSize);

            body.Add((row.Kind, text, indent, rowHeight));
            bodyHeight += rowHeight;
        }

        var height = padding * 2f + headerHeight;
        if (barHeight > 0f)
            height += gap + barHeight;
        if (body.Count > 0)
            height += gap + bodyHeight;

        var box = WolfmedExplanationCardLayout.Box(bounds, width, height);
        handle.DrawRect(box, Panel.WithAlpha(Panel.A * Alpha));
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + stripe, box.Bottom), accent.WithAlpha(Alpha));

        var left = box.Left + stripe + padding;
        var y = box.Top + padding;

        if (icon != null)
            handle.DrawTextureRect(icon, UIBox2.FromDimensions(new Vector2(left, y), new Vector2(iconSize, iconSize)),
                Color.White.WithAlpha(Alpha));

        var textY = y + (headerHeight - headerText) / 2f;
        foreach (var text in titleRows)
        {
            handle.DrawString(titleFont, new Vector2(left + headerLeft, textY), text, 1f, TitleColor.WithAlpha(Alpha));
            textY += titleFont.GetLineHeight(1f);
        }

        var headlineColour = countdown != null ? CountdownColor : BodyColor;
        foreach (var text in headlineRows)
        {
            handle.DrawString(headlineFont, new Vector2(left + headerLeft, textY), text, 1f, headlineColour.WithAlpha(Alpha));
            textY += headlineFont.GetLineHeight(1f);
        }

        y += headerHeight;

        if (barHeight > 0f)
        {
            y += gap;
            DrawCells(handle, left, y, inner, barHeight, scale, countdown, bar, accent);
            y += barHeight;
        }

        if (body.Count == 0)
            return;

        y += gap;
        foreach (var (kind, text, indent, rowHeight) in body)
        {
            if (indent > 0f)
            {
                var x = left;
                foreach (var blocker in blockerIcons)
                {
                    handle.DrawTextureRect(blocker,
                        UIBox2.FromDimensions(new Vector2(x, y), new Vector2(blockerSize, blockerSize)),
                        Color.White.WithAlpha(Alpha));
                    x += blockerSize + iconGap;
                }
            }

            var colour = kind switch
            {
                WolfmedCardRowKind.Help => HelpColor,
                WolfmedCardRowKind.Rescue => RescueColor,
                _ => BodyColor,
            };

            // Text beside the icons is centred on them when it is a single line.
            var rowY = indent > 0f && text.Count == 1 ? y + (rowHeight - bodyFont.GetLineHeight(1f)) / 2f : y;
            foreach (var line in text)
            {
                handle.DrawString(bodyFont, new Vector2(left + indent, rowY), line, 1f, colour.WithAlpha(Alpha));
                rowY += bodyFont.GetLineHeight(1f);
            }

            y += rowHeight;
        }
    }

    /// <summary>
    /// Ten cells. The brain bar exactly as M2 drew it (whole tenths, red); the countdown's in the accent colour,
    /// draining smoothly through the cell it is in; ∞ leaves every cell empty.
    /// </summary>
    private void DrawCells(DrawingHandleScreen handle, float left, float top, float width, float height, float scale,
        WolfmedCardCountdown? countdown, (int Tenths, string Label)? bar, Color accent)
    {
        var gap = CellGap * scale;
        var cell = (width - gap * (Cells - 1)) / Cells;
        var filled = countdown is { } timer ? timer.Fraction * Cells : bar?.Tenths ?? 0;
        var colour = countdown != null ? accent : BarColor;

        for (var i = 0; i < Cells; i++)
        {
            var x = left + i * (cell + gap);
            var box = new UIBox2(x, top, x + cell, top + height);
            var share = Math.Clamp(filled - i, 0f, 1f);
            if (bar != null && countdown == null)
                share = i < bar.Value.Tenths ? 1f : 0f;

            if (share >= 1f)
            {
                handle.DrawRect(box, colour.WithAlpha(Alpha));
                continue;
            }

            handle.DrawRect(box, EmptyCell.WithAlpha(EmptyCell.A * Alpha));
            if (share > 0f)
                handle.DrawRect(new UIBox2(x, top, x + cell * share, top + height), colour.WithAlpha(Alpha));
        }
    }

    /// <summary>A crisp font at the drawn pixel size, not a scaled-up small one.</summary>
    private Font Font(bool bold, float size)
    {
        var pixels = Math.Max(6, (int) MathF.Round(size));
        if (_fonts.TryGetValue((bold, pixels), out var font))
            return font;

        font = new VectorFont(_cache.GetResource<FontResource>(bold ? BoldFont : RegularFont), pixels);
        _fonts[(bold, pixels)] = font;
        return font;
    }

    /// <summary>
    /// The alert's icon as the alerts bar shows it (the highest severity's for a severity alert), animated on the
    /// clock. Null when the alert, its sprite or its state cannot be found: no icon, never the error sprite.
    /// Public for the test that every cause's icon resolves.
    /// </summary>
    public Texture? Icon(ProtoId<AlertPrototype>? id)
    {
        if (id is not { } alertId)
            return null;

        if (!_icons.TryGetValue(alertId.Id, out var icon))
            _icons[alertId.Id] = icon = LoadIcon(alertId);

        if (icon is not { } frames)
            return null;

        if (frames.Frames.Length == 1 || frames.Total <= 0f)
            return frames.Frames[0];

        var time = (float) (_timing.RealTime.TotalSeconds % frames.Total);
        for (var i = 0; i < frames.Frames.Length && i < frames.Delays.Length; i++)
        {
            time -= frames.Delays[i];
            if (time < 0f)
                return frames.Frames[i];
        }

        return frames.Frames[^1];
    }

    private (Texture[] Frames, float[] Delays, float Total)? LoadIcon(ProtoId<AlertPrototype> id)
    {
        if (!_prototypes.TryIndex(id, out var alert) || alert.Icons.Count == 0)
            return null;

        switch (alert.SupportsSeverity ? alert.Icons[^1] : alert.Icons[0])
        {
            case SpriteSpecifier.Rsi rsi
                when _cache.TryGetResource<RSIResource>(SpriteSystem.TextureRoot / rsi.RsiPath, out var resource) &&
                     resource.RSI.TryGetState(rsi.RsiState, out var state):
                var frames = state.GetFrames(RsiDirection.South);
                var delays = state.GetDelays();
                return frames.Length == 0 ? null : (frames, delays, delays.Sum());
            case SpriteSpecifier.Texture texture
                when _cache.TryGetResource<TextureResource>(SpriteSystem.TextureRoot / texture.TexturePath, out var loaded):
                return (new[] { loaded.Texture }, new[] { 0f }, 0f);
            default:
                return null;
        }
    }

    /// <summary>Greedy word wrap to the card's inner width.</summary>
    private static List<string> Wrap(DrawingHandleScreen handle, Font font, string text, float width)
    {
        var rows = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' '))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && handle.GetDimensions(font, candidate, 1f).X > width)
            {
                rows.Add(line);
                line = word;
                continue;
            }

            line = candidate;
        }

        if (line.Length > 0)
            rows.Add(line);

        return rows;
    }
}

/// <summary>
/// Playtest 3: where the explanation card sits. Centred, clear of the action buttons on the left and the alerts
/// column and the targeting doll on the right, its bottom above the hotbar; its text grows with the UI scale.
/// </summary>
public static class WolfmedExplanationCardLayout
{
    /// <summary>Widest the card grows, at scale 1.</summary>
    public const float BaseWidth = 560f;

    /// <summary>Share of the screen kept clear on each side: past the action buttons (12%) and short of the alerts (94%).</summary>
    public const float SideMargin = 0.13f;

    /// <summary>The card's bottom edge, as a share of the screen height: just above the hotbar's band (84%).</summary>
    public const float BottomLine = 0.83f;

    /// <summary>The text and padding scale: the viewport's height, times the player's UI scale.</summary>
    public static float Scale(UIBox2 screen, float uiScale) =>
        Math.Clamp(screen.Height / 1000f, 0.75f, 1.5f) * MathF.Max(0.1f, uiScale);

    public static float Width(UIBox2 screen, float scale) =>
        MathF.Min(screen.Width * (1f - 2f * SideMargin), BaseWidth * scale);

    public static UIBox2 Box(UIBox2 screen, float width, float height)
    {
        var left = screen.Left + (screen.Width - width) / 2f;
        var bottom = screen.Top + screen.Height * BottomLine;
        return new UIBox2(left, bottom - height, left + width, bottom);
    }
}

/// <summary>
/// M2 (plan §5.2): a faint's own short white-out, so being briefly knocked out never looks like dying. The system sets
/// the strength; it pulses gently while it lasts.
/// </summary>
public sealed class WolfmedFaintOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public float Alpha;

    public WolfmedFaintOverlay()
    {
        ZIndex = 200;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args) => Alpha > 0.001f;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var bounds = WolfmedSyntheticHudLayout.Screen(args.ViewportBounds,
            (args.ViewportControl as Control)?.GlobalPixelPosition ?? Vector2i.Zero);
        args.ScreenHandle.DrawRect(bounds, Color.FromHex("#f4f1ea").WithAlpha(Alpha));
    }
}

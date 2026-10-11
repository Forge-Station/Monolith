#nullable enable

using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client.Resources;
using Content.Client._WF.Stylesheets;
using Content.Shared._WF.Shuttles;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WF.Cockpit;

/// <summary>A compact fuel warning lens beside the cockpit's remaining-fuel scale.</summary>
public sealed class WFCockpitFuelLamp : Control
{
    private const long FlashTicks = TimeSpan.TicksPerSecond;

    [Dependency] private IGameTiming _timing = default!;

    private readonly Func<ShipFuelSummary?> _read;
    private readonly string _fuel = Loc.GetString("wf-cockpit-fuel");
    private readonly string _low = Loc.GetString("wf-cockpit-fuel-low");
    private string? _fontSkin;
    private Font _small = default!;
    private Font _large = default!;

    /// <summary>Whether reserves are at or under the low threshold; null while the reading is missing or incomplete.</summary>
    public bool? LowFuel => _read() is { Available: true } fuel ? fuel.Low : null;

    /// <summary>Whether reserves are under the critical threshold, which flashes the lamp; null while the reading is missing or incomplete.</summary>
    public bool? CriticalFuel => _read() is { Available: true } fuel ? fuel.Critical : null;

    /// <summary>Whether the lens is lit on the current frame.</summary>
    public bool Lit => IsLit(_read(), _timing.RealTime);

    /// <summary>Steady while low, flashing once a second while critical, and dark without a full reading.</summary>
    public static bool IsLit(ShipFuelSummary? fuel, TimeSpan realTime)
    {
        if (fuel is not { Available: true, Low: true } reading)
            return false;

        return !reading.Critical || realTime.Ticks % FlashTicks < FlashTicks / 2;
    }

    /// <summary>The colour of the lit lens, which is the skin's red in both skins.</summary>
    public static Color Lens(WolfgateSkin skin) => skin.Danger;

    /// <summary>Retains a fixed warning-lamp footprint while the neighbouring fuel gauge expands.</summary>
    public WFCockpitFuelLamp(Func<ShipFuelSummary?> read)
    {
        IoCManager.InjectDependencies(this);
        _read = read;
        Name = "CockpitFuelLow";
        SetWidth = MinWidth = 64;
        SetHeight = 44;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Pass;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        if (_fontSkin != skin.Id)
        {
            var cache = IoCManager.Resolve<IResourceCache>();
            _small = cache.GetFont(skin.MenuFonts, 10);
            _large = cache.GetFont(skin.MenuFonts, 14);
            _fontSkin = skin.Id;
        }
        var lit = IsLit(_read(), _timing.RealTime);
        var inner = WFConsoleAnnunciator.Face(handle, PixelSizeBox, UIScale, Lens(skin), lit);
        var color = lit ? WFInstrumentTheme.Digital ? skin.Danger : skin.Ink : skin.TextMuted;
        var legend = new UIBox2(inner.TopLeft + new Vector2(3 * UIScale),
            inner.BottomRight - new Vector2(3 * UIScale));
        var split = legend.Top + legend.Height * 0.44f;
        Text(handle, _small, _fuel, new UIBox2(legend.Left, legend.Top, legend.Right, split), color);
        Text(handle, _large, _low, new UIBox2(legend.Left, split, legend.Right, legend.Bottom), color);
    }

    private void Text(DrawingHandleScreen handle, Font font, string text, UIBox2 box, Color color)
    {
        var scale = WFInstrumentText.FitScale(handle, font, text, UIScale, box.Width, box.Height);
        if (scale == 0)
            return;
        var width = handle.GetDimensions(font, text, scale).X;
        handle.DrawString(font, box.Center - new Vector2(width / 2, font.GetHeight(scale) / 2f), text, scale, color);
    }
}

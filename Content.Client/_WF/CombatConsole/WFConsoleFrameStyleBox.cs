using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._WF.CombatConsole;

/// <summary>Uses the selected Wolfgate palette for either cockpit housings or clean digital glass.</summary>
public sealed class WFConsoleFrameStyleBox : StyleBox
{
    private readonly bool _housing;
    private readonly bool _bezel;

    /// <summary>Retains the same content insets in both skins so changing theme does not move controls.</summary>
    public WFConsoleFrameStyleBox(int padding, bool housing = false, bool bezel = false)
    {
        _housing = housing;
        _bezel = bezel;
        SetContentMarginOverride(Margin.All, padding);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var skin = WFInstrumentTheme.Skin;
        if (WFInstrumentTheme.Digital)
        {
            WFConsoleDigital.Panel(handle, box, uiScale, _bezel ? skin.Ink : skin.Glass, skin.Edge);
            return;
        }
        if (!_housing)
        {
            WFConsoleMetal.Bevel(handle, box, uiScale, skin.Glass, skin.EdgeLight, true);
            return;
        }
        WFConsoleMetal.MetalPanel(handle, box, uiScale, _bezel ? skin.GlassLight : skin.GlassRaised);
        if (!_bezel || box.Width < 44 * uiScale || box.Height < 44 * uiScale)
            return;

        // Nested metal lips keep the active display clear of decoration and pointer input.
        var outer = new UIBox2(box.TopLeft + new Vector2(5 * uiScale), box.BottomRight - new Vector2(5 * uiScale));
        WFConsoleMetal.Bevel(handle, outer, uiScale, skin.EdgeSoft, skin.EdgeLight);
        var recess = new UIBox2(box.TopLeft + new Vector2(11 * uiScale), box.BottomRight - new Vector2(11 * uiScale));
        WFConsoleMetal.Bevel(handle, recess, uiScale, skin.Ink, skin.Edge, true);
        var lip = new UIBox2(box.TopLeft + new Vector2(19 * uiScale), box.BottomRight - new Vector2(19 * uiScale));
        handle.DrawRect(lip, skin.EdgeSoft, false);
        var screw = 2.5f * uiScale;
        WFConsoleMetal.Screw(handle, outer.TopLeft + new Vector2(2 * uiScale), screw, skin.EdgeLight);
        WFConsoleMetal.Screw(handle, new Vector2(outer.Right - 2 * uiScale, outer.Top + 2 * uiScale), screw, skin.EdgeLight);
        WFConsoleMetal.Screw(handle, outer.BottomRight - new Vector2(2 * uiScale), screw, skin.EdgeLight);
        WFConsoleMetal.Screw(handle, new Vector2(outer.Left + 2 * uiScale, outer.Bottom - 2 * uiScale), screw, skin.EdgeLight);
        var center = (box.Left + box.Right) / 2;
        for (var i = -3; i <= 3; i++)
        {
            var x = center + i * 6 * uiScale;
            handle.DrawLine(new Vector2(x, box.Bottom - 8 * uiScale), new Vector2(x, box.Bottom - 5 * uiScale), skin.Ink);
        }
    }
}

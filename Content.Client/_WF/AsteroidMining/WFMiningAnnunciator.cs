using System.Numerics;
using Content.Client._WF.CombatConsole;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// The mining lamps: the rock is in its window, running hot, overcharging, or the lasers are overloaded. Lit lamps glow
/// and the two warnings flash.
/// </summary>
public sealed class WFMiningAnnunciator : Control
{
    private readonly Font _font;
    private readonly string[] _captions =
    {
        Loc.GetString("wf-mining-lamp-window"),
        Loc.GetString("wf-mining-lamp-hot"),
        Loc.GetString("wf-mining-lamp-overcharge"),
        Loc.GetString("wf-mining-lamp-overload"),
    };
    private readonly bool[] _lit = new bool[4];
    private float _time;

    public WFMiningAnnunciator()
    {
        MinSize = new Vector2(0, 24);
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Pass;
        ToolTip = Loc.GetString("wf-mining-lamps-help");
        _font = new VectorFont(IoCManager.Resolve<IResourceCache>()
            .GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 9);
    }

    /// <summary>Sets which lamps are lit.</summary>
    public void Set(bool window, bool hot, bool overcharge, bool overload)
    {
        _lit[0] = window;
        _lit[1] = hot;
        _lit[2] = overcharge;
        _lit[3] = overload;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        var digital = WFInstrumentTheme.Digital;
        var gap = 3 * UIScale;
        var width = (PixelWidth - gap * (_lit.Length - 1)) / _lit.Length;
        for (var i = 0; i < _lit.Length; i++)
        {
            var box = UIBox2.FromDimensions(new Vector2(i * (width + gap), 0), new Vector2(width, PixelHeight));
            var color = i switch
            {
                0 => skin.Good,
                1 => skin.Caution,
                _ => skin.Danger,
            };
            // Overcharge flashes fast and overload slowly; the others hold steady.
            var lit = _lit[i] && i switch
            {
                2 => _time * 4f % 1f < 0.6f,
                3 => _time * 1.5f % 1f < 0.7f,
                _ => true,
            };
            var inner = new UIBox2(box.TopLeft + new Vector2(2 * UIScale), box.BottomRight - new Vector2(2 * UIScale));
            if (digital)
            {
                WFConsoleDigital.Panel(handle, box, UIScale, lit ? color.WithAlpha(0.22f) : skin.Glass,
                    lit ? color : skin.EdgeSoft, false, 4);
                WFInstrumentText.DrawFitted(handle, _font, _captions[i], inner, UIScale, lit ? color : skin.TextMuted.WithAlpha(0.6f));
                continue;
            }

            WFConsoleMetal.Bevel(handle, box, UIScale, skin.Ink, skin.EdgeSoft, inset: true);
            handle.DrawRect(inner, lit ? color : Color.InterpolateBetween(skin.Ink, color, 0.12f));
            WFInstrumentText.DrawFitted(handle, _font, _captions[i], inner, UIScale,
                lit ? skin.Ink : color.WithAlpha(0.35f));
            WFInstrumentGlass.Window(handle, inner, UIScale);
        }
    }
}

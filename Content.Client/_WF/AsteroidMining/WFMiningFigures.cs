using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._WF.AsteroidMining;

/// <summary>The rock's cracking window, crack progress and yield as three figure windows in a strip under the heat dial.</summary>
public sealed class WFMiningFigures : Control
{
    private readonly Font _small;
    private readonly Font _digits;
    private readonly string[] _captions =
    {
        Loc.GetString("wf-mining-figure-window"),
        Loc.GetString("wf-mining-figure-crack"),
        Loc.GetString("wf-mining-figure-yield"),
    };
    private readonly string[] _values = new string[3];
    private readonly string _unit = Loc.GetString("wf-mining-dial-unit");
    private readonly string _empty = Loc.GetString("wf-mining-dial-empty");
    private WFMiningBeamTargetEntry? _target;

    public WFMiningFigures()
    {
        MinSize = new Vector2(0, 40);
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Pass;
        ToolTip = Loc.GetString("wf-mining-figures-help");
        var cache = IoCManager.Resolve<IResourceCache>();
        _small = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 9);
        _digits = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 16);
    }

    /// <summary>Shows a rock's figures, or dashes for none; they are formatted here rather than every frame.</summary>
    public void SetTarget(WFMiningBeamTargetEntry? target)
    {
        _target = target;
        if (target is not { } rock)
            return;

        _values[0] = Loc.GetString("wf-mining-figure-window-range", ("low", Percent(rock.WindowLow)), ("high", Percent(rock.WindowHigh)));
        _values[1] = Percent(rock.Progress).ToString();
        _values[2] = Percent(rock.Yield).ToString();
    }

    private static int Percent(float value)
    {
        return (int) MathF.Round(value * 100);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        var digital = WFInstrumentTheme.Digital;
        var gap = 3 * UIScale;
        var width = (PixelWidth - gap * (_captions.Length - 1)) / _captions.Length;
        for (var i = 0; i < _captions.Length; i++)
        {
            var box = UIBox2.FromDimensions(new Vector2(i * (width + gap), 0), new Vector2(width, PixelHeight));
            var inner = new UIBox2(box.TopLeft + new Vector2(3 * UIScale), box.BottomRight - new Vector2(3 * UIScale));
            if (digital)
                WFConsoleDigital.Panel(handle, box, UIScale, skin.Glass, skin.EdgeSoft, false, 4);
            else
                WFConsoleMetal.Bevel(handle, box, UIScale, skin.Ink, skin.EdgeSoft, inset: true);

            var caption = new UIBox2(inner.Left, inner.Top, inner.Right, inner.Top + inner.Height * 0.32f);
            var value = new UIBox2(inner.Left, caption.Bottom + UIScale, inner.Right, inner.Bottom);
            WFInstrumentText.DrawFitted(handle, _small, _captions[i], caption, UIScale, skin.TextMuted);
            if (_target is { } rock)
            {
                var color = i switch
                {
                    0 => skin.Good,
                    1 => skin.Accent,
                    _ => rock.Yield >= 0.995f ? skin.Good : skin.Caution,
                };
                WFInstrumentText.DrawReading(handle, _digits, _values[i], _unit, value, UIScale, color, skin.TextMuted);
            }
            else
            {
                WFInstrumentText.DrawFitted(handle, _digits, _empty, value, UIScale, skin.TextMuted.WithAlpha(0.6f));
            }

            if (!digital)
                WFInstrumentGlass.Window(handle, inner, UIScale);
        }
    }
}

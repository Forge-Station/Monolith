using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Wolfmed.Autodoc;

/// <summary>The pod window's look: an amber phosphor terminal in a monospace face, built from flat style boxes.</summary>
public static class AutodocStyle
{
    public static readonly Color Screen = Color.FromHex("#0c0a06");
    public static readonly Color Panel = Color.FromHex("#12100a");
    public static readonly Color Border = Color.FromHex("#6b4f12");
    public static readonly Color Amber = Color.FromHex("#ffb000");
    public static readonly Color AmberDim = Color.FromHex("#9a7a2a");
    public static readonly Color AmberFaint = Color.FromHex("#4d3d15");
    public static readonly Color Alert = Color.FromHex("#ff5a3c");
    public static readonly Color Good = Color.FromHex("#7fdc6a");
    public static readonly Color Cyan = Color.FromHex("#6fd6ff");

    private const string MonoPath = "/Fonts/RobotoMono/RobotoMono-Regular.ttf";
    private const string MonoBoldPath = "/Fonts/RobotoMono/RobotoMono-Bold.ttf";

    // A Font carries the engine's glyph cache of the client it was made on, so fonts are cached per resource
    // cache and two clients (a test pair, a replay) never share one. Both ends are held weakly: a font reaches
    // its client's font manager and through it the whole client, so a static strong reference to either would
    // keep every disposed test client in memory. A font stays cached for as long as a control still uses it.
    private static readonly List<(WeakReference<IResourceCache> Owner, bool Bold, int Size, WeakReference<Font> Font)> Fonts = new();

    public static Font Mono(int size, bool bold = false)
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        lock (Fonts)
        {
            Fonts.RemoveAll(entry => !entry.Owner.TryGetTarget(out _) || !entry.Font.TryGetTarget(out _));
            foreach (var entry in Fonts)
            {
                if (entry.Bold == bold && entry.Size == size && entry.Owner.TryGetTarget(out var owner)
                    && ReferenceEquals(owner, cache) && entry.Font.TryGetTarget(out var cached))
                    return cached;
            }

            var font = new VectorFont(cache.GetResource<FontResource>(bold ? MonoBoldPath : MonoPath), size);
            Fonts.Add((new WeakReference<IResourceCache>(cache), bold, size, new WeakReference<Font>(font)));
            return font;
        }
    }

    public static StyleBoxFlat Box(Color background, Color? border = null, float margin = 6f)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border ?? Color.Transparent,
            BorderThickness = border == null ? default : new Thickness(1),
            ContentMarginLeftOverride = margin,
            ContentMarginRightOverride = margin,
            ContentMarginTopOverride = margin,
            ContentMarginBottomOverride = margin,
        };
    }

    public static Label Text(string text, int size = 11, Color? colour = null, bool bold = false)
    {
        return new Label
        {
            Text = text,
            FontOverride = Mono(size, bold),
            FontColorOverride = colour ?? Amber,
        };
    }

    /// <summary>A section heading drawn as a rule: "== TITLE ==".</summary>
    public static Control Heading(string title, string? right = null)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 4, 0, 2) };
        row.AddChild(Text($"== {title} ", 11, AmberDim, true));
        row.AddChild(new PanelContainer
        {
            HorizontalExpand = true,
            VerticalAlignment = Control.VAlignment.Center,
            MinHeight = 1,
            MaxHeight = 1,
            PanelOverride = Box(AmberFaint, margin: 0f),
        });
        if (right != null)
            row.AddChild(Text($" {right}", 11, AmberDim));
        return row;
    }

    public static Button FlatButton(string text, Color? colour = null, bool accent = false)
    {
        var button = new Button
        {
            Text = text,
            StyleBoxOverride = Box(accent ? AmberFaint : Panel, colour ?? Border, 4f),
        };
        button.Label.FontOverride = Mono(11, accent);
        button.Label.FontColorOverride = colour ?? Amber;
        return button;
    }

    /// <summary>
    /// A scroll box that asks the layout only for its minimum height, so a column of them fits the window and
    /// the expanding one takes what is left. A plain ScrollContainer asks for its whole content and overflows.
    /// </summary>
    public sealed class TerminalScroll : ScrollContainer
    {
        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var size = base.MeasureOverride(availableSize);
            return new Vector2(size.X, MinHeight);
        }
    }

    /// <summary>A text gauge in the terminal's own vocabulary: [#####.....] 50%.</summary>
    public static string Gauge(float fraction, int width = 10)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        var filled = (int) MathF.Round(fraction * width);
        return "[" + new string('#', filled) + new string('.', width - filled) + "]";
    }
}

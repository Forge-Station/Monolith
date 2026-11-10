using System.Linq;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Stylesheets;

/// <summary>
/// Wolfgate retheme. Builds a set of style rules that is appended after the stock Nano/Space rules,
/// so anything it sets wins on equal specificity without forking the upstream stylesheets.
/// Colours, textures and type all come from the <see cref="WolfgateSkin"/> it is built with.
/// </summary>
public sealed partial class StyleWolfgate
{
    private readonly IResourceCache _resCache;
    private readonly WolfgateSkin _skin;
    private readonly StyleRule[] _rules;

    public WolfgateSkin Skin => _skin;

    // Palette shorthands so the rule files read the same whichever skin is active
    private Color Accent => _skin.Accent;
    private Color AccentDim => _skin.AccentDim;
    private Color Text => _skin.Text;
    private Color TextMuted => _skin.TextMuted;
    private Color TextDisabled => _skin.TextDisabled;
    private Color Ink => _skin.Ink;
    private Color Glass => _skin.Glass;
    private Color GlassRaised => _skin.GlassRaised;
    private Color GlassLight => _skin.GlassLight;
    private Color Edge => _skin.Edge;
    private Color EdgeSoft => _skin.EdgeSoft;
    private Color EdgeLight => _skin.EdgeLight;
    private Color Good => _skin.Good;
    private Color Caution => _skin.Caution;
    private Color Danger => _skin.Danger;
    private Color ButtonDefault => _skin.ButtonDefault;
    private Color ButtonHovered => _skin.ButtonHovered;
    private Color ButtonPressed => _skin.ButtonPressed;
    private Color ButtonDisabled => _skin.ButtonDisabled;
    private Color ButtonCautionDefault => _skin.ButtonCautionDefault;
    private Color ButtonCautionHovered => _skin.ButtonCautionHovered;
    private Color ButtonCautionPressed => _skin.ButtonCautionPressed;
    private Color ButtonCautionDisabled => _skin.ButtonCautionDisabled;
    private Color ButtonGoodDefault => _skin.ButtonGoodDefault;
    private Color ButtonGoodHovered => _skin.ButtonGoodHovered;
    private Color ButtonGoodDisabled => _skin.ButtonGoodDisabled;
    private Color ButtonDangerDefault => _skin.ButtonDangerDefault;
    private Color ButtonDangerHovered => _skin.ButtonDangerHovered;
    private Color TopButtonDefault => _skin.TopButtonDefault;
    private Color ContextDefault => _skin.ContextDefault;
    private Color ContextHovered => _skin.ContextHovered;
    private Color ContextPressed => _skin.ContextPressed;
    private Color ChatBackground => _skin.ChatBackground;
    private Color Selection => _skin.Selection;

    public StyleWolfgate(IResourceCache resCache, WolfgateSkin skin)
    {
        _resCache = resCache;
        _skin = skin;
        _rules = BuildRules();
    }

    /// <summary>Returns the given sheet with the Wolfgate rules appended after it.</summary>
    public Stylesheet Apply(Stylesheet baseSheet)
    {
        return new Stylesheet(baseSheet.Rules.Concat(_rules).ToList());
    }

    /// <summary>Display face for titles and headings.</summary>
    private Font Display(int size)
    {
        return _resCache.GetFont(_skin.DisplayFonts, size);
    }

    /// <summary>Face for menu entries such as the lobby navigation.</summary>
    private Font Menu(int size)
    {
        return _resCache.GetFont(_skin.MenuFonts, size);
    }

    /// <summary>Monospace face for key hints and readouts.</summary>
    private Font Mono(int size)
    {
        return _resCache.GetFont(_skin.MonoFonts, size);
    }

    private Texture Tex(string name)
    {
        return _resCache.GetTexture(_skin.TexturePath + name);
    }

    /// <summary>Nine-patch box from a skin texture with a uniform patch margin.</summary>
    private StyleBoxTexture Box(string name, float patch)
    {
        var box = new StyleBoxTexture { Texture = Tex(name) };
        box.SetPatchMargin(StyleBox.Margin.All, patch);
        return box;
    }

    /// <summary>Window header strip: fixed shaped left end and accent line, stretched in the middle.</summary>
    private StyleBoxTexture HeaderBox(string name)
    {
        return new StyleBoxTexture
        {
            Texture = Tex(name),
            PatchMarginLeft = 10,
            PatchMarginRight = 2,
            PatchMarginTop = 8,
            PatchMarginBottom = 4,
            ContentMarginTopOverride = 2,
            ContentMarginBottomOverride = 3,
        };
    }

    private static StyleBoxFlat Flat(Color color, float vertical = 0, float horizontal = 0)
    {
        var box = new StyleBoxFlat { BackgroundColor = color };
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, vertical);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontal);
        return box;
    }
}

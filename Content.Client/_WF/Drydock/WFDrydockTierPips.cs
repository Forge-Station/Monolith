using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Drydock;

/// <summary>
/// A row of refit-mark lamps, Mk II upward, lit up to the mark the ship owns.
/// </summary>
public sealed class WFDrydockTierPips : Control
{
    /// <summary>Mark numerals by tier, where tier 0 is Mk I.</summary>
    private static readonly string[] Numerals = { "I", "II", "III", "IV", "V", "VI" };

    private Font? _font;
    private string? _fontSkin;

    /// <summary>Refit tiers on offer.</summary>
    public int MaxTier { get; set; }

    /// <summary>Tier the ship owns; 0 is none.</summary>
    public int Tier { get; set; }

    /// <summary>Text on the first lamp in place of its numeral, for a single-tier line.</summary>
    public string? Label { get; set; }

    /// <summary>Whether the refit applies to the ship; otherwise every lamp is dark.</summary>
    public bool Lit { get; set; }

    /// <summary>Roman numeral for a tier, where tier 0 is Mk I.</summary>
    public static string Mark(int tier)
    {
        return tier >= 0 && tier < Numerals.Length ? Numerals[tier] : (tier + 1).ToString();
    }

    public WFDrydockTierPips()
    {
        SetHeight = 26;
        MinWidth = 96;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var count = Math.Clamp(MaxTier, 0, Numerals.Length - 1);
        if (count == 0)
            return;

        var skin = WFInstrumentTheme.Skin;
        if (_fontSkin != skin.Id)
        {
            _font = IoCManager.Resolve<IResourceCache>().GetFont(skin.MenuFonts, 12);
            _fontSkin = skin.Id;
        }

        var gap = 3 * UIScale;
        var width = (PixelWidth - gap * (count - 1)) / count;
        for (var i = 0; i < count; i++)
        {
            var box = UIBox2.FromDimensions(new Vector2(i * (width + gap), 0), new Vector2(width, PixelHeight));
            var lit = Lit && i < Tier;
            var color = i == count - 1 ? skin.Good : skin.Accent;
            var inner = WFConsoleAnnunciator.Face(handle, box, UIScale, color, lit);
            var text = lit ? WFInstrumentTheme.Digital ? color : skin.Ink : skin.TextMuted;
            var numeral = i == 0 && Label != null ? Label : Numerals[i + 1];
            var scale = WFInstrumentText.FitScale(handle, _font!, numeral, UIScale, inner.Width - 2 * UIScale, inner.Height);
            if (scale == 0)
                continue;
            var size = handle.GetDimensions(_font!, numeral, scale).X;
            handle.DrawString(_font!, inner.Center - new Vector2(size / 2, _font!.GetHeight(scale) / 2f), numeral, scale, text);
        }
    }
}

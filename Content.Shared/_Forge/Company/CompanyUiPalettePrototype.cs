using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Forge.Company;

/// <summary>
/// Preset color palette for the company PDA management UI.
/// Free players get the grey default; colored presets require an active sponsor.
/// </summary>
[Prototype("companyUiPalette")]
public sealed partial class CompanyUiPalettePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Locale id for the palette display name.
    /// </summary>
    [DataField(required: true)]
    public LocId Name = default!;

    /// <summary>
    /// When true, only players with an active Forge sponsor level may select this palette.
    /// </summary>
    [DataField]
    public bool RequiresSponsor;

    [DataField]
    public Color ContentBackground = Color.FromHex("#1a1a1a");

    [DataField]
    public Color ContentBorder = Color.FromHex("#2a2a2a");

    [DataField]
    public Color Divider = Color.FromHex("#3a3a3a");

    [DataField]
    public Color ListPanelBackground = Color.FromHex("#121212");

    [DataField]
    public Color ListPanelBorder = Color.FromHex("#333333");

    [DataField]
    public Color CreateSquadPanelBackground = Color.FromHex("#181818");

    [DataField]
    public Color CreateSquadPanelBorder = Color.FromHex("#333333");

    [DataField]
    public Color CardBackground = Color.FromHex("#1e1e1e");

    [DataField]
    public Color CardBorder = Color.FromHex("#3a3a3a");

    [DataField]
    public Color EntryBackground = Color.FromHex("#141414");

    [DataField]
    public Color EntryBorder = Color.FromHex("#2e2e2e");

    [DataField]
    public Color MemberAliveBackground = Color.FromHex("#222222");

    [DataField]
    public Color MemberAliveBorder = Color.FromHex("#4a4a4a");

    [DataField]
    public Color TabActive = Color.FromHex("#8a8a8a");

    [DataField]
    public Color TabInactive = Color.FromHex("#5a5a5a");

    [DataField]
    public Color Heading = Color.FromHex("#c0c0c0");

    [DataField]
    public Color Text = Color.FromHex("#a8a8a8");

    [DataField]
    public Color SubText = Color.FromHex("#888888");

    [DataField]
    public Color Placeholder = Color.FromHex("#666666");

    [DataField]
    public Color ButtonBackground = Color.FromHex("#2a2a2a");

    [DataField]
    public Color ButtonBorder = Color.FromHex("#4a4a4a");

    [DataField]
    public Color LineEditBackground = Color.FromHex("#121212");

    [DataField]
    public Color OptionBackground = Color.FromHex("#1a1a1a");

    [DataField]
    public Color TabActiveBackground = Color.FromHex("#2a2a2a");

    [DataField]
    public Color TabInactiveBackground = Color.FromHex("#161616");

    [DataField]
    public Color PanelBackground = Color.FromHex("#121212");
}

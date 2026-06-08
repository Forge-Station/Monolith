// Forge-Change-start
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.SecApartment;

/// <summary>
/// Косметика UI планшета SecApartment. Все значения задаются в YAML.
/// </summary>
[Prototype("secApUiTheme")]
public sealed partial class SecApUiThemePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public bool ShowLogo = true;

    [DataField]
    public bool ShowFooter = true;

    [DataField]
    public bool ShowOsStripe = true;

    [DataField]
    public string? LogoTexture = "/Textures/Interface/Nano/ntlogo.svg.png";

    [DataField]
    public Color WindowBackground = Color.FromHex("#121212");

    [DataField]
    public Color WindowBorder = Color.FromHex("#303030");

    [DataField]
    public Color ContentBackground = Color.FromHex("#BB2222");

    [DataField]
    public Color ContentBorder = Color.FromHex("#440000");

    [DataField]
    public Color CloseButton = Color.FromHex("#ff4444");

    [DataField]
    public Color Divider = Color.FromHex("#ff4444");

    [DataField]
    public Color ListPanelBackground = Color.FromHex("#330000");

    [DataField]
    public Color ListPanelBorder = Color.FromHex("#ff4444");

    [DataField]
    public Color CreateSquadPanelBackground = Color.FromHex("#552222");

    [DataField]
    public Color CreateSquadPanelBorder = Color.FromHex("#ff4444");

    [DataField]
    public Color CardBackground = Color.FromHex("#4a1a1a");

    [DataField]
    public Color CardBorder = Color.FromHex("#ff4444");

    [DataField]
    public Color EntryBackground = Color.FromHex("#110000");

    [DataField]
    public Color EntryBorder = Color.FromHex("#ff4444");

    [DataField]
    public Color MemberAliveBackground = Color.FromHex("#3a0f0f");

    [DataField]
    public Color MemberAliveBorder = Color.FromHex("#ff6666");

    [DataField]
    public Color MemberDeadBackground = Color.FromHex("#1a0a0a");

    [DataField]
    public Color MemberDeadBorder = Color.FromHex("#990000");

    [DataField]
    public Color MemberDeadText = Color.FromHex("#888888");

    [DataField]
    public Color TabActive = Color.FromHex("#ff4444");

    [DataField]
    public Color TabInactive = Color.FromHex("#ff8888");

    [DataField]
    public Color Heading = Color.FromHex("#ff4444");

    [DataField]
    public Color Text = Color.FromHex("#ff9999");

    [DataField]
    public Color SubText = Color.FromHex("#ff8888");

    [DataField]
    public Color Placeholder = Color.FromHex("#ff6666");

    [DataField]
    public Color ButtonBackground = Color.FromHex("#660000");

    [DataField]
    public Color ButtonBorder = Color.FromHex("#ff4444");

    [DataField]
    public Color LineEditBackground = Color.FromHex("#110000");

    [DataField]
    public Color OptionBackground = Color.FromHex("#330000");

    [DataField]
    public Color TabActiveBackground = Color.FromHex("#440000");

    [DataField]
    public Color TabInactiveBackground = Color.FromHex("#220000");

    [DataField]
    public Color PanelBackground = Color.FromHex("#110000");

    [DataField]
    public Color TimerOverdue = Color.FromHex("#ff0000");

    [DataField]
    public Color TimerCritical = Color.FromHex("#ff3333");

    [DataField]
    public Color TimerWarning = Color.FromHex("#ff9933");

    [DataField]
    public Color TimerNormal = Color.FromHex("#ff9999");
}
// Forge-Change-end

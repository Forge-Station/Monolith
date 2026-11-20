using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Stealth;

/// <summary>
/// Gives the wearer of this clothing an action that switches the stealth it grants on and off.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFStealthToggleComponent : Component
{
    /// <summary>Whether the granted stealth is on.</summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [DataField]
    public EntProtoId Action = "WFActionToggleStealth";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    /// <summary>Visibility per second regained while the field fades out after being switched off.</summary>
    [DataField]
    public float FadeOutRate = 2f;

    /// <summary>
    /// How visible the wearer's strongly green pixels (a visor) stay while cloaked, 0 to 1. Zero hides them too.
    /// </summary>
    [DataField]
    public float GlowStrength = 0.85f;
}

/// <summary>
/// On a wearer whose stealth was switched off, so reviving from crit or death leaves it off.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFStealthSuppressedComponent : Component;

/// <summary>On a wearer whose field was switched off and is still fading back into view.</summary>
[RegisterComponent]
public sealed partial class WFStealthFadingOutComponent : Component
{
    [ViewVariables]
    public float Rate;
}

/// <summary>On a cloaked wearer whose visor glows through the field; read by the stealth shader.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFStealthGlowComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Strength;
}

public sealed partial class WFToggleStealthEvent : InstantActionEvent;

using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Features.Components;

/// <summary>
/// Wall keypad. Mappers set <see cref="Code"/>; a matching entry fires device-link ports.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CombinationLockComponent : Component
{
    /// <summary>
    /// Digit-only combination. Configured in mapping / VV.
    /// </summary>
    [DataField]
    public string Code = "1234";

    [DataField]
    public int MaxLength = 8;

    [DataField]
    public string SuccessPort = "Pressed";

    [DataField]
    public string FailurePort = "Off";

    [DataField]
    public SoundSpecifier KeypadSound = new SoundPathSpecifier("/Audio/Machines/Nuke/general_beep.ogg");

    [DataField]
    public SoundSpecifier SuccessSound = new SoundPathSpecifier("/Audio/Machines/high_tech_confirm.ogg");

    [DataField]
    public SoundSpecifier FailureSound = new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg");

    [ViewVariables]
    public string Entered = string.Empty;

    [ViewVariables]
    public CombinationLockStatus Status = CombinationLockStatus.Idle;
}

public enum CombinationLockStatus : byte
{
    Idle,
    Success,
    Failure
}

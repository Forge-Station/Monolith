using Content.Shared.Actions;

namespace Content.Shared._Starlight.NullSpace;

[RegisterComponent]
public sealed partial class NullPhaseComponent : Component
{
    [DataField]
    public EntityUid? PhaseAction;

    [DataField]
    public TimeSpan? Cooldown;

    [DataField]
    public TimeSpan? ShuntCooldown;
}

public sealed partial class NullPhaseActionEvent : InstantActionEvent { }

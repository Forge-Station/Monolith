using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>
/// A Downed body's Call for help (M1a, plan §5.3): the action while Downed, the medical-HUD flag and the
/// cooldown. The flag outlives Downed on purpose: a caller who then goes under still needs finding.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedCallForHelpComponent : Component
{
    /// <summary>Medical HUDs flag the body until this time.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan FlagUntil;

    /// <summary>No new call before this time.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan CooldownUntil;

    /// <summary>The granted action while Downed; server only.</summary>
    [DataField]
    public EntityUid? Action;
}

/// <summary>Shouts a short line and flags the caller on medical HUDs.</summary>
public sealed partial class WolfmedCallForHelpActionEvent : InstantActionEvent
{
    /// <summary>Longest line the player may type, in characters.</summary>
    [DataField]
    public int MaxLength = 60;
}

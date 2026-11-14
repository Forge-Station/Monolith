namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>How a body was deliberately ended (M2, OD17).</summary>
public enum WolfmedEnding : byte
{
    /// <summary>Catastrophic brain injury, revivable by brain (or core) repair and a shock or restart.</summary>
    Execution,

    /// <summary>The same injury; the ghost that already left cannot come back.</summary>
    Suicide,
}

/// <summary>
/// M2 (OD17, P10, P29): raised directed on the victim by the marked lines in the shared execution system. On a wound
/// host the server sets the brain (or positronic core) to 0 where it sits and then kills the body, the order Succumb
/// uses. With a weapon it also leaves the gore that weapon's strength buys. Nothing subscribes on the client.
/// </summary>
[ByRefEvent]
public record struct WolfmedEndingEvent(WolfmedEnding Ending, EntityUid? Attacker = null, EntityUid? Weapon = null, bool Handled = false);

/// <summary>
/// Raised broadcast on the server by the melee Execute verb before its do-after starts. Handled means the server took
/// the start over: it asks the executor "are you sure?" and starts the do-after itself on a yes.
/// </summary>
[ByRefEvent]
public record struct WolfmedExecutionAskEvent(EntityUid Attacker, EntityUid Victim, EntityUid Weapon, bool Handled = false);

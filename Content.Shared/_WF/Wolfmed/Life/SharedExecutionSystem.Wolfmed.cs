using Content.Shared._WF.Wolfmed.Life;
using Robust.Shared.Network;

namespace Content.Shared.Execution;

// The "are you sure?" seam of the melee Execute verb: the verb asks the server, the server starts the do-after.
public sealed partial class SharedExecutionSystem
{
    [Dependency] private INetManager _wolfmedNet = default!;

    private bool _wolfmedConfirmed;

    /// <summary>True when the verb must not start its do-after now: the server asks the executor first.</summary>
    private bool WolfmedAsksFirst(EntityUid weapon, EntityUid victim, EntityUid attacker)
    {
        if (_wolfmedConfirmed)
            return false;

        // Never predicted: a client's own executor is always asked, and only the server hears the answer.
        if (_wolfmedNet.IsClient)
            return true;

        var ask = new WolfmedExecutionAskEvent(attacker, victim, weapon);
        RaiseLocalEvent(ref ask);
        return ask.Handled;
    }

    /// <summary>
    /// Starts the Execute do-after once the executor has said yes. False when the victim can no longer be executed.
    /// </summary>
    public bool StartConfirmedExecution(EntityUid weapon, EntityUid victim, EntityUid attacker)
    {
        if (!TryComp(weapon, out ExecutionComponent? comp) || !CanBeExecuted(victim, attacker))
            return false;

        _wolfmedConfirmed = true;
        try
        {
            TryStartExecutionDoAfter(weapon, victim, attacker, comp);
        }
        finally
        {
            _wolfmedConfirmed = false;
        }

        // The verb's own line to the executor is a predicted popup, and the client predicted none of this.
        var line = attacker == victim ? comp.InternalSelfExecutionMessage : comp.InternalMeleeExecutionMessage;
        ShowExecutionInternalPopup(line, attacker, victim, weapon, false);
        return true;
    }
}

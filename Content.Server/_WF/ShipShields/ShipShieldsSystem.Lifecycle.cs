using Content.Shared._Crescent.ShipShields;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    /// <summary>Checks that a field can still provide protection.</summary>
    private bool IsWolfgateShieldLive(EntityUid shield)
    {
        return !TerminatingOrDeleted(shield) && !EntityManager.IsQueuedForDeletion(shield) &&
            HasComp<ShipShieldComponent>(shield);
    }

    /// <summary>Releases stale emitter references without claiming another owner's field.</summary>
    private void ReconcileWolfgateShieldEmitter(EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        if (emitter.Shield == null && emitter.Shielded == null)
            return;
        if (emitter.Shield is { } shield && emitter.Shielded is { } grid &&
            Transform(uid).GridUid == grid && IsWolfgateShieldLive(shield) &&
            TryComp<ShipShieldedComponent>(grid, out var shielded) && shielded.Shield == shield &&
            shielded.Source == uid)
            return;
        RemoveWolfgateEmitterShield(uid, emitter);
        emitter.Shield = null;
        emitter.Shielded = null;
    }

    /// <summary>Removes only the field that the emitter owns, including after a grid move.</summary>
    private bool RemoveWolfgateEmitterShield(EntityUid uid, ShipShieldEmitterComponent emitter, string reason = "owner unavailable or stale field")
    {
        if (emitter.Shielded is not { } grid || !TryComp<ShipShieldedComponent>(grid, out var shielded) ||
            shielded.Source != uid || !UnshieldEntity(grid, shielded))
            return false;
        LogWolfgateShieldTransition(uid, grid, emitter, false, reason);
        return true;
    }

    /// <summary>Records field transitions without logging ordinary damage or healing ticks.</summary>
    private void LogWolfgateShieldTransition(EntityUid uid, EntityUid grid, ShipShieldEmitterComponent emitter,
        bool online, string reason)
    {
        Log.Info($"Shield {(online ? "online" : "offline")}: emitter={uid}, grid={grid}, " +
            $"health={GetWolfgateShieldHealth(emitter):P1}, damage={emitter.Damage}, " +
            $"recharging={emitter.Recharging}, overload={emitter.OverloadAccumulator}, reason={reason}");
    }
}

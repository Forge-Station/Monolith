using Content.Shared._Crescent.ShipShields;
using Content.Shared._Forge.BoardingTeleport.Components;
using Robust.Shared.GameObjects;

namespace Content.Shared._Forge.BoardingTeleport;

public static class BoardingTeleportShieldHelper
{
    public static int GetEffectiveShieldTier(ShipShieldEmitterComponent emitter)
    {
        if (emitter.ShieldTier > 0)
            return emitter.ShieldTier;

        return emitter.DamageLimit switch
        {
            <= 60_000f => 1,
            <= 120_000f => 2,
            <= 200_000f => 3,
            _ => 4,
        };
    }

    public static int GetEffectiveEngineTier(BoardingTeleportEngineComponent engine)
    {
        if (engine.EngineTier > 0)
            return engine.EngineTier;

        if (engine.ExperimentalPhaseShift)
            return 4;

        return engine.Range switch
        {
            >= 400f => 2,
            _ => 1,
        };
    }

    public static bool TryGetActiveShieldEmitter(
        IEntityManager entMan,
        EntityUid grid,
        out EntityUid emitterUid,
        out ShipShieldEmitterComponent emitter)
    {
        emitterUid = default;
        emitter = default!;

        if (!entMan.TryGetComponent(grid, out ShipShieldedComponent? shielded) ||
            shielded.Source is not { } source ||
            !entMan.TryGetComponent(source, out ShipShieldEmitterComponent? resolvedEmitter) ||
            resolvedEmitter is not { } activeEmitter)
        {
            return false;
        }

        if (!activeEmitter.Online)
            return false;

        emitter = activeEmitter;
        emitterUid = source;
        return true;
    }
    public static bool HasActiveTeleportImmuneShield(IEntityManager entMan, EntityUid grid)
    {
        return TryGetActiveShieldEmitter(entMan, grid, out _, out var emitter) && emitter.BluespaceTeleportImmune;
    }

    public static bool CanEngineBypassTargetShield(
        IEntityManager entMan,
        EntityUid targetGrid,
        BoardingTeleportEngineComponent engine,
        out int shieldTier)
    {
        shieldTier = 0;

        if (!TryGetActiveShieldEmitter(entMan, targetGrid, out _, out var emitter))
            return true;

        if (emitter.BluespaceTeleportImmune)
            return false;

        shieldTier = GetEffectiveShieldTier(emitter);
        var engineTier = GetEffectiveEngineTier(engine);
        return engineTier > shieldTier;
    }
}

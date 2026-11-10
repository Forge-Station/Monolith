using Content.Server._WF.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Robust.Shared.Map.Components;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    /// <summary>Reports absorbed damage with the launch grid preferred over a shooter's current location.</summary>
    private void ReportWolfgateShieldAttack(ShipShieldComponent shield, float damage, EntityUid? launchGrid,
        EntityUid? weapon, EntityUid? shooter)
    {
        if (!float.IsFinite(damage) || damage <= 0f || TerminatingOrDeleted(shield.Shielded)
            || !HasComp<MapGridComponent>(shield.Shielded))
            return;
        var attacker = WolfgateAttackSourceGrid(launchGrid) ?? WolfgateAttackSourceGrid(weapon) ?? WolfgateAttackSourceGrid(shooter);
        if (attacker is not { } attackerGrid || attackerGrid == shield.Shielded)
            return;
        var ev = new WFShipShieldAttackedEvent(shield.Shielded, attackerGrid, shooter, weapon);
        RaiseLocalEvent(shield.Shielded, ref ev, true);
    }

    /// <summary>Resolves an existing source grid, or the free-flying mech it came from, without relying on the projectile's impact parent.</summary>
    private EntityUid? WolfgateAttackSourceGrid(EntityUid? source)
    {
        if (source is not { } uid || TerminatingOrDeleted(uid))
            return null;
        if (HasComp<MapGridComponent>(uid))
            return uid;
        if (TryComp(uid, out TransformComponent? transform) && transform.GridUid is { } grid
            && !TerminatingOrDeleted(grid) && HasComp<MapGridComponent>(grid))
            return grid;
        return EntityManager.System<Content.Server._WF.NpcCrew.Systems.WFCrewAlertSystem>().FreeMechOf(uid);
    }
}

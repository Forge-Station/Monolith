using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.BoardingAction;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Tether;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.BoardingAction;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Fires a raider's disabling guns at the ship it means to board, with no gunner needed: the ion disruptors first, to
/// knock its engines out, then the hacking harpoon, to drop its FTL and park it. Once docked it holds the cable, and lets
/// go when the target is cleared.
/// </summary>
public sealed partial class WFShipDisablerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private RopeSystem _rope = default!;
    [Dependency] private ShipTargetingSystem _targeting = default!;
    [Dependency] private WFEngineDisruptorSystem _engines = default!;

    /// <summary>How far off the disruptors open fire, in metres: inside their bolt's reach.</summary>
    public const float DisruptRange = 800f;

    /// <summary>How far off the harpoon fires, in metres: inside its 250 m cable with room for the ships to drift.</summary>
    public const float HarpoonRange = 200f;

    /// <summary>How long the disruptors try before the harpoon goes in regardless.</summary>
    private static readonly TimeSpan DisruptPatience = TimeSpan.FromSeconds(16);

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.25);

    private TimeSpan _nextUpdate;
    private readonly List<EntityUid> _fire = new();

    /// <summary>Lays a raider's disabling guns on a ship, or stands them down with null.</summary>
    public void SetTarget(EntityUid grid, EntityUid? target)
    {
        if (!TryComp<WFShipDisablerComponent>(grid, out var comp) || comp.Target == target)
            return;

        comp.Target = target;
        comp.DisruptingSince = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;
        var query = EntityQueryEnumerator<WFShipDisablerComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var body, out var xform))
        {
            comp.Guns.RemoveAll(gun => TerminatingOrDeleted(gun));
            var target = comp.Target is { } set && !TerminatingOrDeleted(set) && Transform(set).MapID == xform.MapID
                ? set
                : (EntityUid?) null;

            // A cable into anything but the target is let go.
            var hacking = false;
            foreach (var gun in comp.Guns)
            {
                if (!TryComp<WFHackHarpoonGunComponent>(gun, out var harpoon) || harpoon.Cable is not { } cable)
                    continue;

                if (harpoon.Target == target)
                    hacking = true;
                else
                    _rope.BreakRope(cable);
            }

            if (target is not { } mark || hacking || _docking.AreGridsDocked(uid, mark))
                continue;

            var here = _transform.GetWorldPosition(xform);
            var aim = Centre(mark);
            var distance = Vector2.Distance(here, aim);
            if (distance > DisruptRange)
                continue;

            _fire.Clear();
            var disrupted = _engines.IsDisrupted(mark);
            var disruptors = false;
            foreach (var gun in comp.Guns)
            {
                if (HasComp<WFHackHarpoonGunComponent>(gun))
                    continue;

                disruptors = true;
                if (!disrupted)
                    _fire.Add(gun);
            }

            if (_fire.Count > 0)
                comp.DisruptingSince ??= now;

            // The harpoon goes in once the engines are down, or when the disruptors can't get them down.
            var patience = comp.DisruptingSince is { } since && now - since >= DisruptPatience;
            if (distance <= HarpoonRange && (disrupted || !disruptors || patience))
            {
                _fire.Clear();
                foreach (var gun in comp.Guns)
                {
                    if (!TryComp<WFHackHarpoonGunComponent>(gun, out var harpoon) || harpoon.Cable != null)
                        continue;

                    // A miss or a dart stopped by a shield is reloaded for the next try.
                    if (TryComp<BallisticAmmoProviderComponent>(gun, out var ammo) && ammo.Count == 0)
                        _gun.SetBallisticUnspawned((gun, ammo), 1);
                    _fire.Add(gun);
                }
            }

            if (_fire.Count == 0)
                continue;

            var theirs = TryComp<PhysicsComponent>(mark, out var markBody) ? markBody.LinearVelocity : Vector2.Zero;
            _targeting.FireWeapons(uid, _fire, new MapCoordinates(aim, xform.MapID), body.LinearVelocity, theirs);
        }
    }

    /// <summary>A hull's centre of mass in world space, which lies on the hull where its origin may not.</summary>
    private Vector2 Centre(EntityUid grid)
    {
        if (!TryComp<PhysicsComponent>(grid, out var body))
            return _transform.GetWorldPosition(grid);

        return Vector2.Transform(body.LocalCenter, _transform.GetWorldMatrix(grid));
    }
}

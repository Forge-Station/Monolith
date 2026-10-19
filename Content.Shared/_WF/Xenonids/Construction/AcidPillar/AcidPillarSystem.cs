using Content.Shared._WF.RMCCore.Animations;
using Content.Shared._WF.Xenonids.Construction.Nest;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.Spray;
using Content.Shared.Atmos.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Xenonids.Construction.AcidPillar;

public sealed partial class AcidPillarSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private EntityLookupSystem _entityLookup = default!;
    [Dependency] private SharedXenoHiveSystem _hive = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedRMCAnimationSystem _rmcAnimation = default!;
    [Dependency] private StandingStateSystem _standingState = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private XenoSprayAcidSystem _xenoSprayAcid = default!;

    private readonly HashSet<Entity<MobStateComponent>> _marines = new(); // WOLFGATE: any living non-xeno mob replaces MarineComponent
    private readonly HashSet<Entity<XenoComponent>> _xenos = new();

    private bool CanTarget(EntityUid pillar, EntityUid target)
    {
        if (_mobState.IsDead(target) || HasComp<XenoNestedComponent>(target))
            return false;

        if (_hive.FromSameHiveOrAlly(pillar, target))
        {
            return HasComp<XenoComponent>(target) &&
                   TryComp(target, out FlammableComponent? flammable) &&
                   flammable.OnFire;
        }

        return !_mobState.IsIncapacitated(target) &&
               !_standingState.IsDown(target) &&
               !HasComp<StunnedComponent>(target);
    }

    private void TrySetIfCloserTarget(ref (EntityUid Ent, float Range) closest, EntityUid pillar, EntityUid target, EntityCoordinates coords)
    {
        if (!CanTarget(pillar, target))
            return;

        var targetCoords = _transform.GetMoverCoordinates(target);
        if (!coords.TryDistance(EntityManager, targetCoords, out var distance) ||
            distance >= closest.Range)
        {
            return;
        }

        closest = (target, distance);
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var time = _timing.CurTime;
        var query = EntityQueryEnumerator<AcidPillarComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (HasComp<ActiveAcidSprayingComponent>(uid))
                comp.NextCheck = time + comp.CheckEvery;

            if (time < comp.NextCheck)
                continue;

            if (time < comp.Next)
                continue;

            comp.Next = time + comp.Cooldown;

            _marines.Clear();
            _xenos.Clear();

            var pillarCoords = _transform.GetMoverCoordinates(uid);
            _entityLookup.GetEntitiesInRange(pillarCoords, comp.Range, _marines, LookupFlags.Uncontained);
            _entityLookup.GetEntitiesInRange(pillarCoords, comp.Range, _xenos, LookupFlags.Uncontained);

            (EntityUid Ent, float Range) closest = (default, float.MaxValue);
            foreach (var marine in _marines)
            {
                // xenos are checked below
                if (HasComp<XenoComponent>(marine))
                    continue;

                TrySetIfCloserTarget(ref closest, uid, marine, pillarCoords);
            }

            foreach (var xeno in _xenos)
            {
                TrySetIfCloserTarget(ref closest, uid, xeno, pillarCoords);
            }

            if (closest.Ent == default)
                continue;

            var end = _transform.GetMoverCoordinates(closest.Ent);
            _xenoSprayAcid.CreateLine(uid, pillarCoords, end, comp.AcidSpreadDelay, comp.Range, comp.Acid, false);
            _rmcAnimation.TryFlick(uid, comp.FiringSprite, comp.IdleSprite);
            _audio.PlayPvs(comp.Sound, uid); // WOLFGATE: RMC's pillar fires silently
        }
    }
}

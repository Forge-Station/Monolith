using System.Linq;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Vents;
using Content.Server.Storage.Components;
using Content.Shared._WF.Remade;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.Remade.Director;

/// <summary>
/// Corpse hauling. With no crew near, a Remade now and then walks to a crew corpse, takes hold of it with the upstream pulling
/// system, drags it to a vent and goes in with it. The body comes out of another vent in a morgue tray, by a nest or on the
/// corruption by the stone, where it will turn, and whoever comes back for it finds nothing.
/// </summary>
public sealed partial class WFRemadeDirectorSystem
{
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private WFRemadeConversionSystem _haulConversion = default!;

    // Steering stops within 1.5 m but can stall just short beside a body it pushes against; pulling has no range of its own.
    private const float HaulReach = 2.2f;
    private const float HaulVentReach = 2.2f;
    private const float HaulGiveUp = 90f;
    private const float HaulHidden = 22f;

    /// <summary>Seconds between hauls for one Monolith, by stage. Zero means none.</summary>
    public static (float Min, float Max) HaulEvery(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Harvest => (240f, 400f),
            WFMonolithStage.Rapture => (150f, 260f),
            WFMonolithStage.Crescendo => (100f, 180f),
            _ => (0f, 0f),
        };
    }

    /// <summary>
    /// Sends a free Remade to drag a corpse nobody is watching into the vents. False if no crew are near, no corpse or hauler
    /// is free, or no vent is by the corpse and by a place to put it down.
    /// </summary>
    public bool TryHaul(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        if (ent.Comp2.Stage < WFMonolithStage.Harvest || Hauling(ent.Owner))
            return false;

        GatherCrew(ent, director.AwakeRadius);
        if (_crew.Count == 0)
            return false;

        var options = new List<(EntityUid Corpse, EntityUid Vent, EntityUid Grid)>();
        var bodies = EntityQueryEnumerator<MobStateComponent, HumanoidAppearanceComponent, TransformComponent>();
        while (bodies.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (!_mobState.IsDead(uid)
                || HasComp<WFRemadeComponent>(uid)
                || _container.IsEntityInContainer(uid)
                || xform.GridUid is not { } grid
                || !_haulConversion.CanConvert(uid)
                || SeenByCrew(xform.Coordinates, HaulHidden)
                || NearestVent(grid, _transform.GetWorldPosition(xform), 14f) is not { } vent)
            {
                continue;
            }

            options.Add((uid, vent, grid));
        }

        if (options.Count == 0)
            return false;

        var (corpse, ventIn, gridUid) = _random.Pick(options);
        var corpseAt = Transform(corpse).Coordinates;
        if (FindHauler(director, corpse, gridUid) is not { } hauler || PickDrop(ent, gridUid) is not { } drop)
            return false;

        var dropPosition = _transform.ToMapCoordinates(drop.At).Position;
        if (NearestVent(gridUid, dropPosition, 14f) is not { } exit || exit == ventIn || VentDistance(ventIn, exit) < TripMin)
            return false;

        var haul = AddComp<WFRemadeHaulComponent>(hauler);
        haul.Corpse = corpse;
        haul.Vent = ventIn;
        haul.Exit = exit;
        haul.Drop = drop.At;
        haul.Tray = drop.Tray;
        haul.Phase = WFHaulPhase.Fetch;
        haul.GiveUpAt = now + TimeSpan.FromSeconds(HaulGiveUp);

        // A Remade at its work is left alone by the Director's own steering, as a builder is.
        var behaviour = Comp<WFRemadeBehaviourComponent>(hauler);
        behaviour.Mode = WFRemadeMode.Build;
        SetKnown(hauler, behaviour, corpseAt, now);
        return true;
    }

    private bool Hauling(EntityUid monolith)
    {
        var query = EntityQueryEnumerator<WFRemadeHaulComponent, WFRemadeComponent>();
        while (query.MoveNext(out _, out _, out var remade))
        {
            if (remade.Source == monolith)
                return true;
        }

        return false;
    }

    /// <summary>The Monolith's nearest free Remade on the grid that can pull.</summary>
    private EntityUid? FindHauler(WFRemadeDirectorComponent director, EntityUid corpse, EntityUid grid)
    {
        var at = WorldPos(corpse);
        EntityUid? best = null;
        var bestDistance = 60f * 60f;
        foreach (var uid in director.Horde)
        {
            if (!HasComp<PullerComponent>(uid)
                || HasComp<WFRemadeHaulComponent>(uid)
                || HasComp<WFRemadePlayingDeadComponent>(uid)
                || HasComp<WFRemadeVentTripComponent>(uid)
                || _mobState.IsIncapacitated(uid)
                || _container.IsEntityInContainer(uid)
                || Transform(uid).GridUid != grid
                || !TryComp<WFRemadeBehaviourComponent>(uid, out var behaviour)
                || behaviour.Mode != WFRemadeMode.Hunt
                || InFight(uid))
            {
                continue;
            }

            var distance = (WorldPos(uid) - at).LengthSquared();
            if (distance < bestDistance)
            {
                best = uid;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Where a hauled body goes: a free tray, the floor by a morgue or a nest, or the corruption by the stone.</summary>
    private FakeSite? PickDrop(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, EntityUid grid)
    {
        var sites = FakeSites(ent.Owner, grid, 0f, 90f);
        var stone = WorldPos(ent.Owner);
        var near = new List<EntityCoordinates>();
        foreach (var uid in ent.Comp2.Corruption)
        {
            if (near.Count >= 30)
                break;

            if (TryComp(uid, out TransformComponent? xform) && xform.GridUid == grid && (_transform.GetWorldPosition(xform) - stone).Length() <= 14f)
                near.Add(xform.Coordinates);
        }

        if (near.Count > 0)
            sites.Add(new FakeSite(_random.Pick(near), null));

        if (sites.Count == 0)
            return null;

        // Trays first: a body there turns at once.
        var trays = sites.Where(site => site.Tray != null).ToList();
        return trays.Count > 0 && _random.Prob(0.6f) ? _random.Pick(trays) : _random.Pick(sites);
    }

    /// <summary>Walks each hauler through its job: to the body, with it to the vent, and in.</summary>
    public void UpdateHauls(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFRemadeHaulComponent, WFRemadeBehaviourComponent>();
        while (query.MoveNext(out var uid, out var haul, out var behaviour))
        {
            if (haul.Phase == WFHaulPhase.Travel)
                continue;

            if (TerminatingOrDeleted(haul.Corpse)
                || TerminatingOrDeleted(haul.Vent)
                || _mobState.IsIncapacitated(uid)
                || now >= haul.GiveUpAt
                || behaviour.Mode != WFRemadeMode.Build
                || InFight(uid)
                || SeenByCrew(Transform(uid).Coordinates, 12f))
            {
                AbortHaul(uid, haul, behaviour);
                continue;
            }

            if (haul.Phase == WFHaulPhase.Fetch)
            {
                if (!Arrived(uid, Transform(haul.Corpse).Coordinates, HaulReach))
                {
                    if (behaviour.Known == null)
                        SetKnown(uid, behaviour, Transform(haul.Corpse).Coordinates, now);

                    continue;
                }

                if (!_pulling.TryStartPull(uid, haul.Corpse))
                {
                    AbortHaul(uid, haul, behaviour);
                    continue;
                }

                haul.Phase = WFHaulPhase.Drag;
                SetKnown(uid, behaviour, Transform(haul.Vent).Coordinates, now);
                continue;
            }

            if (!Arrived(uid, Transform(haul.Vent).Coordinates, HaulVentReach))
            {
                if (behaviour.Known == null)
                    SetKnown(uid, behaviour, Transform(haul.Vent).Coordinates, now);

                continue;
            }

            Stash(uid, haul, behaviour, now);
        }
    }

    /// <summary>It lets go of the body at the vent and goes in with it.</summary>
    private void Stash(EntityUid hauler, WFRemadeHaulComponent haul, WFRemadeBehaviourComponent behaviour, TimeSpan now)
    {
        if (CompOrNull<WFRemadeComponent>(hauler)?.Source is not { } holder
            || TerminatingOrDeleted(haul.Exit)
            || !TryComp<PullableComponent>(haul.Corpse, out var pullable))
        {
            AbortHaul(hauler, haul, behaviour);
            return;
        }

        _pulling.TryStopPull(haul.Corpse, pullable);
        var container = _container.EnsureContainer<Container>(holder, HoldId);
        if (!_container.Insert(haul.Corpse, container))
        {
            AbortHaul(hauler, haul, behaviour);
            return;
        }

        if (!TryVentTravel(hauler, haul.Vent, haul.Exit, Transform(haul.Exit).Coordinates, now, null))
        {
            _container.Remove(haul.Corpse, container, destination: Transform(hauler).Coordinates);
            AbortHaul(hauler, haul, behaviour);
            return;
        }

        haul.Phase = WFHaulPhase.Travel;
        Comp<WFRemadeBehaviourComponent>(hauler).AfterVent = WFRemadeMode.Hunt;
    }

    private void AbortHaul(EntityUid hauler, WFRemadeHaulComponent haul, WFRemadeBehaviourComponent behaviour)
    {
        if (!TerminatingOrDeleted(haul.Corpse) && TryComp<PullableComponent>(haul.Corpse, out var pullable) && pullable.Puller == hauler)
            _pulling.TryStopPull(haul.Corpse, pullable);

        RemCompDeferred<WFRemadeHaulComponent>(hauler);
        if (behaviour.Mode == WFRemadeMode.Build)
            behaviour.Mode = WFRemadeMode.Hunt;

        ClearKnown(hauler, behaviour);
    }

    /// <summary>A hauler has come out of its exit vent: the body it brought is set down where it was meant to go.</summary>
    private void FinishHaul(EntityUid hauler)
    {
        if (!TryComp<WFRemadeHaulComponent>(hauler, out var haul))
            return;

        RemCompDeferred<WFRemadeHaulComponent>(hauler);
        var corpse = haul.Corpse;
        if (TerminatingOrDeleted(corpse))
            return;

        if (_container.IsEntityInContainer(corpse))
            _container.TryRemoveFromContainer(corpse, true);

        if (haul.Tray is { } tray && TryComp<EntityStorageComponent>(tray, out var storage) && !storage.Open && !HasContents(storage))
            _container.Insert(corpse, storage.Contents);
        else
            _transform.SetCoordinates(corpse, haul.Drop);

        if (CompOrNull<WFRemadeComponent>(hauler)?.Source is { } stone && TryComp<WFVentStateComponent>(stone, out var state))
            state.Hauls++;
    }
}

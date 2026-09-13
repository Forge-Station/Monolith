using Content.Shared.Eye;
using Robust.Server.GameObjects;
using Content.Server.Atmos.Components;
using Content.Shared.Temperature;
using Content.Shared.Temperature.Components;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using System.Linq;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared._Starlight.NullSpace;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Hands;
using Content.Shared.Shuttles.Components;
using Content.Shared.Stunnable;
using Content.Shared.Movement.Components;
using Content.Shared.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Timing;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Map.Components;
using Content.Shared.Physics;
using Content.Shared.Actions;

namespace Content.Server._Starlight.NullSpace;

public sealed partial class NullSpaceSystem : SharedNullSpaceSystem
{
    [Dependency] private readonly SharedStealthSystem _stealth = default!;
    [Dependency] private readonly EyeSystem _eye = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedVirtualItemSystem _virtualItem = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly NullSpacePhaseSystem _phaseSystem = default!;
    [Dependency] private readonly VisibilitySystem _visibility = default!;
    [Dependency] private readonly InternalsSystem _internals = default!;
    [Dependency] private readonly UseDelaySystem _usedelay = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NullSpaceComponent, MapInitEvent>(OnStartup);
        SubscribeLocalEvent<NullSpaceComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NullSpaceComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<NullSpaceComponent, AtmosExposedGetAirEvent>(OnExpose);
        SubscribeLocalEvent<NullSpaceComponent, VirtualItemDeletedEvent>(OnVirtualItemDeleted);
        SubscribeLocalEvent<NullSpaceComponent, NullSpaceShuntEvent>(NullSpaceShunt);
        SubscribeLocalEvent<NullSpaceComponent, GetVisMaskEvent>(OnGetVisMask);
        SubscribeLocalEvent<TemperatureImmunityComponent, ModifyChangedTemperatureEvent>(OnModifyTemperature); // WOLFGATE
    }

    private void OnGetVisMask(Entity<NullSpaceComponent> uid, ref GetVisMaskEvent args) =>
        args.VisibilityMask |= (int)VisibilityFlags.NullSpace;

    // WOLFGATE - NullSpace added this marker but nothing read it, so entities in NullSpace still changed temperature.
    private void OnModifyTemperature(Entity<TemperatureImmunityComponent> ent, ref ModifyChangedTemperatureEvent args) =>
        args.TemperatureDelta = 0;

    public void OnStartup(EntityUid uid, NullSpaceComponent component, MapInitEvent args)
    {
        var visibility = EnsureComp<VisibilityComponent>(uid);
        _visibility.RemoveLayer((uid, visibility), (int)VisibilityFlags.Normal, false);
        _visibility.AddLayer((uid, visibility), (int)VisibilityFlags.NullSpace, false);
        _visibility.RefreshVisibility(uid, visibility);

        _eye.RefreshVisibilityMask(uid);

        var stealth = EnsureComp<StealthComponent>(uid);
        _stealth.SetVisibility(uid, 0.8f, stealth);

        SuppressFactions(uid, component, true);

        RemComp<KnockedDownComponent>(uid);

        EnsureComp<PressureImmunityComponent>(uid);
        EnsureComp<FTLSmashImmuneComponent>(uid);
        EnsureComp<TemperatureImmunityComponent>(uid);
        EnsureComp<MovementIgnoreGravityComponent>(uid);

        if (TryComp<InternalsComponent>(uid, out var internals))
            _internals.DisconnectTank((uid, internals), forced: true);

        if (TryComp<HandsComponent>(uid, out var handsComponent))
        {
            foreach (var hand in _hands.EnumerateHands(uid, handsComponent))
            {
                if (hand.HeldEntity.HasValue)
                {
                    if (HasComp<UnremoveableComponent>(hand.HeldEntity))
                        continue;

                    if (TryComp<VirtualItemComponent>(hand.HeldEntity, out var vcomp))
                        if (HasComp<NullSpacePulledComponent>(vcomp.BlockingEntity) && TryComp<PullableComponent>(vcomp.BlockingEntity, out var pulling) && pulling.BeingPulled)
                        {
                            RemComp<NullSpacePulledComponent>(vcomp.BlockingEntity);
                            // safety check just to make sure you dont pull something out of nullspace by phasing in
                            if (!HasComp<NullSpaceComponent>(vcomp.BlockingEntity)) _phaseSystem.Phase(vcomp.BlockingEntity);
                            continue;
                        }

                    _hands.DoDrop(uid, hand, true, handsComponent);
                }

                if (_virtualItem.TrySpawnVirtualItemInHand(uid, uid, out var virtItem))
                    EnsureComp<UnremoveableComponent>(virtItem.Value);
            }
        }

        if (TryComp<PullableComponent>(uid, out var pullable) && pullable.BeingPulled)
        {
            // if thing pulling is in nullspace, you're coming along with them.
            if (!HasComp<NullSpaceComponent>(pullable.Puller!.Value))
                _pulling.TryStopPull(uid, pullable);
        }
    }

    public void OnShutdown(EntityUid uid, NullSpaceComponent component, ComponentShutdown args)
    {
        if (TryComp<VisibilityComponent>(uid, out var visibility))
        {
            _visibility.RemoveLayer((uid, visibility), (int)VisibilityFlags.NullSpace, false);
            _visibility.AddLayer((uid, visibility), (int)VisibilityFlags.Normal, false);
            _visibility.RefreshVisibility(uid, visibility);
        }

        SuppressFactions(uid, component, false);

        RemComp<StealthComponent>(uid);
        RemComp<PressureImmunityComponent>(uid);
        RemComp<FTLSmashImmuneComponent>(uid);
        RemComp<TemperatureImmunityComponent>(uid);

        _virtualItem.DeleteInHandsMatching(uid, uid);

        TrySlideToFreeTile(uid);
    }

    private static readonly Vector2i[] AdjacentOffsets =
    [
        new(0, 1), new(0, -1), new(1, 0), new(-1, 0),
        new(1, 1), new(-1, 1), new(1, -1), new(-1, -1),
    ];

    private void TrySlideToFreeTile(EntityUid uid)
    {
        var currentTile = _turf.GetTileRef(Transform(uid).Coordinates);
        if (currentTile == null)
            return;

        // MobMask covers everything a normal mob cannot walk through: walls (Impassable),
        // but also windows, shutters, glass airlocks, half-walls, etc.
        if (!_turf.IsTileBlocked(currentTile.Value, CollisionGroup.MobMask))
            return;

        if (!TryComp<MapGridComponent>(currentTile.Value.GridUid, out var grid))
            return;

        Span<int> indices = stackalloc int[AdjacentOffsets.Length];
        foreach (var i in ShuffledRange(indices))
        {
            var tileIndices = currentTile.Value.GridIndices + AdjacentOffsets[i];
            if (!_mapSystem.TryGetTileRef(currentTile.Value.GridUid, grid, tileIndices, out var candidate))
                continue;

            if (!_turf.IsTileBlocked(candidate, CollisionGroup.MobMask))
            {
                _transform.SetCoordinates(uid, _turf.GetTileCenter(candidate));
                return;
            }
        }

        // Second pass: all neighbours were MobMask-blocked (e.g. surrounded by windows/shutters).
        // Settle for any tile free of solid walls so the entity is at least not geometry-clipping.
        foreach (var i in indices)
        {
            var tileIndices = currentTile.Value.GridIndices + AdjacentOffsets[i];
            if (!_mapSystem.TryGetTileRef(currentTile.Value.GridUid, grid, tileIndices, out var candidate))
                continue;

            if (!_turf.IsTileBlocked(candidate, CollisionGroup.Impassable))
            {
                _transform.SetCoordinates(uid, _turf.GetTileCenter(candidate));
                return;
            }
        }
    }

    private Span<int> ShuffledRange(Span<int> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = i;
        _random.Shuffle(buffer);
        return buffer;
    }

    public void OnRemove(EntityUid uid, NullSpaceComponent component, ComponentRemove args)
    {
        _eye.RefreshVisibilityMask(uid);

        RemComp<MovementIgnoreGravityComponent>(uid);
    }

    private void OnVirtualItemDeleted(EntityUid uid, NullSpaceComponent component, VirtualItemDeletedEvent args)
    {
        if (TryComp<HandsComponent>(uid, out var handsComponent))
        {
            foreach (var hand in _hands.EnumerateHands(uid, handsComponent))
            {
                if (hand.HeldEntity.HasValue)
                {
                    if (HasComp<UnremoveableComponent>(hand.HeldEntity))
                        continue;

                    if (TryComp<VirtualItemComponent>(hand.HeldEntity, out var vcomp))
                    {
                        // safety check just to make sure you dont pull something into nullspace by phasing out.
                        if (HasComp<NullSpaceComponent>(vcomp.BlockingEntity)) _phaseSystem.Phase(vcomp.BlockingEntity);
                        continue;
                    }

                    _hands.DoDrop(uid, hand, true, handsComponent);
                }

                if (_virtualItem.TrySpawnVirtualItemInHand(uid, uid, out var virtItem))
                    EnsureComp<UnremoveableComponent>(virtItem.Value);
            }
        }
    }

    private void NullSpaceShunt(EntityUid uid, NullSpaceComponent component, NullSpaceShuntEvent args)
    {
        if (TryComp<NullPhaseComponent>(uid, out var nullphase) && nullphase.ShuntCooldown is not null)
        {
            _usedelay.SetLength(uid, nullphase.ShuntCooldown.Value, "nullphase-delay");
            _actionsSystem.SetCooldown(nullphase.PhaseAction, nullphase.ShuntCooldown.Value);
            _usedelay.TryResetDelay(uid, id: "nullphase-delay");
        }

        SpawnAtPosition(_shadekinShadow, Transform(uid).Coordinates);
        RemComp(uid, component);
    }

    public void SuppressFactions(EntityUid uid, NullSpaceComponent component, bool set)
    {
        if (set)
        {
            if (!TryComp<NpcFactionMemberComponent>(uid, out var factions))
                return;

            component.SuppressedFactions = factions.Factions.ToList();

            foreach (var faction in factions.Factions)
                _factions.RemoveFaction(uid, faction);
        }
        else
        {
            foreach (var faction in component.SuppressedFactions)
                _factions.AddFaction(uid, faction);

            component.SuppressedFactions.Clear();
        }
    }

    private void OnExpose(EntityUid uid, NullSpaceComponent component, ref AtmosExposedGetAirEvent args)
    {
        if (args.Handled)
            return;

        args.Gas = null;
        args.Handled = true;
    }
}

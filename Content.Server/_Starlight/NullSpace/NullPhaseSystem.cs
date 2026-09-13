using Content.Shared.Inventory.Events;
using Content.Shared.Clothing.Components;
using Content.Shared.Actions;
using Content.Shared._Starlight.NullSpace;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Content.Shared.Popups;
using Content.Shared.Physics;
using System.Linq;
using Content.Server.Ghost;
using Robust.Server.Containers;
using Robust.Shared.Prototypes;
using Content.Shared.Light.Components;
using Robust.Shared.Containers;
using Content.Shared.Mobs.Components;
using Content.Shared.Inventory;
using Content.Shared._Starlight.Shadekin;
using Content.Shared.Timing;
using Robust.Shared.Timing;
using Content.Server._Starlight.Shadekin;
using Content.Shared.Sprite;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Humanoid;
using System.Numerics;

namespace Content.Server._Starlight.NullSpace;

public sealed class NullSpacePhaseSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly GhostSystem _ghost = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly UseDelaySystem _usedelay = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedScaleVisualsSystem _scaleVisuals = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;

    private readonly EntProtoId _shadekinShadow = "ShadekinShadow";
    private readonly EntProtoId ShadekinPhaseInEffect = "ShadekinPhaseInEffect";
    private readonly EntProtoId ShadekinPhaseOutEffect = "ShadekinPhaseOutEffect";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NullPhaseComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<NullPhaseComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NullPhaseComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<NullPhaseComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<NullPhaseComponent, NullPhaseActionEvent>(OnPhaseAction);
    }

    private void OnInit(EntityUid uid, NullPhaseComponent component, MapInitEvent args)
    {
        Toggle(uid, component, true);
    }

    public void OnShutdown(EntityUid uid, NullPhaseComponent component, ComponentShutdown args)
    {
        Toggle(uid, component, false);
    }

    private void OnEquipped(EntityUid uid, NullPhaseComponent component, GotEquippedEvent args)
    {
        if (HasComp<BrighteyeComponent>(args.Equipee)) return;

        if (!TryComp<ClothingComponent>(uid, out var clothing)
            || !clothing.Slots.HasFlag(args.SlotFlags))
            return;

        var nullphase = EnsureComp<NullPhaseComponent>(args.Equipee);
        nullphase.Cooldown = component.Cooldown;
        nullphase.ShuntCooldown = component.ShuntCooldown;

        if (TryComp<UseDelayComponent>(uid, out var usedelay) && _usedelay.IsDelayed((uid, usedelay), "nullphase-delay"))
        {
            if (_usedelay.TryGetDelayInfo(uid, out var info, "nullphase-delay"))
            {
                _usedelay.SetLength(args.Equipee, info.EndTime - _gameTiming.CurTime, "nullphase-delay");
                _usedelay.TryResetDelay(args.Equipee, id: "nullphase-delay");
            }

            _usedelay.CancelDelay((uid, usedelay), "nullphase-delay");
        }
    }

    private void OnUnequipped(EntityUid uid, NullPhaseComponent component, GotUnequippedEvent args)
    {
        if (HasComp<BrighteyeComponent>(args.Equipee)) return;

        if (TryComp<NullPhaseComponent>(args.Equipee, out var nullphase))
        {
            component.Cooldown = nullphase.Cooldown;
            component.ShuntCooldown = nullphase.ShuntCooldown;

            if (TryComp<UseDelayComponent>(args.Equipee, out var usedelay) && _usedelay.IsDelayed((args.Equipee, usedelay), "nullphase-delay"))
            {
                if (_usedelay.TryGetDelayInfo(args.Equipee, out var info, "nullphase-delay"))
                {
                    _usedelay.SetLength(uid, info.EndTime - _gameTiming.CurTime, "nullphase-delay");
                    _usedelay.TryResetDelay(uid, id: "nullphase-delay");
                }

                _usedelay.CancelDelay((args.Equipee, usedelay), "nullphase-delay");
            }
        }

        RemComp<NullPhaseComponent>(args.Equipee);
    }

    private void OnPhaseAction(EntityUid uid, NullPhaseComponent component, NullPhaseActionEvent args)
    {
        if (CanPhase(uid))
            Phase(uid);

        args.Handled = true;
    }

    private void Toggle(EntityUid uid, NullPhaseComponent component, bool toggle)
    {
        if (toggle)
            _actionsSystem.AddAction(uid, ref component.PhaseAction, "NullPhaseAction", uid);
        else
            _actionsSystem.RemoveAction(uid, component.PhaseAction);
    }

    public bool CanPhase(EntityUid uid)
    {
        if (TryComp<NullSpaceComponent>(uid, out var nullspace))
        {
            var currentTile = _turf.GetTileRef(Transform(uid).Coordinates);
            // MobMask (not just Impassable) so that windows, shutters, glass airlocks, and anything
            // else a normal mob cannot walk through also prevents exit — not only solid walls.
            if (currentTile != null && _turf.IsTileBlocked(currentTile.Value, CollisionGroup.MobMask))
            {
                _popup.PopupEntity(Loc.GetString("revenant-in-solid"), uid, uid);
                return false;
            }
        }
        else
        {
            // HL - UseDelay.
            if (_usedelay.IsDelayed(uid, "nullphase-delay"))
                return false;

            if (HasComp<NullSpaceDrainerComponent>(uid))
            {
                _popup.PopupEntity(Loc.GetString("phase-fail-generic"), uid, uid);
                return false;
            }

            // No phaising if were in a container.
            if (_container.IsEntityInContainer(uid))
            {
                _popup.PopupEntity(Loc.GetString("phase-fail-generic"), uid, uid);
                return false;
            }

            // No phaising if were blocked by a NullSpaceBlockerComponent entity.
            foreach (var entity in _lookup.GetEntitiesIntersecting(Transform(uid).Coordinates))
            {
                if (HasComp<NullSpaceBlockerComponent>(entity))
                {
                    _popup.PopupEntity(Loc.GetString("phase-fail-generic"), uid, uid);
                    return false;
                }
            }

            // No phaising if were holding or have an entity with the MobStateComponent (including backpack)
            if (TryComp<InventoryComponent>(uid, out var inventoryComponent) && _inventorySystem.TryGetSlots(uid, out var slots))
                foreach (var slot in slots)
                    if (_inventorySystem.TryGetSlotEntity(uid, slot.Name, out var slotEnt, inventoryComponent))
                    {
                        if (HasComp<MobStateComponent>(slotEnt))
                        {
                            _popup.PopupEntity(Loc.GetString("phase-fail-generic"), uid, uid);
                            return false;
                        }

                        if (TryComp<ContainerManagerComponent>(slotEnt, out var containercomp))
                            foreach (var container in containercomp.Containers.Values)
                                foreach (var contEnt in container.ContainedEntities)
                                    if (HasComp<MobStateComponent>(contEnt))
                                    {
                                        _popup.PopupEntity(Loc.GetString("phase-fail-generic"), uid, uid);
                                        return false;
                                    }
                    }
        }

        return true;
    }

    public void Phase(EntityUid uid)
    {
        if (TryComp<NullSpaceComponent>(uid, out var nullspace))
        {
            if (TryComp<NullPhaseComponent>(uid, out var nullphase) && nullphase.Cooldown is not null)
            {
                _usedelay.SetLength(uid, nullphase.Cooldown.Value, "nullphase-delay");
                _usedelay.TryResetDelay(uid, checkDelayed: true, id: "nullphase-delay");
            }

            if (TryComp<ShadekinComponent>(uid, out var shadekin))
            {
                var lightQuery = _lookup.GetEntitiesInRange(uid, 5, flags: LookupFlags.StaticSundries)
                    .Where(x => HasComp<PoweredLightComponent>(x));
                foreach (var light in lightQuery)
                    _ghost.DoGhostBooEvent(light);

                SetPhaseEffect(uid, ShadekinPhaseInEffect);
            }
            else
                SetPhaseEffect(uid, _shadekinShadow);

            RemComp(uid, nullspace);
        }
        else
        {
            if (TryComp<PullerComponent>(uid, out var puller) && puller.Pulling is not null)
                EnsureComp<NullSpacePulledComponent>(puller.Pulling.Value);

            EnsureComp<NullSpaceComponent>(uid);
            RemComp<ShadegenComponent>(uid);

            if (TryComp<ShadekinComponent>(uid, out var shadekin))
            {
                var lightQuery = _lookup.GetEntitiesInRange(uid, 5, flags: LookupFlags.StaticSundries)
                    .Where(x => HasComp<PoweredLightComponent>(x));
                foreach (var light in lightQuery)
                    _ghost.DoGhostBooEvent(light);

                SetPhaseEffect(uid, ShadekinPhaseOutEffect);
            }
            else
                SetPhaseEffect(uid, _shadekinShadow);
        }
    }

    private void SetPhaseEffect(EntityUid uid, EntProtoId effectproto)
    {
        var effect = SpawnAtPosition(effectproto, Transform(uid).Coordinates);

        var scaled = _scaleVisuals.GetSpriteScale(uid);
        _scaleVisuals.SetSpriteScale(effect, scaled);
        Transform(effect).LocalRotation = Transform(uid).LocalRotation;
    }
}

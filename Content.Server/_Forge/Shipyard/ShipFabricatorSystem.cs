using System.Linq;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._Forge.Shipyard;
using Content.Shared._Forge.Shipyard.Components;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Server.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.UserInterface;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Shipyard;

public sealed class ShipFabricatorSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShipFabricatorComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ShipFabricatorComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<ShipFabricatorComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ShipFabricatorComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<ShipFabricatorComponent, EntRemovedFromContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<ShipFabricatorComponent, ShipFabricatorStartMessage>(OnStart);
        SubscribeLocalEvent<ShipFabricatorComponent, ShipFabricatorEjectBlueprintMessage>(OnEjectBlueprint);
        SubscribeLocalEvent<ShipFabricatorComponent, AfterActivatableUIOpenEvent>(OnUiOpen);
        SubscribeLocalEvent<ShipFabricatorComponent, PowerChangedEvent>(OnPowerChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<ShipFabricatorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out _))
        {
            if (!comp.Fabricating)
                continue;

            if (_timing.CurTime < comp.FabricationEndTime)
                continue;

            CompleteFabrication(uid, comp);
        }
    }

    private void OnInit(EntityUid uid, ShipFabricatorComponent component, ComponentInit args)
    {
        component.BlueprintContainer = _container.EnsureContainer<Container>(uid, ShipFabricatorComponent.BlueprintContainerName);
        _itemSlots.AddItemSlot(uid, ShipFabricatorComponent.TargetIdCardSlotId, component.TargetIdSlot);
    }

    private void OnRemove(EntityUid uid, ShipFabricatorComponent component, ComponentRemove args) =>
        _itemSlots.RemoveItemSlot(uid, component.TargetIdSlot);

    private void OnPowerChanged(EntityUid uid, ShipFabricatorComponent component, ref PowerChangedEvent args)
    {
        if (!args.Powered && component.Fabricating)
        {
            component.Fabricating = false;
            component.FabricatingVessel = null;
            UpdateUi(uid, component);
        }
    }

    private void OnUiOpen(EntityUid uid, ShipFabricatorComponent component, AfterActivatableUIOpenEvent args) => UpdateUi(uid, component);

    private void OnContainerModified(EntityUid uid, ShipFabricatorComponent component, ContainerModifiedMessage args)
    {
        if (args.Container.ID != ShipFabricatorComponent.BlueprintContainerName)
            return;

        if (!component.Fabricating)
            UpdateActiveVessel(uid, component);

        UpdateUi(uid, component);
    }

    private void OnInteractUsing(EntityUid uid, ShipFabricatorComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<VesselBlueprintComponent>(args.Used, out var blueprint))
            return;

        if (component.BlueprintWhitelist != null && !_whitelist.IsWhitelistPass(component.BlueprintWhitelist, args.Used))
            return;

        if (!_container.Insert(args.Used, component.BlueprintContainer))
            return;

        args.Handled = true;
        UpdateActiveVessel(uid, component);
        UpdateUi(uid, component);
    }

    private void OnEjectBlueprint(EntityUid uid, ShipFabricatorComponent component, ShipFabricatorEjectBlueprintMessage args)
    {
        if (component.Fabricating || component.BlueprintContainer.Count == 0)
            return;

        foreach (var ent in component.BlueprintContainer.ContainedEntities.ToArray())
            _container.Remove(ent, component.BlueprintContainer);

        component.ActiveVessel = null;
        UpdateUi(uid, component);
    }

    private void OnStart(EntityUid uid, ShipFabricatorComponent component, ShipFabricatorStartMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (component.Fabricating || component.ActiveVessel == null)
            return;

        if (!_proto.TryIndex(component.ActiveVessel.Value, out VesselPrototype? vessel))
            return;

        if (!TryComp<ApcPowerReceiverComponent>(uid, out var power) || !power.Powered)
        {
            _popup.PopupEntity(Loc.GetString("ship-fabricator-no-power"), uid, user);
            return;
        }

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            _popup.PopupEntity(Loc.GetString("shipyard-console-no-idcard"), uid, user);
            return;
        }

        if (TryComp<ShuttleDeedComponent>(targetId, out var existingDeed) && existingDeed.ShuttleUid is { Valid: true })
        {
            _popup.PopupEntity(Loc.GetString("shipyard-console-already-has-deed"), uid, user);
            return;
        }

        if (HasComp<ShuttleDeedComponent>(targetId))
            RemComp<ShuttleDeedComponent>(targetId);

        var materials = VesselFabricationHelper.GetFabricationMaterials(vessel)
            .ToDictionary(p => p.Key.Id, p => p.Value);
        if (!TryComp<MaterialStorageComponent>(uid, out var storage)
            || !_materialStorage.CanChangeMaterialAmount((uid, storage), materials))
        {
            _popup.PopupEntity(Loc.GetString("ship-fabricator-insufficient-materials"), uid, user);
            return;
        }

        var consumed = materials.ToDictionary(p => p.Key, p => -p.Value);
        _materialStorage.TryChangeMaterialAmount((uid, storage), consumed, localOnly: false);

        component.FabricatingVessel = component.ActiveVessel;

        foreach (var blueprint in component.BlueprintContainer.ContainedEntities.ToArray())
            QueueDel(blueprint);

        component.Fabricating = true;
        component.FabricationEndTime = _timing.CurTime + vessel.FabricationTime;
        component.FabricatingActor = user;
        component.FabricatingIdCard = targetId;
        UpdateUi(uid, component);
    }

    private void CompleteFabrication(EntityUid uid, ShipFabricatorComponent component)
    {
        component.Fabricating = false;
        var vesselId = component.FabricatingVessel;
        component.FabricatingVessel = null;
        component.ActiveVessel = null;

        if (vesselId == null)
        {
            UpdateUi(uid, component);
            return;
        }

        if (!_proto.TryIndex(vesselId.Value, out VesselPrototype? vessel))
        {
            UpdateUi(uid, component);
            return;
        }

        var shipyard = EntityManager.System<ShipyardSystem>();
        var stationUid = _station.GetOwningStation(uid);
        if (stationUid == null)
        {
            _popup.PopupEntity(Loc.GetString("ship-fabricator-no-station"), uid);
            UpdateUi(uid, component);
            return;
        }

        if (!shipyard.TryPurchaseShuttle(stationUid.Value, vessel.ShuttlePath, out var shuttleUid) || shuttleUid == null)
        {
            _popup.PopupEntity(Loc.GetString("ship-fabricator-spawn-failed"), uid);
            component.FabricatingActor = null;
            component.FabricatingIdCard = null;
            UpdateUi(uid, component);
            return;
        }

        var player = component.FabricatingActor;
        var targetId = component.FabricatingIdCard;
        component.FabricatingActor = null;
        component.FabricatingIdCard = null;

        if (player is not { Valid: true } || targetId is not { Valid: true }
            || !shipyard.TryAssignFabricatedShuttle(player.Value, targetId.Value, shuttleUid.Value, vessel))
        {
            _popup.PopupEntity(Loc.GetString("ship-fabricator-deed-failed"), uid);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("ship-fabricator-complete", ("vessel", vessel.Name)), uid, player.Value);
        }

        UpdateUi(uid, component);
    }

    private void UpdateActiveVessel(EntityUid uid, ShipFabricatorComponent component)
    {
        component.ActiveVessel = null;
        foreach (var ent in component.BlueprintContainer.ContainedEntities)
        {
            if (TryComp<VesselBlueprintComponent>(ent, out var bp))
            {
                component.ActiveVessel = bp.Vessel;
                break;
            }
        }
    }

    private ProtoId<VesselPrototype>? GetDisplayedVessel(ShipFabricatorComponent component) =>
        component.Fabricating ? component.FabricatingVessel : component.ActiveVessel;

    private void UpdateUi(EntityUid uid, ShipFabricatorComponent component)
    {
        var displayedVessel = GetDisplayedVessel(component);
        var progress = 0f;
        TimeSpan remaining = TimeSpan.Zero;
        if (component.Fabricating && displayedVessel != null && _proto.TryIndex(displayedVessel.Value, out VesselPrototype? vessel))
        {
            var total = vessel.FabricationTime.TotalSeconds;
            remaining = component.FabricationEndTime - _timing.CurTime;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
            progress = total > 0 ? (float) (1 - remaining.TotalSeconds / total) : 1f;
        }

        Dictionary<string, int> requirements = new();
        Dictionary<string, int> stored = new();
        string? vesselName = null;

        if (displayedVessel != null && _proto.TryIndex(displayedVessel.Value, out VesselPrototype? v))
        {
            vesselName = v.Name;
            foreach (var (mat, amt) in VesselFabricationHelper.GetFabricationMaterials(v))
                requirements[mat.Id] = amt;
        }

        if (TryComp<MaterialStorageComponent>(uid, out var storage))
        {
            foreach (var (mat, amt) in storage.Storage)
                stored[mat.Id] = amt;
        }

        var state = new ShipFabricatorBoundUserInterfaceState(
            component.Fabricating,
            progress,
            displayedVessel,
            vesselName,
            requirements,
            stored,
            component.BlueprintContainer.Count > 0,
            remaining);

        _ui.SetUiState(uid, ShipFabricatorUiKey.Key, state);
    }
}

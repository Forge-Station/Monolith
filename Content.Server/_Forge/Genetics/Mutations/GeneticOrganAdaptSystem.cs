using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Shared._Forge.Genetics.Mutations;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Genetics.Mutations;

public sealed partial class GeneticOrganAdaptSystem : EntitySystem
{
    public const string AirSacContainerId = "genetic-air-sac";

    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedGasTankSystem _gasTank = default!;
    [Dependency] private SharedInternalsSystem _internals = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticOrganAdaptComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GeneticOrganAdaptComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<GeneticOrganAdaptComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<PressureImmunityComponent>(ent.Owner))
        {
            EnsureComp<PressureImmunityComponent>(ent.Owner);
            ent.Comp.AddedPressureImmunity = true;
        }

        if (TryComp<BloodstreamComponent>(ent.Owner, out var blood))
        {
            ent.Comp.OriginalBloodMaxVolume = blood.BloodMaxVolume;
            var newMax = FixedPoint2.New((float) blood.BloodMaxVolume * ent.Comp.BloodVolumeFactor);
            blood.BloodMaxVolume = newMax;

            if (_solutions.ResolveSolution(ent.Owner, blood.BloodSolutionName, ref blood.BloodSolution))
                _solutions.SetCapacity(blood.BloodSolution.Value, newMax);
        }

        var sac = Spawn(ent.Comp.AirSacPrototype, Transform(ent.Owner).Coordinates);
        ent.Comp.AirSacEntity = sac;

        // Prefer nesting inside the backpack (same slot UI) without replacing it.
        var placed = false;
        if (_inventory.TryGetSlotEntity(ent.Owner, "back", out var backpack) &&
            _storage.Insert(backpack.Value, sac, out _, user: ent.Owner, playSound: false))
        {
            placed = true;
        }
        else
        {
            var container = _containers.EnsureContainer<ContainerSlot>(ent.Owner, AirSacContainerId);
            placed = _containers.Insert(sac, container);
        }

        if (!placed)
        {
            QueueDel(sac);
            ent.Comp.AirSacEntity = null;
            _popup.PopupEntity(Loc.GetString("gene-organ-adapt-sac-fail"), ent.Owner, ent.Owner, PopupType.SmallCaution);
        }
        else
        {
            TryConnectAirSac(ent.Owner, sac);
            _popup.PopupEntity(Loc.GetString("gene-organ-adapt-activate"), ent.Owner, ent.Owner);
            ent.Comp.NextInternalsAttempt = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        }

        Dirty(ent);
    }

    private void OnShutdown(Entity<GeneticOrganAdaptComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.AddedPressureImmunity)
            RemComp<PressureImmunityComponent>(ent.Owner);

        if (ent.Comp.OriginalBloodMaxVolume is { } original &&
            TryComp<BloodstreamComponent>(ent.Owner, out var blood))
        {
            blood.BloodMaxVolume = original;
            if (_solutions.ResolveSolution(ent.Owner, blood.BloodSolutionName, ref blood.BloodSolution))
                _solutions.SetCapacity(blood.BloodSolution.Value, original);
        }

        if (ent.Comp.AirSacEntity is { } sac && Exists(sac))
        {
            if (TryComp<InternalsComponent>(ent.Owner, out var internals))
                _internals.DisconnectBreathTool((ent.Owner, internals), sac, forced: true);

            _containers.TryRemoveFromContainer(sac, force: true);
            QueueDel(sac);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<GeneticOrganAdaptComponent>();
        while (query.MoveNext(out var uid, out var adapt))
        {
            if (adapt.ConnectedInternals || adapt.AirSacEntity is not { } sac || !Exists(sac))
                continue;

            if (adapt.NextInternalsAttempt > _timing.CurTime)
                continue;

            adapt.NextInternalsAttempt = _timing.CurTime + TimeSpan.FromSeconds(2);
            if (TryConnectAirSac(uid, sac))
            {
                adapt.ConnectedInternals = true;
                Dirty(uid, adapt);
            }
        }
    }

    private bool TryConnectAirSac(EntityUid owner, EntityUid sac)
    {
        if (!TryComp<InternalsComponent>(owner, out var internals) ||
            !TryComp<GasTankComponent>(sac, out var tank))
            return false;

        if (TryComp<BreathToolComponent>(sac, out var breath) && breath.ConnectedInternalsEntity != owner)
            _internals.ConnectBreathTool((owner, internals), sac);

        return _gasTank.ConnectToInternals((sac, tank), user: owner);
    }
}

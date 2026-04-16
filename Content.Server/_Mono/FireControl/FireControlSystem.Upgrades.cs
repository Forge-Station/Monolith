using System.Collections.Generic;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Stack;
using Content.Server._Mono.Detection;
using Content.Shared.Construction.Components;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Content.Shared.Wires;

namespace Content.Server._Mono.FireControl;

// Forge-Change-full
public sealed partial class FireControlSystem
{
    [Dependency] private readonly ConstructionSystem _construction = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly StackSystem _stack = default!;

    private void InitializeUpgrades()
    {
        SubscribeLocalEvent<GunneryServerUpgradeComponent, MapInitEvent>(OnUpgradeMapInit);
        SubscribeLocalEvent<GunneryServerUpgradeComponent, AfterInteractUsingEvent>(OnUpgradeAfterInteractUsing);
        SubscribeLocalEvent<GunneryServerUpgradeConstructionComponent, ComponentInit>(OnUpgradeConstructionInit);
        SubscribeLocalEvent<GunneryServerUpgradeConstructionComponent, ExaminedEvent>(OnUpgradeConstructionExamined);
    }

    private void OnUpgradeMapInit(EntityUid uid, GunneryServerUpgradeComponent component, MapInitEvent args)
    {
        ApplyTierProfile(uid, component, refillBattery: true);
    }

    private void OnUpgradeAfterInteractUsing(EntityUid uid, GunneryServerUpgradeComponent component, AfterInteractUsingEvent args)
    {
        if (!args.CanReach || args.Handled)
            return;

        if (TryComp<WiresPanelComponent>(uid, out var panel) && !panel.Open)
        {
            _popup.PopupEntity(Loc.GetString("gunnery-upgrade-panel-closed"), uid, args.User, PopupType.Medium);
            return;
        }

        if (TryComp<GunneryServerUpgradeConstructionComponent>(uid, out var construction))
        {
            if (TryInsertUpgradeConstructionPart(uid, construction, args.Used))
            {
                args.Handled = true;
                CompleteUpgradeIfReady(uid, component, construction, args.User);
            }
            return;
        }

        if (!TryComp<GunneryServerUpgradeBoardComponent>(args.Used, out var upgradeBoard))
            return;

        if (component.Tier == GunneryServerTier.Omega)
        {
            _popup.PopupEntity(Loc.GetString("gunnery-upgrade-already-max"), uid, args.User, PopupType.Medium);
            return;
        }

        if (upgradeBoard.SourceTier != component.Tier)
        {
            _popup.PopupEntity(
                Loc.GetString(
                    "gunnery-upgrade-invalid-board",
                    ("currentTier", Loc.GetString(GetTierLocId(component.Tier))),
                    ("requiredTier", Loc.GetString(GetTierLocId(upgradeBoard.SourceTier)))),
                uid,
                args.User,
                PopupType.Medium);
            return;
        }

        var upgrade = EnsureComp<GunneryServerUpgradeConstructionComponent>(uid);
        upgrade.TargetTier = upgradeBoard.TargetTier;
        upgrade.RequiredParts = new Dictionary<string, int>(upgradeBoard.RequiredParts);
        upgrade.InsertedParts.Clear();
        foreach (var (proto, _) in upgrade.RequiredParts)
        {
            upgrade.InsertedParts[proto] = 0;
        }

        QueueDel(args.Used);
        args.Handled = true;
        _popup.PopupEntity(
            Loc.GetString("gunnery-upgrade-construction-started", ("tier", Loc.GetString(GetTierLocId(upgrade.TargetTier)))),
            uid,
            args.User,
            PopupType.Medium);
    }

    private void OnUpgradeConstructionInit(EntityUid uid, GunneryServerUpgradeConstructionComponent component, ComponentInit args)
    {
        component.PartsContainer = _containers.EnsureContainer<Container>(uid, GunneryServerUpgradeConstructionComponent.PartsContainerId);
    }

    private void OnUpgradeConstructionExamined(EntityUid uid, GunneryServerUpgradeConstructionComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("gunnery-upgrade-construction-active", ("tier", Loc.GetString(GetTierLocId(component.TargetTier)))));
        foreach (var (prototype, required) in component.RequiredParts)
        {
            var inserted = component.InsertedParts.GetValueOrDefault(prototype, 0);
            args.PushMarkup(Loc.GetString("gunnery-upgrade-construction-part", ("part", prototype), ("inserted", inserted), ("required", required)));
        }
    }

    private bool TryInsertUpgradeConstructionPart(EntityUid serverUid, GunneryServerUpgradeConstructionComponent construction, EntityUid usedUid)
    {
        var proto = Prototype(usedUid)?.ID;
        if (proto == null || !construction.RequiredParts.TryGetValue(proto, out var required))
            return false;

        var inserted = construction.InsertedParts.GetValueOrDefault(proto, 0);
        if (inserted >= required)
            return false;

        if (TryComp<StackComponent>(usedUid, out var stack) && stack.Count > 1)
        {
            var split = _stack.Split(usedUid, 1, Transform(serverUid).Coordinates, stack);
            if (split == null)
                return false;

            if (!_containers.Insert(split.Value, construction.PartsContainer, force: true))
                return false;
        }
        else
        {
            if (!_containers.TryRemoveFromContainer(usedUid, false, out _))
                return false;
            if (!_containers.Insert(usedUid, construction.PartsContainer, force: true))
                return false;
        }

        construction.InsertedParts[proto] = inserted + 1;
        return true;
    }

    private void CompleteUpgradeIfReady(EntityUid uid, GunneryServerUpgradeComponent upgrade, GunneryServerUpgradeConstructionComponent construction, EntityUid user)
    {
        foreach (var (proto, required) in construction.RequiredParts)
        {
            if (construction.InsertedParts.GetValueOrDefault(proto, 0) < required)
            {
                _popup.PopupEntity(Loc.GetString("gunnery-upgrade-part-installed"), uid, user, PopupType.Medium);
                return;
            }
        }

        if (TryComp<MachineComponent>(uid, out var machine))
        {
            var replacedPartTypes = new HashSet<string>();
            foreach (var insertedPart in construction.PartsContainer.ContainedEntities)
            {
                if (TryComp<MachinePartComponent>(insertedPart, out var insertedMachinePart))
                    replacedPartTypes.Add(insertedMachinePart.PartType);
            }

            var existingParts = new List<EntityUid>(machine.PartContainer.ContainedEntities);
            foreach (var existingPart in existingParts)
            {
                if (!TryComp<MachinePartComponent>(existingPart, out var existingMachinePart))
                    continue;

                if (replacedPartTypes.Contains(existingMachinePart.PartType))
                    QueueDel(existingPart);
            }

            var upgradedParts = new List<EntityUid>(construction.PartsContainer.ContainedEntities);
            foreach (var upgradedPart in upgradedParts)
            {
                _containers.RemoveEntity(uid, upgradedPart);
                _containers.Insert(upgradedPart, machine.PartContainer, force: true);
            }

            _construction.RefreshParts(uid, machine);
        }

        upgrade.Tier = construction.TargetTier;
        ApplyTierProfile(uid, upgrade);
        RemComp<GunneryServerUpgradeConstructionComponent>(uid);

        _popup.PopupEntity(
            Loc.GetString("gunnery-upgrade-success", ("tier", Loc.GetString(GetTierLocId(upgrade.Tier)))),
            uid,
            user,
            PopupType.Medium);
    }

    private void ApplyTierProfile(EntityUid uid, GunneryServerUpgradeComponent component, bool refillBattery = false)
    {
        var profile = GetTierProfile(component.Tier);

        if (TryComp<FireControlServerComponent>(uid, out var server))
        {
            server.ProcessingPower = profile.ProcessingPower;
            server.MaxWeapons = profile.MaxWeapons;
        }

        if (TryComp<BatteryComponent>(uid, out var battery))
        {
            var chargeRatio = battery.MaxCharge <= 0f ? 1f : battery.CurrentCharge / battery.MaxCharge;
            _battery.SetMaxCharge(uid, profile.MaxCharge, battery);
            _battery.SetCharge(uid, refillBattery ? profile.MaxCharge : profile.MaxCharge * chargeRatio, battery);
        }

        if (TryComp<ApcPowerReceiverComponent>(uid, out var powerReceiver))
            powerReceiver.Load = profile.PowerLoad;

        if (TryComp<PassiveThermalSignatureComponent>(uid, out var passiveThermal))
            passiveThermal.Signature = profile.ThermalSignature;

        if (TryComp<ShipRepairableComponent>(uid, out var repairable))
        {
            repairable.RepairTime = profile.RepairTime;
            repairable.RepairCost = profile.RepairCost;
            Dirty(uid, repairable);
        }

        _metaData.SetEntityName(uid, Loc.GetString(GetTierLocId(component.Tier)));
    }

    private static GunneryServerTierProfile GetTierProfile(GunneryServerTier tier)
    {
        return tier switch
        {
            GunneryServerTier.Low => new GunneryServerTierProfile(24, 24, 125f, 250f, 5f, 300f, 100000f, 10f, 25),
            GunneryServerTier.Medium => new GunneryServerTierProfile(42, 42, 250f, 500f, 10f, 600f, 250000f, 13f, 40),
            GunneryServerTier.High => new GunneryServerTierProfile(60, 50, 500f, 1000f, 20f, 1200f, 1000000f, 20f, 70),
            GunneryServerTier.Ultra => new GunneryServerTierProfile(90, 50, 1000f, 2000f, 50f, 2400f, 4000000f, 25f, 100),
            GunneryServerTier.Omega => new GunneryServerTierProfile(500, 50, 2000f, 4000f, 100f, 4800f, 8000000f, 40f, 150),
            _ => new GunneryServerTierProfile(24, 24, 125f, 250f, 5f, 300f, 100000f, 10f, 25),
        };
    }

    private static string GetTierLocId(GunneryServerTier tier)
    {
        return tier switch
        {
            GunneryServerTier.Low => "gunnery-server-tier-low",
            GunneryServerTier.Medium => "gunnery-server-tier-medium",
            GunneryServerTier.High => "gunnery-server-tier-high",
            GunneryServerTier.Ultra => "gunnery-server-tier-ultra",
            GunneryServerTier.Omega => "gunnery-server-tier-omega",
            _ => "gunnery-server-tier-low",
        };
    }

    private readonly record struct GunneryServerTierProfile(
        int ProcessingPower,
        int MaxWeapons,
        float MaxCharge,
        float PowerLoad,
        float IdleLoad,
        float BatteryRechargeRate,
        float ThermalSignature,
        float RepairTime,
        int RepairCost);
}

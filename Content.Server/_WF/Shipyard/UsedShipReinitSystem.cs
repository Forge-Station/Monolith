using Content.Server._Mono.ShipShields.Components;
using Content.Server._NF.Power.Components;
using Content.Server._WF.ShipPa;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Pinpointer;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Wires;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipPa;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Pinpointer;
using Content.Shared.VendingMachines;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Shipyard;

/// <summary>
/// Puts a used ship back on its feet after it has been loaded from YAML. The loader marks everything
/// it reads as map-initialised without ever raising MapInitEvent, so anything a system only sets up
/// on map init is simply missing: device net addresses, wire layouts, nav map data, gravity. Only
/// the setup that is safe to repeat is redone here - a blanket map init would refill every locker,
/// vending machine and light on the hull.
/// </summary>
public sealed class UsedShipReinitSystem : EntitySystem
{
    [Dependency] private IComponentFactory _compFactory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private NavMapSystem _navMap = default!;
    [Dependency] private PowerChargeSystem _powerCharge = default!;
    [Dependency] private ShipPaSystem _shipPa = default!;
    [Dependency] private WiresSystem _wires = default!;

    /// <summary>
    /// Walks a freshly loaded grid and redoes the map-init work its entities never got. Every step is
    /// idempotent and skips silently when the component or the system behind it is not there.
    /// </summary>
    public void ReinitLoadedShip(EntityUid grid)
    {
        if (TerminatingOrDeleted(grid))
            return;

        // The seller's shield bubble: the emitter's own link to it is not saved, so the leftover
        // bubble would wedge the emitter into never raising a new one.
        RemComp<ShipShieldedComponent>(grid);

        var pending = new Queue<EntityUid>();
        pending.Enqueue(grid);

        while (pending.TryDequeue(out var parent))
        {
            var children = Transform(parent).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (HasComp<ShipShieldComponent>(child))
                {
                    QueueDel(child);
                    continue;
                }

                pending.Enqueue(child);
                ReinitEntity(child);
            }
        }

        // Nav map chunks are rebuilt from the tiles, not saved, so the console has nothing to draw.
        RemComp<NavMapComponent>(grid);
        _navMap.EnsureGridNavMap(grid);
    }

    private void ReinitEntity(EntityUid uid)
    {
        if (TryComp<DeviceNetworkComponent>(uid, out var device))
            _deviceNetwork.ReinitLoadedDevice(uid, device);

        if (TryComp<WiresComponent>(uid, out var wires))
            _wires.ReinitLoadedWires(uid, wires);

        if (TryComp<ShipShieldEmitterComponent>(uid, out var emitter))
            ResetShieldEmitter(uid, emitter);

        if (TryComp<ShipPaSpeakerComponent>(uid, out var speaker))
            _shipPa.ReindexSpeaker((uid, speaker));

        if (TryComp<PowerChargeComponent>(uid, out var charge))
            _powerCharge.ReinitLoadedCharge(uid, charge);

        ResetUpgradeBaselines(uid);
        EnsureVendingCashSlot(uid);
    }

    /// <summary>
    /// Clears an emitter's dead link to the old bubble and undoes any ramping the hull lived through,
    /// which multiplies the draw fields in place and would otherwise compound on every resale.
    /// </summary>
    private void ResetShieldEmitter(EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        emitter.Shield = null;
        emitter.Shielded = null;

        if (MetaData(uid).EntityPrototype is { } proto
            && proto.TryGetComponent<ShipShieldEmitterComponent>(out var original, _compFactory))
        {
            emitter.BaseDraw = original.BaseDraw;
            emitter.MaxDraw = original.MaxDraw;
        }

        if (TryComp<ShipShieldRampingLoadComponent>(uid, out var ramping))
            ramping.NextUpdate = _timing.CurTime + ramping.MultiplicationInterval;
    }

    /// <summary>
    /// Recaptures the unmodified power figures machine upgrades scale from. They are not data fields,
    /// so a loaded machine starts at zero and the next parts refresh would zero its draw or supply.
    /// </summary>
    private void ResetUpgradeBaselines(EntityUid uid)
    {
        if (TryComp<UpgradePowerDrawComponent>(uid, out var draw))
        {
            if (TryComp<PowerConsumerComponent>(uid, out var consumer))
                draw.BaseLoad = consumer.DrawRate;
            else if (TryComp<ApcPowerReceiverComponent>(uid, out var receiver))
                draw.BaseLoad = receiver.Load;
        }

        if (TryComp<UpgradePowerSupplierComponent>(uid, out var supplier)
            && TryComp<PowerSupplierComponent>(uid, out var power))
        {
            supplier.BaseSupplyRate = power.MaxSupply;
        }

        if (TryComp<UpgradePowerSupplyRampingComponent>(uid, out var ramping)
            && TryComp<PowerNetworkBatteryComponent>(uid, out var battery))
        {
            ramping.BaseRampRate = battery.SupplyRampRate;
        }
    }

    /// <summary>
    /// Puts a vending machine's cash slot back. The slot dictionary is a read-only data field, so a
    /// slot added at map init is never written out and the machine comes back taking no money.
    /// </summary>
    private void EnsureVendingCashSlot(EntityUid uid)
    {
        if (!TryComp<VendingMachineComponent>(uid, out var vending)
            || vending.CashSlot is not { } slot
            || vending.CashSlotName is not { } slotName
            || _itemSlots.TryGetSlot(uid, slotName, out _))
        {
            return;
        }

        _itemSlots.AddItemSlot(uid, slotName, slot);
    }
}

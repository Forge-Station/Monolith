using Content.Server.PowerCell;
using Content.Shared._Forge.Atmos.Components;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.PowerCell.Components;

namespace Content.Server._Forge.Atmos.Systems;

/// <summary>
/// Runs the gas cloud siphon off its dedicated accumulator instead of the APC grid.
/// </summary>
public sealed partial class GasCloudSiphonSystem : EntitySystem
{
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private SharedPowerReceiverSystem _powerReceiver = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GasCloudSiphonComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GasCloudSiphonComponent, PowerCellChangedEvent>(OnCellChanged);
        SubscribeLocalEvent<GasCloudSiphonComponent, GasPressurePumpToggleStatusMessage>(OnToggle);
    }

    private void OnMapInit(Entity<GasCloudSiphonComponent> ent, ref MapInitEvent args)
    {
        _powerCell.SetDrawEnabled(ent.Owner, false);
        SyncReceiver(ent.Owner);
    }

    private void OnCellChanged(Entity<GasCloudSiphonComponent> ent, ref PowerCellChangedEvent args)
    {
        SyncReceiver(ent.Owner);
    }

    private void OnToggle(Entity<GasCloudSiphonComponent> ent, ref GasPressurePumpToggleStatusMessage args)
    {
        _powerCell.SetDrawEnabled(ent.Owner, args.Enabled);
        SyncReceiver(ent.Owner);
    }

    private void SyncReceiver(EntityUid uid)
    {
        // NeedsPower false = treat as powered without an APC while the cell has charge.
        _powerReceiver.SetNeedsPower(uid, !_powerCell.HasDrawCharge(uid));
    }
}

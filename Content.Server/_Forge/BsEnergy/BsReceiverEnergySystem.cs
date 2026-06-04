using Content.Server.Administration.Logs;
using Content.Server.Power.Components;
using Content.Server.Stack;
using Content.Shared._Forge.BsEnergy;
using Content.Shared.Cargo.Components;
using Content.Shared.Coordinates;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.BsEnergy;

public sealed class BsReceiverEnergySystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly BsTransmitterEnergySystem _bsTransmitterEnergySystem = default!;
    [Dependency] private readonly StackSystem _stackSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;

    private readonly Dictionary<NetEntity, UpdateTransmitterStateData> _cachedTransmittersData = new();
    private const float UpdateInterval = BsEnergySettings.UpdateInterval;
    private float _updateTimer;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BsReceiverEnergyComponent, AfterActivatableUIOpenEvent>(OnUIOpen);
        SubscribeLocalEvent<BsReceiverEnergyComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<BsReceiverEnergyComponent, InteractUsingEvent>(OnDeposit);
        SubscribeLocalEvent<BsReceiverEnergyComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
        SubscribeLocalEvent<BsReceiverEnergyComponent, WithdrawMessage>(OnWithdraw);
        SubscribeLocalEvent<BsReceiverEnergyComponent, ChoiceTransmitterMessage>(OnChoiceServer);
        SubscribeLocalEvent<BsReceiverEnergyComponent, ChangePowerMessage>(OnChangePower);
        SubscribeLocalEvent<BsReceiverEnergyComponent, EnableToggleMessage>(OnEnableToggle);
        SubscribeLocalEvent<BsReceiverEnergyComponent, ComponentInit>(OnInit);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BsReceiverEnergyComponent>();
        while (query.MoveNext(out var receiverUid, out var receiverEnergyComponent))
        {
            if (receiverEnergyComponent.OldEnableState != receiverEnergyComponent.Enabled)
            {
                receiverEnergyComponent.OldEnableState = receiverEnergyComponent.Enabled;
                UpdateAppearance(receiverUid, receiverEnergyComponent.Enabled);
            }

            UpdateUI(receiverUid, receiverEnergyComponent);
        }

        _updateTimer += frameTime;

        if (_updateTimer < UpdateInterval)
            return;

        _updateTimer = 0;
    }

    private void OnAnchorStateChanged(EntityUid receiverUid, BsReceiverEnergyComponent receiverEnergyComponent, AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            ReceiverOff(receiverUid, receiverEnergyComponent);
    }

    private void OnInit(EntityUid receiverUid, BsReceiverEnergyComponent receiverEnergyComponent, ComponentInit args)
    {
        UpdateAppearance(receiverUid, receiverEnergyComponent.Enabled);
    }

    private void OnDeposit(EntityUid receiverUid, BsReceiverEnergyComponent receiverEnergyComponent, InteractUsingEvent args)
    {

        if (args.Handled ||
            args.User is not { Valid: true } player ||
            !TryComp<CashComponent>(args.Used, out _) ||
            !TryComp<StackComponent>(args.Used, out var stackComponent))
            return;

        _adminLogger.Add(LogType.ATMUsage, LogImpact.Low, $"{ToPrettyString(player):actor} deposited {stackComponent.Count} into {ToPrettyString(receiverEnergyComponent.Owner)}");
        _popup.PopupEntity(Loc.GetString("popup-bs-energy-deposit-successful"), receiverUid);
        receiverEnergyComponent.Money += stackComponent.Count;
        args.Handled = true;

        QueueDel(args.Used);
    }

    private void UpdateAppearance(EntityUid receiverUid, bool enabled)
    {
        _appearance.SetData(receiverUid, BsEnergyVisuals.Enabled, enabled);
        if (TryComp<PointLightComponent>(receiverUid, out var light))
            _lights.SetEnabled(receiverUid, enabled, light);
    }

    private void OnComponentShutdown(EntityUid receiverUid, BsReceiverEnergyComponent receiverEnergyComponent, ComponentShutdown args)
    {
        if (!receiverEnergyComponent.ConnectedTransmitter.IsValid() || !TryComp<BsTransmitterEnergyComponent>(receiverEnergyComponent.ConnectedTransmitter, out var bsTransmitterEnergyComponent))
            return;

        _bsTransmitterEnergySystem.ReceiverDisconnect(receiverUid, bsTransmitterEnergyComponent);
    }

    private bool CheckConnected(EntityUid transmitterUid,  BsTransmitterEnergyComponent bsTransmitterEnergyComponent)
    {
        return bsTransmitterEnergyComponent.Receivers.Contains(transmitterUid);
    }

    private void OnUIOpen(EntityUid receiverUid, BsReceiverEnergyComponent receiverEnergyComponent, AfterActivatableUIOpenEvent args)
    {
        UpdateUI(receiverUid, receiverEnergyComponent);
    }

    private void OnEnableToggle(EntityUid receiverUid, BsReceiverEnergyComponent receiverEnergyComponent, EnableToggleMessage args)
    {
        if (args.Enabled && Transform(receiverUid).Anchored)
        {
            receiverEnergyComponent.Enabled = true;
            UpdateAppearance(receiverUid, true);
        }
        else
            ReceiverOff(receiverUid, receiverEnergyComponent);
    }

    private void ReceiverOff(EntityUid receiverUid, BsReceiverEnergyComponent receiverEnergyComponent)
    {
        receiverEnergyComponent.Enabled = false;
        UpdateAppearance(receiverUid, false);

        receiverEnergyComponent.ReceivedPower = 0;

        if (TryComp<BsTransmitterEnergyComponent>(receiverEnergyComponent.ConnectedTransmitter, out var bsTransmitterEnergyComponent))
            _bsTransmitterEnergySystem.ReceiverDisconnect(receiverUid, bsTransmitterEnergyComponent);
    }

    private (float, float)? GetNetworkData(EntityUid receiverUid)
    {
        if (!TryComp<PowerSupplierComponent>(receiverUid, out var powerSupplierComponent))
            return null;

        (float, float)? networkStats = null;
        if (powerSupplierComponent.Net is { IsConnectedNetwork: true } net)
            networkStats = (net.NetworkNode.LastCombinedLoad, net.NetworkNode.LastCombinedSupply);

        return networkStats;
    }

    private void UpdateTransmittersCache()
    {
        _cachedTransmittersData.Clear();

        var query = _entityManager.AllEntityQueryEnumerator<BsTransmitterEnergyComponent, TransformComponent>();
        while (query.MoveNext(out var transmitterUid, out var bsTransmitterEnergyComponent, out var transformComponent))
        {
            if (!bsTransmitterEnergyComponent.Enabled)
                continue;

            TryComp<MetaDataComponent>(transformComponent.GridUid, out var gridMetaData);

            var transmitterStateData = new UpdateTransmitterStateData
            {
                CurrentConnected = bsTransmitterEnergyComponent.Receivers.Count,
                MaxConnected = bsTransmitterEnergyComponent.MaxConnected,
                Price = bsTransmitterEnergyComponent.Price,
                TransmitterAvailablePower = bsTransmitterEnergyComponent.AvailablePower,
                GridTransmitterName = gridMetaData?.EntityName ?? string.Empty,
            };

            _cachedTransmittersData[GetNetEntity(transmitterUid)] = transmitterStateData;
        }
    }

    private void UpdateUI(EntityUid receiverUid, BsReceiverEnergyComponent receiverEnergyComponent)
    {
        if (!_uiSystem.IsUiOpen(receiverUid, BsEnergyUiKey.ReceiverKey))
            return;

        UpdateTransmittersCache();
        TryComp<BsTransmitterEnergyComponent>(receiverEnergyComponent.ConnectedTransmitter, out var transmitterEnergyComponent);

        var state = new BsReceiverInterfaceStateMessage
        {
            StepSize = receiverEnergyComponent.StepSize,
            MaxValue = receiverEnergyComponent.MaxValue,
            RequestedPower = receiverEnergyComponent.RequestedPower,
            NetworkStats = GetNetworkData(receiverUid),
            Enabled = receiverEnergyComponent.Enabled,
            Money = (int)receiverEnergyComponent.Money,
            ReceivedPower = receiverEnergyComponent.ReceivedPower,
            ConnectedTransmitter = transmitterEnergyComponent != null && CheckConnected(receiverUid, transmitterEnergyComponent) ? GetNetEntity(receiverEnergyComponent.ConnectedTransmitter) : NetEntity.Invalid,
            TransmittersData = _cachedTransmittersData,
        };

        _uiSystem.SetUiState(receiverUid, BsEnergyUiKey.ReceiverKey, state);
    }

    private void OnChangePower(EntityUid receiverUid, BsReceiverEnergyComponent receiverEnergyComponent, ChangePowerMessage args)
    {
        if (receiverEnergyComponent.RequestedPower == args.Power)
            return;

        var maxValue = receiverEnergyComponent.MaxValue;
        var power = Math.Max(0, args.Power);
        var steppedValue = (int)(Math.Round(power / (double)receiverEnergyComponent.StepSize) * receiverEnergyComponent.StepSize);
        power = steppedValue > maxValue ? maxValue : steppedValue;
        receiverEnergyComponent.RequestedPower = power;

        if (TryComp<BsTransmitterEnergyComponent>(receiverEnergyComponent.ConnectedTransmitter, out var bsTransmitterEnergyComponent))
            _bsTransmitterEnergySystem.UpdatePriorityClients(bsTransmitterEnergyComponent);

        _audio.PlayPvs(_audio.ResolveSound(receiverEnergyComponent.SoundClick), receiverUid);
    }

    private void OnChoiceServer(EntityUid receiverUid, BsReceiverEnergyComponent receiverEnergyComponent, ChoiceTransmitterMessage args)
    {
        if (!receiverEnergyComponent.Enabled)
            return;

        var selectedTransmitterUid = GetEntity(args.NetUid);
        if (!TryComp<BsTransmitterEnergyComponent>(selectedTransmitterUid, out var bsTransmitterEnergyComponent))
            return;

        if (CheckConnected(receiverUid, bsTransmitterEnergyComponent))
        {
            _bsTransmitterEnergySystem.ReceiverDisconnect(receiverUid, bsTransmitterEnergyComponent);
            return;
        }

        ClosePreviousConnection(receiverUid, receiverEnergyComponent.ConnectedTransmitter);
        receiverEnergyComponent.ConnectedTransmitter = _bsTransmitterEnergySystem.ReceiverConnect(receiverUid, bsTransmitterEnergyComponent) ? selectedTransmitterUid : EntityUid.Invalid;
    }

    private void ClosePreviousConnection(EntityUid receiverUid, EntityUid transmitterUid)
    {
        if (!TryComp<BsTransmitterEnergyComponent>(transmitterUid, out var bsTransmitterEnergyComponent))
            return;

        _bsTransmitterEnergySystem.ReceiverDisconnect(receiverUid, bsTransmitterEnergyComponent);
    }

    private void OnWithdraw(EntityUid receiverUid, BsReceiverEnergyComponent receiverEnergyComponent, WithdrawMessage args)
    {
        if (args.Actor is not { Valid : true } player || (int)receiverEnergyComponent.Money > 0)
            return;

        var stackPrototype = _prototypeManager.Index<StackPrototype>("Credit");
        _audio.PlayPvs(_audio.ResolveSound(receiverEnergyComponent.SoundOnWithdraw), receiverUid);
        _adminLogger.Add(LogType.ATMUsage, LogImpact.Low, $"{ToPrettyString(player):actor} withdrew {receiverEnergyComponent.Money} from {ToPrettyString(receiverEnergyComponent.Owner)}");
        _popup.PopupEntity(Loc.GetString("popup-bs-energy-withdraw-successful"), receiverUid);
        _stackSystem.Spawn((int)receiverEnergyComponent.Money, stackPrototype, receiverUid.ToCoordinates());
        receiverEnergyComponent.Money = 0;
    }
}


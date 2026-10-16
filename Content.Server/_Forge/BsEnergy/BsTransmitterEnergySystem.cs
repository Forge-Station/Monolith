using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Power.Components;
using Content.Server.Stack;
using Content.Shared._Forge.BsEnergy;
using Content.Shared.Coordinates;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.BsEnergy;

public sealed class BsTransmitterEnergySystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly StackSystem _stackSystem = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;

    private const int KvtConst = BsEnergySettings.KvtConst;
    private const int PassiveIncome = BsEnergySettings.PassiveIncome;
    private const float UpdateInterval = BsEnergySettings.UpdateInterval;
    private const float GeneratorLossFactor = BsEnergySettings.GeneratorLossFactor;
    private readonly Dictionary<EntityUid, Dictionary<NetEntity, UpdateReceiversData>> _cachedReceiversData = [];
    private float _updateTimer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BsTransmitterEnergyComponent, AfterActivatableUIOpenEvent>(OnUIOpen);
        SubscribeLocalEvent<BsTransmitterEnergyComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<BsTransmitterEnergyComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
        SubscribeLocalEvent<BsTransmitterEnergyComponent, ChangePowerMessage>(OnChangePower);
        SubscribeLocalEvent<BsTransmitterEnergyComponent, EnableToggleMessage>(OnEnableToggle);
        SubscribeLocalEvent<BsTransmitterEnergyComponent, WithdrawMessage>(OnWithdraw);
        SubscribeLocalEvent<BsTransmitterEnergyComponent, PriceMessage>(OnChangePrice);
        SubscribeLocalEvent<BsTransmitterEnergyComponent, ConnectingLimitMessage>(OnChangeConnectingLimit);
        SubscribeLocalEvent<BsTransmitterEnergyComponent, PasswordMessage>(OnChangePassword);
        SubscribeLocalEvent<BsTransmitterEnergyComponent, KickMessage>(OnKick);
        SubscribeLocalEvent<BsTransmitterEnergyComponent, ComponentInit>(OnInit);
    }

    public override void Update(float frameTime)
    {
        _updateTimer += frameTime;
        if (_updateTimer < UpdateInterval)
            return;

        _updateTimer = 0;
        UpdatePower();

        var queryServer = _entityManager.AllEntityQueryEnumerator<BsTransmitterEnergyComponent>();
        while (queryServer.MoveNext(out var transmitterUid, out var bsTransmitterEnergyComponent))
            UpdateUI(transmitterUid, bsTransmitterEnergyComponent);
    }

    public void UpdatePriorityClients(BsTransmitterEnergyComponent bsTransmitterEnergyComponent)
    {
        bsTransmitterEnergyComponent.Receivers = bsTransmitterEnergyComponent.Receivers.OrderByDescending(uid =>
            {
                if (TryComp<BsReceiverEnergyComponent>(uid, out var bsReceiverEnergyComponent))
                    return bsReceiverEnergyComponent.RequestedPower;
                return -1;
            })
            .ToList();
    }

    public bool ReceiverConnect(EntityUid receiverUid, EntityUid transmitterUid, string? password = null)
    {
        if (!receiverUid.IsValid() || !transmitterUid.IsValid() ||
            !TryComp<BsTransmitterEnergyComponent>(transmitterUid, out var bsTransmitterEnergyComponent) ||
            bsTransmitterEnergyComponent.Receivers.Count >= bsTransmitterEnergyComponent.MaxConnected ||
            bsTransmitterEnergyComponent.Receivers.Count >= bsTransmitterEnergyComponent.ConnectingLimit ||
            !bsTransmitterEnergyComponent.Enabled ||
            !string.IsNullOrWhiteSpace(bsTransmitterEnergyComponent.Password) && bsTransmitterEnergyComponent.Password != password ||
            !TryComp<BsReceiverEnergyComponent>(receiverUid, out var bsReceiverEnergyComponent) ||
            !bsReceiverEnergyComponent.Enabled)
            return false;

        if (bsTransmitterEnergyComponent.Receivers.Contains(receiverUid))
            return true;

        ReceiverDisconnect(receiverUid, bsReceiverEnergyComponent.ConnectedTransmitter);
        bsTransmitterEnergyComponent.Receivers.Add(receiverUid);
        bsReceiverEnergyComponent.ConnectedTransmitter = transmitterUid;

        if (!bsTransmitterEnergyComponent.HistoryData.TryGetValue(GetNetEntity(receiverUid), out var historyData))
        {
            historyData = bsTransmitterEnergyComponent.HistoryData[GetNetEntity(receiverUid)] = new();
            TryComp<MetaDataComponent>(Transform(receiverUid).GridUid, out var gridMetaData);
            historyData.GridReceiverName = gridMetaData?.EntityName ?? string.Empty;
            bsTransmitterEnergyComponent.HistoryData[GetNetEntity(receiverUid)] = historyData;
        }

        UpdatePriorityClients(bsTransmitterEnergyComponent);
        UpdateUI(transmitterUid, bsTransmitterEnergyComponent);
        return true;
    }

    private void UpdatePower()
    {
        var query = _entityManager.AllEntityQueryEnumerator<BsTransmitterEnergyComponent, PowerConsumerComponent>();
        while (query.MoveNext(out var transmitterUid, out var bsTransmitterEnergyComponent, out var powerConsumerComponent))
        {
            if (!bsTransmitterEnergyComponent.Enabled)
            {
                bsTransmitterEnergyComponent.AvailablePower = 0;
                bsTransmitterEnergyComponent.Income = 0;
                powerConsumerComponent.DrawRate = 0;
                DisconnectAllReceivers(bsTransmitterEnergyComponent);
                continue;
            }

            UpdatePriorityClients(bsTransmitterEnergyComponent);

            // Last tick's draw is the budget. Delivering `requested` watts costs requested / lossFactor,
            // so a full network draw of requested / lossFactor arrives as `requested` on the receiver.
            var remainingBudget = Math.Max(0f, bsTransmitterEnergyComponent.LastDrawnPower);
            var totalIncome = 0f;
            var totalRequested = 0;
            var totalGranted = 0;
            var toDisconnect = new List<EntityUid>();

            foreach (var receiverUid in bsTransmitterEnergyComponent.Receivers)
            {
                if (!receiverUid.IsValid() ||
                    !_entityManager.TryGetComponent<BsReceiverEnergyComponent>(receiverUid, out var bsReceiverEnergyComponent) ||
                    !_entityManager.TryGetComponent<PowerSupplierComponent>(receiverUid, out var powerSupplierComponent))
                {
                    toDisconnect.Add(receiverUid);
                    continue;
                }

                if (!bsReceiverEnergyComponent.Enabled)
                {
                    toDisconnect.Add(receiverUid);
                    continue;
                }

                totalRequested += bsReceiverEnergyComponent.RequestedPower;

                var (delivered, spent) = AllocateDelivery(bsReceiverEnergyComponent.RequestedPower, remainingBudget);
                var transferMoney = delivered / (float)KvtConst * (bsTransmitterEnergyComponent.Price / 60f) * UpdateInterval;

                if (delivered > 0 && bsReceiverEnergyComponent.Money < transferMoney)
                {
                    toDisconnect.Add(receiverUid);
                    powerSupplierComponent.MaxSupply = 0;
                    bsReceiverEnergyComponent.ReceivedPower = 0;
                    bsReceiverEnergyComponent.Enabled = false;
                    continue;
                }

                powerSupplierComponent.MaxSupply = delivered;
                bsReceiverEnergyComponent.ReceivedPower = delivered;
                remainingBudget = Math.Max(0f, remainingBudget - spent);

                if (delivered <= 0)
                    continue;

                bsReceiverEnergyComponent.Money -= transferMoney;
                bsTransmitterEnergyComponent.Money += transferMoney;
                totalIncome += transferMoney;
                totalGranted += delivered;

                TryComp<MetaDataComponent>(Transform(receiverUid).GridUid, out var gridMetaData);
                AddHistory(bsTransmitterEnergyComponent, receiverUid, gridMetaData?.EntityName ?? string.Empty, delivered, transferMoney);
            }

            foreach (var receiverUid in toDisconnect)
            {
                if (!receiverUid.IsValid())
                {
                    bsTransmitterEnergyComponent.Receivers.Remove(receiverUid);
                    continue;
                }

                ReceiverDisconnect(receiverUid, transmitterUid, updateUi: false);
            }

            bsTransmitterEnergyComponent.AvailablePower = GetAvailablePower(
                transmitterUid,
                powerConsumerComponent,
                bsTransmitterEnergyComponent.TargetPower,
                totalGranted);

            if (bsTransmitterEnergyComponent.EnablePassiveIncome && remainingBudget > 0f)
            {
                var passiveIncome = remainingBudget / KvtConst * (PassiveIncome / 60f) * UpdateInterval;
                bsTransmitterEnergyComponent.Money += passiveIncome;
                totalIncome += passiveIncome;
                AddHistory(
                    bsTransmitterEnergyComponent,
                    EntityUid.Invalid,
                    Loc.GetString("ui-bs-transmitter-automatic-energy-sales"),
                    (int)Math.Floor(remainingBudget),
                    passiveIncome);
            }

            bsTransmitterEnergyComponent.Income = totalIncome / UpdateInterval * 60f;
            var neededDraw = (float)Math.Ceiling(totalRequested / (double)GeneratorLossFactor);
            powerConsumerComponent.DrawRate = bsTransmitterEnergyComponent.EnablePassiveIncome
                ? bsTransmitterEnergyComponent.TargetPower
                : Math.Min(neededDraw, bsTransmitterEnergyComponent.TargetPower);
            bsTransmitterEnergyComponent.LastDrawnPower = powerConsumerComponent.ReceivedPower;
        }
    }

    private static (int Delivered, float Spent) AllocateDelivery(int requested, float remainingBudget)
    {
        if (requested <= 0 || remainingBudget <= 0f)
            return (0, 0f);

        var cost = requested / GeneratorLossFactor;
        if (remainingBudget >= cost)
            return (requested, cost);

        return ((int)Math.Floor(remainingBudget * GeneratorLossFactor), remainingBudget);
    }

    private void AddHistory(BsTransmitterEnergyComponent transmitter, EntityUid receiverUid, string gridName, int energy, float money)
    {
        var key = receiverUid.IsValid() ? GetNetEntity(receiverUid) : NetEntity.Invalid;
        if (!transmitter.HistoryData.TryGetValue(key, out var history))
            history = transmitter.HistoryData[key] = new();

        if (!string.IsNullOrEmpty(gridName))
            history.GridReceiverName = gridName;

        if (energy > 0)
            history.TotalEnergyReceived += (ulong)energy;

        history.TotalMoneyTransferred += money;
    }

    private void OnAnchorStateChanged(EntityUid transmitterUid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent, AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            TransmitterOff(transmitterUid, bsTransmitterEnergyComponent);
    }

    private void OnInit(EntityUid transmitterUid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent, ComponentInit args)
    {
        if (bsTransmitterEnergyComponent.ConnectingLimit < 0)
            bsTransmitterEnergyComponent.ConnectingLimit = bsTransmitterEnergyComponent.MaxConnected;
        else
            bsTransmitterEnergyComponent.ConnectingLimit = Math.Clamp(bsTransmitterEnergyComponent.ConnectingLimit, 0, bsTransmitterEnergyComponent.MaxConnected);

        UpdateAppearance(transmitterUid, bsTransmitterEnergyComponent.Enabled);
    }

    private void OnChangePrice(EntityUid transmitterUid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent, PriceMessage args)
    {
        bsTransmitterEnergyComponent.Price = Math.Max(0, args.Price);
        DisconnectAllReceivers(bsTransmitterEnergyComponent);
        UpdateUI(transmitterUid, bsTransmitterEnergyComponent);
    }

    private void OnChangePassword(EntityUid uid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent, PasswordMessage args)
    {
        if (args.Password.Length > BsEnergySettings.PasswordMaxLenght)
            return;

        var password = string.IsNullOrWhiteSpace(args.Password) ? string.Empty : args.Password;
        if (password == bsTransmitterEnergyComponent.Password)
            return;

        bsTransmitterEnergyComponent.Password = password;
        DisconnectAllReceivers(bsTransmitterEnergyComponent);
        UpdateUI(uid, bsTransmitterEnergyComponent);
    }

    private void OnChangeConnectingLimit(EntityUid uid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent, ConnectingLimitMessage args)
    {
        if (args.LimitConnecting < 0 || args.LimitConnecting > bsTransmitterEnergyComponent.MaxConnected ||
            bsTransmitterEnergyComponent.ConnectingLimit == args.LimitConnecting)
            return;

        bsTransmitterEnergyComponent.ConnectingLimit = args.LimitConnecting;
        TrimReceiversToLimit(uid, bsTransmitterEnergyComponent);
        UpdateUI(uid, bsTransmitterEnergyComponent);
    }

    private void TrimReceiversToLimit(EntityUid transmitterUid, BsTransmitterEnergyComponent transmitter)
    {
        while (transmitter.Receivers.Count > transmitter.ConnectingLimit)
        {
            var count = transmitter.Receivers.Count;
            ReceiverDisconnect(transmitter.Receivers[^1], transmitterUid, updateUi: false);
            if (transmitter.Receivers.Count >= count)
                transmitter.Receivers.RemoveAt(transmitter.Receivers.Count - 1);
        }
    }

    private void OnKick(EntityUid transmitterUid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent, KickMessage args)
    {
        var receiverUid = GetEntity(args.KickedReceiver);
        if (!bsTransmitterEnergyComponent.Receivers.Contains(receiverUid))
            return;

        ReceiverDisconnect(receiverUid, transmitterUid, updateUi: false);

        if (TryComp<BsReceiverEnergyComponent>(receiverUid, out var bsReceiverEnergyComponent))
            bsReceiverEnergyComponent.Enabled = false;

        UpdateUI(transmitterUid, bsTransmitterEnergyComponent);
    }

    private void OnWithdraw(EntityUid transmitterUid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent, WithdrawMessage args)
    {
        if (args.Actor is not { Valid : true } player || (int)bsTransmitterEnergyComponent.Money <= 0)
            return;

        var stackPrototype = _prototypeManager.Index<StackPrototype>("Credit");
        _audio.PlayPvs(_audio.ResolveSound(bsTransmitterEnergyComponent.SoundOnWithdraw), transmitterUid);
        _adminLogger.Add(LogType.ATMUsage, LogImpact.Low, $"{ToPrettyString(player):actor} withdrew {bsTransmitterEnergyComponent.Money} from {ToPrettyString(bsTransmitterEnergyComponent.Owner)}");
        _popup.PopupEntity(Loc.GetString("popup-bs-energy-withdraw-successful"), transmitterUid);
        _stackSystem.Spawn((int)bsTransmitterEnergyComponent.Money, stackPrototype, transmitterUid.ToCoordinates());
        bsTransmitterEnergyComponent.Money = 0;
        UpdateUI(transmitterUid, bsTransmitterEnergyComponent);
    }

    private void UpdateAppearance(EntityUid transmitterUid, bool enabled)
    {
        _appearance.SetData(transmitterUid, BsEnergyVisuals.Enabled, enabled);
        if (TryComp<PointLightComponent>(transmitterUid, out var light))
            _lights.SetEnabled(transmitterUid, enabled, light);
    }

    private void OnComponentShutdown(EntityUid transmitterUid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent, ComponentShutdown args)
    {
        foreach (var receiverUid in bsTransmitterEnergyComponent.Receivers)
        {
            if (TryComp<PowerSupplierComponent>(receiverUid, out var powerSupplierComponent))
                powerSupplierComponent.MaxSupply = 0;

            if (!TryComp<BsReceiverEnergyComponent>(receiverUid, out var bsReceiverEnergyComponent))
                continue;

            bsReceiverEnergyComponent.ReceivedPower = 0;
            if (bsReceiverEnergyComponent.ConnectedTransmitter == transmitterUid)
                bsReceiverEnergyComponent.ConnectedTransmitter = EntityUid.Invalid;
        }
    }

    public void ReceiverDisconnect(EntityUid receiverUid, EntityUid transmitterUid, bool updateUi = true)
    {
        if (!receiverUid.IsValid())
            return;

        if (TryComp<BsTransmitterEnergyComponent>(transmitterUid, out var transmitter))
        {
            if (!transmitter.Receivers.Remove(receiverUid))
            {
                if (!TryComp<BsReceiverEnergyComponent>(receiverUid, out var linked) || linked.ConnectedTransmitter != transmitterUid)
                    return;
            }
        }
        else if (TryComp<BsReceiverEnergyComponent>(receiverUid, out var linked) &&
                 linked.ConnectedTransmitter != transmitterUid &&
                 transmitterUid.IsValid())
        {
            return;
        }

        if (TryComp<PowerSupplierComponent>(receiverUid, out var powerSupplierComponent))
            powerSupplierComponent.MaxSupply = 0;

        if (TryComp<BsReceiverEnergyComponent>(receiverUid, out var receiver))
        {
            receiver.ReceivedPower = 0;
            if (!transmitterUid.IsValid() || receiver.ConnectedTransmitter == transmitterUid)
                receiver.ConnectedTransmitter = EntityUid.Invalid;
        }

        if (updateUi && transmitter != null)
            UpdateUI(transmitterUid, transmitter);
    }

    private (float Load, float Supply, float MaxSupply)? GetNetworkStats(EntityUid transmitterUid)
    {
        if (!TryComp<PowerConsumerComponent>(transmitterUid, out var powerConsumerComponent))
            return null;

        if (powerConsumerComponent.Net is { IsConnectedNetwork: true } net)
            return (net.NetworkNode.LastCombinedLoad, net.NetworkNode.LastCombinedSupply, net.NetworkNode.LastCombinedMaxSupply);

        return null;
    }

    private (float, float)? GetNetworkData(EntityUid transmitterUid)
    {
        if (GetNetworkStats(transmitterUid) is not { } stats)
            return null;

        return (stats.Load, stats.Supply);
    }

    private int GetAvailablePower(EntityUid transmitterUid, PowerConsumerComponent powerConsumer, int targetPower, int totalGranted)
    {
        var spare = 0f;
        if (GetNetworkStats(transmitterUid) is { } stats)
            spare = Math.Max(0f, stats.MaxSupply - stats.Load) + powerConsumer.ReceivedPower;

        var deliverableCap = (int)Math.Min(
            MathF.Floor(spare * GeneratorLossFactor),
            MathF.Floor(targetPower * GeneratorLossFactor));

        return Math.Max(0, deliverableCap - totalGranted);
    }

    private void DisconnectAllReceivers(BsTransmitterEnergyComponent transmitterComp)
    {
        foreach (var receiverUid in transmitterComp.Receivers)
        {
            if (!TryComp<BsReceiverEnergyComponent>(receiverUid, out var bsReceiverEnergyComponent) ||
                !TryComp<PowerSupplierComponent>(receiverUid, out var powerSupplierComponent))
                continue;

            bsReceiverEnergyComponent.ConnectedTransmitter = EntityUid.Invalid;
            bsReceiverEnergyComponent.ReceivedPower = 0;
            powerSupplierComponent.MaxSupply = 0;
            bsReceiverEnergyComponent.Enabled = false;
        }

        transmitterComp.Receivers.Clear();
    }

    private void OnUIOpen(EntityUid transmitterUid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent, AfterActivatableUIOpenEvent args)
    {
        UpdateUI(transmitterUid, bsTransmitterEnergyComponent);
    }

    private void OnEnableToggle(EntityUid transmitterUid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent, EnableToggleMessage args)
    {
        if (args.Enabled && Transform(transmitterUid).Anchored)
        {
            bsTransmitterEnergyComponent.Enabled = true;
            UpdateAppearance(transmitterUid, true);

            if (_entityManager.TryGetComponent<PowerConsumerComponent>(transmitterUid, out var powerConsumerComponent))
                powerConsumerComponent.DrawRate = bsTransmitterEnergyComponent.TargetPower;
        }
        else
            TransmitterOff(transmitterUid, bsTransmitterEnergyComponent);

        UpdateUI(transmitterUid, bsTransmitterEnergyComponent);
    }

    private void TransmitterOff(EntityUid transmitterUid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent)
    {
        bsTransmitterEnergyComponent.Enabled = false;
        UpdateAppearance(transmitterUid, false);

        if (_entityManager.TryGetComponent<PowerConsumerComponent>(transmitterUid, out var powerConsumerComponent))
            powerConsumerComponent.DrawRate = 0;

        DisconnectAllReceivers(bsTransmitterEnergyComponent);
    }

    private void OnChangePower(EntityUid transmitterUid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent, ChangePowerMessage args)
    {
        if (bsTransmitterEnergyComponent.TargetPower == args.Power || !_entityManager.TryGetComponent<PowerConsumerComponent>(transmitterUid, out var powerConsumerComponent))
            return;

        var maxValue = bsTransmitterEnergyComponent.MaxValue;
        var power = Math.Max(0, args.Power);
        var steppedValue = (int)(Math.Round(power / (double)bsTransmitterEnergyComponent.StepSize) * bsTransmitterEnergyComponent.StepSize);
        power = steppedValue > maxValue ? maxValue : steppedValue;
        bsTransmitterEnergyComponent.TargetPower = power;

        if (bsTransmitterEnergyComponent.Enabled)
            powerConsumerComponent.DrawRate = power;

        _audio.PlayPvs(_audio.ResolveSound(bsTransmitterEnergyComponent.SoundClick), transmitterUid);
        UpdateUI(transmitterUid, bsTransmitterEnergyComponent);
    }

    private void UpdateReceiversCache(EntityUid transmitterUid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent)
    {
        var priority = 0;
        _cachedReceiversData.TryAdd(transmitterUid, new());
        _cachedReceiversData[transmitterUid].Clear();

        foreach (var receiverUid in bsTransmitterEnergyComponent.Receivers)
        {
            if (!TryComp<BsReceiverEnergyComponent>(receiverUid, out var bsReceiverEnergyComponent))
                continue;

            var transformComponent = Transform(receiverUid);
            TryComp<MetaDataComponent>(transformComponent.GridUid, out var gridMetaData);

            var receiverStateData = new UpdateReceiversData
            {
                GridReceiverName = gridMetaData?.EntityName ?? string.Empty,
                Priority = priority,
                RequestedPower = bsReceiverEnergyComponent.RequestedPower,
                ReceivedPower = bsReceiverEnergyComponent.ReceivedPower,
            };

            _cachedReceiversData[transmitterUid][GetNetEntity(receiverUid)] = receiverStateData;
            priority++;
        }

        if (!bsTransmitterEnergyComponent.EnablePassiveIncome)
            return;

        _cachedReceiversData[transmitterUid][NetEntity.Invalid] = new UpdateReceiversData
        {
            GridReceiverName = Loc.GetString("ui-bs-transmitter-automatic-energy-sales"),
            Priority = int.MaxValue,
            RequestedPower = float.PositiveInfinity,
            ReceivedPower = bsTransmitterEnergyComponent.AvailablePower,
        };
    }

    private void UpdateUI(EntityUid transmitterUid, BsTransmitterEnergyComponent bsTransmitterEnergyComponent)
    {
        if (!_uiSystem.IsUiOpen(transmitterUid, BsEnergyUiKey.TransmitterKey) ||
            !TryComp<PowerConsumerComponent>(transmitterUid, out var powerConsumerComponent))
            return;

        UpdateReceiversCache(transmitterUid, bsTransmitterEnergyComponent);

        var state = new BsTransmitterInterfaceStateMessage
        {
            StepSize = bsTransmitterEnergyComponent.StepSize,
            MaxConnected = bsTransmitterEnergyComponent.MaxConnected,
            ConnectingLimit = bsTransmitterEnergyComponent.ConnectingLimit,
            Income = bsTransmitterEnergyComponent.Income,
            MaxValue = bsTransmitterEnergyComponent.MaxValue,
            ConnectedCount = bsTransmitterEnergyComponent.Receivers.Count,
            TargetPower = bsTransmitterEnergyComponent.TargetPower,
            Price = bsTransmitterEnergyComponent.Price,
            Enabled = bsTransmitterEnergyComponent.Enabled,
            Money = (int)bsTransmitterEnergyComponent.Money,
            PowerConsumer = (int)powerConsumerComponent.ReceivedPower,
            AvailablePower = bsTransmitterEnergyComponent.AvailablePower,
            NetworkStats = GetNetworkData(transmitterUid),
            ReceiversData = new Dictionary<NetEntity, UpdateReceiversData>(_cachedReceiversData[transmitterUid]),
            HistoryData = CopyHistory(bsTransmitterEnergyComponent.HistoryData),
            Password = bsTransmitterEnergyComponent.Password,
        };

        _uiSystem.SetUiState(transmitterUid, BsEnergyUiKey.TransmitterKey, state);
    }

    private static Dictionary<NetEntity, UpdateHistoryData> CopyHistory(Dictionary<NetEntity, UpdateHistoryData> source)
    {
        var copy = new Dictionary<NetEntity, UpdateHistoryData>(source.Count);
        foreach (var (key, value) in source)
        {
            copy[key] = new UpdateHistoryData
            {
                GridReceiverName = value.GridReceiverName,
                TotalEnergyReceived = value.TotalEnergyReceived,
                TotalMoneyTransferred = value.TotalMoneyTransferred,
            };
        }

        return copy;
    }
}

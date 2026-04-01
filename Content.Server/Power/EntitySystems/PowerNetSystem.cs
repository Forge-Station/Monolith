using System; // Mono
using System.Diagnostics;
using System.Linq;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.NodeGroups;
using Content.Server.Power.Pow3r;
using Content.Shared.CCVar;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using JetBrains.Annotations;
using Prometheus; // Forge-Change
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Threading;
using Robust.Shared.Utility;

namespace Content.Server.Power.EntitySystems
{
    /// <summary>
    ///     Manages power networks, power state, and all power components.
    /// </summary>
    [UsedImplicitly]
    public sealed class PowerNetSystem : SharedPowerNetSystem
    {
        // Forge-Change-start
        private static readonly Gauge PowerNetworkCountGauge = Metrics.CreateGauge(
            "power_network_count",
            "Number of pow3r networks.");

        private static readonly Gauge PowerLoadCountGauge = Metrics.CreateGauge(
            "power_load_count",
            "Number of pow3r loads.");

        private static readonly Gauge PowerSupplyCountGauge = Metrics.CreateGauge(
            "power_supply_count",
            "Number of pow3r supplies.");

        private static readonly Gauge PowerBatteryCountGauge = Metrics.CreateGauge(
            "power_battery_count",
            "Number of pow3r batteries.");

        private static readonly Gauge PowerDirtyLoadsGauge = Metrics.CreateGauge(
            "power_dirty_loads_last_tick",
            "Number of pow3r loads that changed on the last solver tick.");

        private static readonly Gauge PowerDirtyBatteriesGauge = Metrics.CreateGauge(
            "power_dirty_batteries_last_tick",
            "Number of pow3r batteries that changed on the last solver tick.");

        private static readonly Gauge PowerReconnectQueueGauge = Metrics.CreateGauge(
            "power_reconnect_queue_size",
            "Number of queued power-net reconnects for the current tick.");

        private static readonly Gauge ApcReconnectQueueGauge = Metrics.CreateGauge(
            "power_apc_reconnect_queue_size",
            "Number of queued APC-net reconnects for the current tick.");

        private static readonly Gauge PowerDirtyNetworkGauge = Metrics.CreateGauge(
            "power_dirty_network_count",
            "Number of dirty power topology networks.");

        private static readonly Gauge PowerDirtyEdgeGauge = Metrics.CreateGauge(
            "power_dirty_edge_count",
            "Number of dirty power topology edges.");

        private static readonly Counter PowerIncrementalGroupRebuildCounter = Metrics.CreateCounter(
            "power_incremental_group_rebuild_total",
            "Number of incremental grouped-network rebuilds.");

        private static readonly Histogram PowerPhaseDurationMs = Metrics.CreateHistogram(
            "power_phase_duration_ms",
            "Execution time per power update phase (milliseconds).",
            new HistogramConfiguration
            {
                LabelNames = ["phase"],
                Buckets = Histogram.ExponentialBuckets(0.01, 2, 12),
            });
        // Forge-Change-end
        [Dependency] private readonly AppearanceSystem _appearance = default!;
        [Dependency] private readonly PowerNetConnectorSystem _powerNetConnector = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IParallelManager _parMan = default!;
        [Dependency] private readonly BatterySystem _battery = default!;

        private readonly PowerState _powerState = new();
        private readonly HashSet<PowerNet> _powerNetReconnectQueue = new();
        private readonly HashSet<ApcNet> _apcNetReconnectQueue = new();

        private EntityQuery<ApcPowerReceiverBatteryComponent> _apcBatteryQuery;
        private EntityQuery<BatteryComponent> _batteryQuery;
        // Forge-Change-start
        private EntityQuery<PowerNetworkBatteryComponent> _powerNetBatteryQuery;
        private readonly Dictionary<long, EntityUid> _apcReceiverLoads = new();
        private readonly Dictionary<long, EntityUid> _consumerLoads = new();
        private readonly Dictionary<long, EntityUid> _networkBatteries = new();
        private readonly HashSet<EntityUid> _dirtyApcReceivers = new();
        private readonly HashSet<EntityUid> _dirtyConsumers = new();
        private readonly HashSet<EntityUid> _dirtyNetworkBatteries = new();
        private readonly HashSet<EntityUid> _activeApcBatteryReceivers = new();
        private readonly List<EntityUid> _apcProcessBuffer = new();
        private readonly List<EntityUid> _processBuffer = new();
        private readonly Stopwatch _phaseStopwatch = new();
        private float _batterySupplyEpsilon = 0.01f;
        // Forge-Change-end
        private BatteryRampPegSolver _solver = new();

        // Mono
        private TimeSpan _updateInterval = TimeSpan.FromSeconds(0.5);
        private TimeSpan _updateAccumulator = TimeSpan.FromSeconds(0);

        public override void Initialize()
        {
            base.Initialize();

            _apcBatteryQuery = GetEntityQuery<ApcPowerReceiverBatteryComponent>();
            _batteryQuery = GetEntityQuery<BatteryComponent>();
            _powerNetBatteryQuery = GetEntityQuery<PowerNetworkBatteryComponent>(); // Forge-Change

            UpdatesAfter.Add(typeof(NodeGroupSystem));
            _solver = new(_cfg.GetCVar(CCVars.DebugPow3rDisableParallel));

            SubscribeLocalEvent<ApcPowerReceiverComponent, MapInitEvent>(ApcPowerReceiverMapInit); // Forge-Change
            SubscribeLocalEvent<ApcPowerReceiverComponent, ComponentInit>(ApcPowerReceiverInit);
            SubscribeLocalEvent<ApcPowerReceiverComponent, ComponentShutdown>(ApcPowerReceiverShutdown);
            SubscribeLocalEvent<ApcPowerReceiverComponent, ComponentRemove>(ApcPowerReceiverRemove);
            SubscribeLocalEvent<ApcPowerReceiverComponent, EntityPausedEvent>(ApcPowerReceiverPaused);
            SubscribeLocalEvent<ApcPowerReceiverComponent, EntityUnpausedEvent>(ApcPowerReceiverUnpaused);

            SubscribeLocalEvent<PowerNetworkBatteryComponent, ComponentInit>(BatteryInit);
            SubscribeLocalEvent<PowerNetworkBatteryComponent, ComponentShutdown>(BatteryShutdown);
            SubscribeLocalEvent<PowerNetworkBatteryComponent, EntityPausedEvent>(BatteryPaused);
            SubscribeLocalEvent<PowerNetworkBatteryComponent, EntityUnpausedEvent>(BatteryUnpaused);

            SubscribeLocalEvent<PowerConsumerComponent, ComponentInit>(PowerConsumerInit);
            SubscribeLocalEvent<PowerConsumerComponent, ComponentShutdown>(PowerConsumerShutdown);
            SubscribeLocalEvent<PowerConsumerComponent, EntityPausedEvent>(PowerConsumerPaused);
            SubscribeLocalEvent<PowerConsumerComponent, EntityUnpausedEvent>(PowerConsumerUnpaused);

            SubscribeLocalEvent<PowerSupplierComponent, ComponentInit>(PowerSupplierInit);
            SubscribeLocalEvent<PowerSupplierComponent, ComponentShutdown>(PowerSupplierShutdown);
            SubscribeLocalEvent<PowerSupplierComponent, EntityPausedEvent>(PowerSupplierPaused);
            SubscribeLocalEvent<PowerSupplierComponent, EntityUnpausedEvent>(PowerSupplierUnpaused);

            Subs.CVar(_cfg, CCVars.DebugPow3rDisableParallel, DebugPow3rDisableParallelChanged);
            Subs.CVar(_cfg, CCVars.PowerBatterySupplyEventEpsilon, val => _batterySupplyEpsilon = MathF.Max(0f, val));
        }

        private void DebugPow3rDisableParallelChanged(bool val)
        {
            _solver = new(val);
        }
        // Forge-Change-start
        private void ApcPowerReceiverMapInit(Entity<ApcPowerReceiverComponent> ent, ref MapInitEvent args)
        {
            _appearance.SetData(ent, PowerDeviceVisuals.Powered, ent.Comp.Powered);
        }

        private void ApcPowerReceiverInit(EntityUid uid, ApcPowerReceiverComponent component, ComponentInit args)
        {
            AllocLoad(uid, component.NetworkLoad, _apcReceiverLoads);
            QueueApcPowerReceiverUpdate(uid);
        }

        private void ApcPowerReceiverShutdown(EntityUid uid, ApcPowerReceiverComponent component,
            ComponentShutdown args)
        {
            FreeLoad(component.NetworkLoad, _apcReceiverLoads);
            _activeApcBatteryReceivers.Remove(uid);
        }
        // Forge-Change-end
        private void ApcPowerReceiverRemove(EntityUid uid, ApcPowerReceiverComponent component, ComponentRemove args)
        {
            component.Provider?.RemoveReceiver(component);
        }

        private static void ApcPowerReceiverPaused(
            EntityUid uid,
            ApcPowerReceiverComponent component,
            ref EntityPausedEvent args)
        {
            component.NetworkLoad.Paused = true;
        }

        private static void ApcPowerReceiverUnpaused(
            EntityUid uid,
            ApcPowerReceiverComponent component,
            ref EntityUnpausedEvent args)
        {
            component.NetworkLoad.Paused = false;
        }
        // Forge-Change-start
        private void BatteryInit(EntityUid uid, PowerNetworkBatteryComponent component, ComponentInit args)
        {
            AllocBattery(uid, component.NetworkBattery, _networkBatteries);
        }

        private void BatteryShutdown(EntityUid uid, PowerNetworkBatteryComponent component, ComponentShutdown args)
        {
            FreeBattery(component.NetworkBattery, _networkBatteries);
        }
        // Forge-Change-end
        private static void BatteryPaused(EntityUid uid, PowerNetworkBatteryComponent component, ref EntityPausedEvent args)
        {
            component.NetworkBattery.Paused = true;
        }

        private static void BatteryUnpaused(EntityUid uid, PowerNetworkBatteryComponent component, ref EntityUnpausedEvent args)
        {
            component.NetworkBattery.Paused = false;
        }
        // Forge-Change-start
        private void PowerConsumerInit(EntityUid uid, PowerConsumerComponent component, ComponentInit args)
        {
            _powerNetConnector.BaseNetConnectorInit(component);
            AllocLoad(uid, component.NetworkLoad, _consumerLoads);
            _dirtyConsumers.Add(uid);
        }

        private void PowerConsumerShutdown(EntityUid uid, PowerConsumerComponent component, ComponentShutdown args)
        {
            FreeLoad(component.NetworkLoad, _consumerLoads);
        }
        // Forge-Change-end
        private static void PowerConsumerPaused(EntityUid uid, PowerConsumerComponent component, ref EntityPausedEvent args)
        {
            component.NetworkLoad.Paused = true;
        }

        private static void PowerConsumerUnpaused(EntityUid uid, PowerConsumerComponent component, ref EntityUnpausedEvent args)
        {
            component.NetworkLoad.Paused = false;
        }

        private void PowerSupplierInit(EntityUid uid, PowerSupplierComponent component, ComponentInit args)
        {
            _powerNetConnector.BaseNetConnectorInit(component);
            AllocSupply(component.NetworkSupply);
        }

        private void PowerSupplierShutdown(EntityUid uid, PowerSupplierComponent component, ComponentShutdown args)
        {
            _powerState.Supplies.Free(component.NetworkSupply.Id);
        }

        private static void PowerSupplierPaused(EntityUid uid, PowerSupplierComponent component, ref EntityPausedEvent args)
        {
            component.NetworkSupply.Paused = true;
        }

        private static void PowerSupplierUnpaused(EntityUid uid, PowerSupplierComponent component, ref EntityUnpausedEvent args)
        {
            component.NetworkSupply.Paused = false;
        }

        public void InitPowerNet(PowerNet powerNet)
        {
            AllocNetwork(powerNet.NetworkNode);
            MarkTopologyDirty(powerNet.NetworkNode.Id, requireFullRebuild: true);
        }

        public void DestroyPowerNet(PowerNet powerNet)
        {
            MarkTopologyDirty(powerNet.NetworkNode.Id, requireFullRebuild: true);
            RemoveTopologyEdges(powerNet.NetworkNode.Id);
            _powerState.Networks.Free(powerNet.NetworkNode.Id);
        }

        public void QueueReconnectPowerNet(PowerNet powerNet)
        {
            _powerNetReconnectQueue.Add(powerNet);
            MarkTopologyDirty(powerNet.NetworkNode.Id);
        }

        public void InitApcNet(ApcNet apcNet)
        {
            AllocNetwork(apcNet.NetworkNode);
            MarkTopologyDirty(apcNet.NetworkNode.Id, requireFullRebuild: true);
        }

        public void DestroyApcNet(ApcNet apcNet)
        {
            MarkTopologyDirty(apcNet.NetworkNode.Id, requireFullRebuild: true);
            RemoveTopologyEdges(apcNet.NetworkNode.Id);
            _powerState.Networks.Free(apcNet.NetworkNode.Id);
        }

        public void QueueReconnectApcNet(ApcNet apcNet)
        {
            _apcNetReconnectQueue.Add(apcNet);
            MarkTopologyDirty(apcNet.NetworkNode.Id);
        }

        public PowerStatistics GetStatistics()
        {
            return new()
            {
                CountBatteries = _powerState.Batteries.Count,
                CountLoads = _powerState.Loads.Count,
                CountNetworks = _powerState.Networks.Count,
                CountSupplies = _powerState.Supplies.Count
            };
        }

        public NetworkPowerStatistics GetNetworkStatistics(PowerState.Network network)
        {
            // Right, consumption. Now this is a big mess.
            // Start by summing up consumer draw rates.
            // Then deal with batteries.
            // While for consumers we want to use their max draw rates,
            //  for batteries we ought to use their current draw rates,
            //  because there's all sorts of weirdness with them.
            // A full battery will still have the same max draw rate,
            //  but will likely have deliberately limited current draw rate.
            float consumptionW = network.Loads.Sum(s => _powerState.Loads[s].DesiredPower);
            consumptionW += network.BatteryLoads.Sum(s => _powerState.Batteries[s].CurrentReceiving);

            // This is interesting because LastMaxSupplySum seems to match LastAvailableSupplySum for some reason.
            // I suspect it's accounting for current supply rather than theoretical supply.
            float maxSupplyW = network.Supplies.Sum(s => _powerState.Supplies[s].MaxSupply);

            // Battery stuff is more complex.
            // Without stealing PowerState, the most efficient way
            //  to grab the necessary discharge data is from
            //  PowerNetworkBatteryComponent (has Pow3r reference).
            float supplyBatteriesW = 0.0f;
            float storageCurrentJ = 0.0f;
            float storageMaxJ = 0.0f;
            foreach (var discharger in network.BatterySupplies)
            {
                var nb = _powerState.Batteries[discharger];
                supplyBatteriesW += nb.CurrentSupply;
                storageCurrentJ += nb.CurrentStorage;
                storageMaxJ += nb.Capacity;
                maxSupplyW += nb.MaxSupply;
            }
            // And charging
            float outStorageCurrentJ = 0.0f;
            float outStorageMaxJ = 0.0f;
            foreach (var charger in network.BatteryLoads)
            {
                var nb = _powerState.Batteries[charger];
                outStorageCurrentJ += nb.CurrentStorage;
                outStorageMaxJ += nb.Capacity;
            }
            return new()
            {
                SupplyCurrent = network.LastCombinedMaxSupply,
                SupplyBatteries = supplyBatteriesW,
                SupplyTheoretical = maxSupplyW,
                Consumption = consumptionW,
                InStorageCurrent = storageCurrentJ,
                InStorageMax = storageMaxJ,
                OutStorageCurrent = outStorageCurrentJ,
                OutStorageMax = outStorageMaxJ
            };
        }
        // Forge-Change-start
        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            // Mono
            _updateAccumulator += TimeSpan.FromSeconds(frameTime);
            if (_updateAccumulator < _updateInterval)
                return;
            _updateAccumulator -= _updateInterval;

            var queuedPowerReconnects = _powerNetReconnectQueue.Count;
            var queuedApcReconnects = _apcNetReconnectQueue.Count;
            ObservePhase("reconnect", ReconnectNetworks);

            // Synchronize batteries
            ObservePhase("battery_pre_sync", () => RaiseLocalEvent(new NetworkBatteryPreSync()));

            // Mono - replace frameTime with update interval
            // Run power solver.
            ObservePhase("solver", () => _solver.Tick((float)_updateInterval.TotalSeconds, _powerState, _parMan));

            // Synchronize batteries, the other way around.
            ObservePhase("battery_post_sync", () => RaiseLocalEvent(new NetworkBatteryPostSync()));


            ObservePhase("collect_dirty", CollectDirtyFromSolver);

            PowerNetworkCountGauge.Set(_powerState.Networks.Count);
            PowerLoadCountGauge.Set(_powerState.Loads.Count);
            PowerSupplyCountGauge.Set(_powerState.Supplies.Count);
            PowerBatteryCountGauge.Set(_powerState.Batteries.Count);
            PowerDirtyLoadsGauge.Set(_solver.DirtyLoads.Count);
            PowerDirtyBatteriesGauge.Set(_solver.DirtyBatteries.Count);
            PowerReconnectQueueGauge.Set(queuedPowerReconnects);
            ApcReconnectQueueGauge.Set(queuedApcReconnects);
            PowerDirtyNetworkGauge.Set(_powerState.DirtyNetworks.Count);
            PowerDirtyEdgeGauge.Set(_powerState.DirtyEdges.Count);
            if (_solver.LastTopologyRebuildWasIncremental)
                PowerIncrementalGroupRebuildCounter.Inc();

            ObservePhase("update_apc_receivers", () => UpdateApcPowerReceivers((float) _updateInterval.TotalSeconds)); // Mono
            ObservePhase("update_consumers", UpdatePowerConsumers);
            ObservePhase("update_batteries", UpdateNetworkBatteries);
        }

        private void CollectDirtyFromSolver()
        {
            foreach (var loadId in _solver.DirtyLoads)
            {
                var combined = loadId.Combined;

                if (_apcReceiverLoads.TryGetValue(combined, out var apcUid))
                    _dirtyApcReceivers.Add(apcUid);

                if (_consumerLoads.TryGetValue(combined, out var consumerUid))
                    _dirtyConsumers.Add(consumerUid);
            }

            foreach (var batteryId in _solver.DirtyBatteries)
            {
                if (_networkBatteries.TryGetValue(batteryId.Combined, out var uid))
                    _dirtyNetworkBatteries.Add(uid);
            }
        }

        public void QueueApcPowerReceiverUpdate(EntityUid uid)
        {
            _dirtyApcReceivers.Add(uid);
        }

        private bool IsPoweredCalculate(ApcPowerReceiverComponent comp)
        {
            return !comp.PowerDisabled
                   && (!comp.NeedsPower
                       || MathHelper.CloseToPercent(comp.NetworkLoad.ReceivingPower,
                           comp.Load));
        }
        // Forge-Change-end
        private void ReconnectNetworks()
        {
            foreach (var apcNet in _apcNetReconnectQueue)
            {
                if (apcNet.Removed)
                    continue;

                DoReconnectApcNet(apcNet);
            }

            _apcNetReconnectQueue.Clear();

            foreach (var powerNet in _powerNetReconnectQueue)
            {
                if (powerNet.Removed)
                    continue;

                DoReconnectPowerNet(powerNet);
            }

            _powerNetReconnectQueue.Clear();

            ValidateTopologyCache();
        }

        // Forge-Change-start
        private void UpdateApcPowerReceivers(float frameTime)
        {
            _apcProcessBuffer.Clear();
            _apcProcessBuffer.AddRange(_dirtyApcReceivers);
            foreach (var uid in _activeApcBatteryReceivers)
            {
                if (_dirtyApcReceivers.Contains(uid))
                    continue;

                _apcProcessBuffer.Add(uid);
            }

            _dirtyApcReceivers.Clear();

            foreach (var uid in _apcProcessBuffer)
            {
                if (!TryComp(uid, out ApcPowerReceiverComponent? apcReceiver))
                {
                    _activeApcBatteryReceivers.Remove(uid);
                    continue;
                }

                var powered = IsPoweredCalculate(apcReceiver);

                MetaDataComponent? metadata = null;

                // TODO: If we get archetypes would be better to split this out.
                // Check if the entity has an internal battery
                var needsBatteryTick = false;
                if (_apcBatteryQuery.TryComp(uid, out var apcBattery) && _batteryQuery.TryComp(uid, out var battery))
                {
                    apcReceiver.Load = apcBattery.IdleLoad;

                    // Try to draw power from the battery if there isn't sufficient external power
                    var requireBattery = !powered && !apcReceiver.PowerDisabled;

                    if (requireBattery)
                    {
                        _battery.SetCharge(uid, battery.CurrentCharge - apcBattery.IdleLoad * frameTime, battery);
                    }
                    // Otherwise try to charge the battery
                    else if (powered && !_battery.IsFull(uid, battery))
                    {
                        apcReceiver.Load += apcBattery.BatteryRechargeRate * apcBattery.BatteryRechargeEfficiency;
                        _battery.SetCharge(uid, battery.CurrentCharge + apcBattery.BatteryRechargeRate * frameTime, battery);
                    }

                    // Enable / disable the battery if the state changed
                    var enableBattery = requireBattery && battery.CurrentCharge > 0;

                    if (apcBattery.Enabled != enableBattery)
                    {
                        apcBattery.Enabled = enableBattery;
                        metadata = MetaData(uid);
                        Dirty(uid, apcBattery, metadata);

                        var apcBatteryEv = new ApcPowerReceiverBatteryChangedEvent(enableBattery);
                        RaiseLocalEvent(uid, ref apcBatteryEv);

                        _appearance.SetData(uid, PowerDeviceVisuals.BatteryPowered, enableBattery);
                    }

                    powered |= enableBattery;

                    needsBatteryTick = (requireBattery && battery.CurrentCharge > 0) || (powered && !_battery.IsFull(uid, battery));
                }

                // If new value is the same as the old, then exit
                if (!apcReceiver.Recalculate && apcReceiver.Powered == powered)
                {
                    if (needsBatteryTick)
                        _activeApcBatteryReceivers.Add(uid);
                    else
                        _activeApcBatteryReceivers.Remove(uid);

                    continue;
                }

                metadata ??= MetaData(uid);
                if (Paused(uid, metadata))
                    continue;

                apcReceiver.Recalculate = false;
                apcReceiver.Powered = powered;
                Dirty(uid, apcReceiver, metadata);

                var ev = new PowerChangedEvent(powered, apcReceiver.NetworkLoad.ReceivingPower);
                RaiseLocalEvent(uid, ref ev);

                _appearance.SetData(uid, PowerDeviceVisuals.Powered, powered);

                if (needsBatteryTick)
                    _activeApcBatteryReceivers.Add(uid);
                else
                    _activeApcBatteryReceivers.Remove(uid);
            }
        }

        private void UpdatePowerConsumers()
        {
            _processBuffer.Clear();
            _processBuffer.AddRange(_dirtyConsumers);
            _dirtyConsumers.Clear();

            foreach (var uid in _processBuffer)
            {
                if (!TryComp(uid, out PowerConsumerComponent? consumer))
                    continue;

                var newRecv = consumer.NetworkLoad.ReceivingPower;
                ref var lastRecv = ref consumer.LastReceived;
                if (MathHelper.CloseToPercent(lastRecv, newRecv))
                    continue;

                lastRecv = newRecv;
                var msg = new PowerConsumerReceivedChanged(newRecv, consumer.DrawRate);
                RaiseLocalEvent(uid, ref msg);
            }
        }

        private void UpdateNetworkBatteries()
        {
            _processBuffer.Clear();
            _processBuffer.AddRange(_dirtyNetworkBatteries);
            _dirtyNetworkBatteries.Clear();

            foreach (var uid in _processBuffer)
            {
                if (!TryComp(uid, out PowerNetworkBatteryComponent? powerNetBattery))
                    continue;

                var lastSupply = powerNetBattery.LastSupply;
                var currentSupply = powerNetBattery.CurrentSupply;
                var hasSupply = currentSupply > _batterySupplyEpsilon;
                var hadSupply = lastSupply > _batterySupplyEpsilon;

                if (!hadSupply && hasSupply)
                {
                    var ev = new PowerNetBatterySupplyEvent(true);
                    RaiseLocalEvent(uid, ref ev);
                }
                else if (hadSupply && !hasSupply)
                {
                    var ev = new PowerNetBatterySupplyEvent(false);
                    RaiseLocalEvent(uid, ref ev);
                }

                powerNetBattery.LastSupply = currentSupply;
            }
        }

        private void AllocLoad(EntityUid uid, PowerState.Load load, Dictionary<long, EntityUid> map)
        {
            _powerState.Loads.Allocate(out load.Id) = load;
            map[load.Id.Combined] = uid;
        }

        private void AllocSupply(PowerState.Supply supply)
        {
            _powerState.Supplies.Allocate(out supply.Id) = supply;
        }

        private void AllocBattery(EntityUid uid, PowerState.Battery battery, Dictionary<long, EntityUid> map)
        {
            _powerState.Batteries.Allocate(out battery.Id) = battery;
            map[battery.Id.Combined] = uid;
        }

        private void AllocNetwork(PowerState.Network network)
        {
            _powerState.Networks.Allocate(out network.Id) = network;
        }

        private void FreeLoad(PowerState.Load load, Dictionary<long, EntityUid> map)
        {
            map.Remove(load.Id.Combined);
            _powerState.Loads.Free(load.Id);
        }

        private void FreeBattery(PowerState.Battery battery, Dictionary<long, EntityUid> map)
        {
            map.Remove(battery.Id.Combined);
            _powerState.Batteries.Free(battery.Id);
        }

        private void DoReconnectApcNet(ApcNet net)
        {
            var netNode = net.NetworkNode;
            RemoveTopologyEdges(netNode.Id);

            netNode.Loads.Clear();
            netNode.BatterySupplies.Clear();
            netNode.BatteryLoads.Clear();
            netNode.Supplies.Clear();

            foreach (var provider in net.Providers)
            {
                foreach (var receiver in provider.LinkedReceivers)
                {
                    netNode.Loads.Add(receiver.NetworkLoad.Id);
                    receiver.NetworkLoad.LinkedNetwork = netNode.Id;
                }
            }

            DoReconnectBasePowerNet(net, netNode);

            foreach (var apc in net.Apcs)
            {
                var netBattery = _powerNetBatteryQuery.GetComponent(apc.Owner);
                netNode.BatterySupplies.Add(netBattery.NetworkBattery.Id);
                netBattery.NetworkBattery.LinkedNetworkDischarging = netNode.Id;
            }

            RebuildTopologyEdges(netNode);
            MarkTopologyDirty(netNode.Id);
        }

        private void DoReconnectPowerNet(PowerNet net)
        {
            var netNode = net.NetworkNode;
            RemoveTopologyEdges(netNode.Id);

            netNode.Loads.Clear();
            netNode.Supplies.Clear();
            netNode.BatteryLoads.Clear();
            netNode.BatterySupplies.Clear();

            DoReconnectBasePowerNet(net, netNode);

            foreach (var charger in net.Chargers)
            {
                var battery = _powerNetBatteryQuery.GetComponent(charger.Owner);
                netNode.BatteryLoads.Add(battery.NetworkBattery.Id);
                battery.NetworkBattery.LinkedNetworkCharging = netNode.Id;
            }

            foreach (var discharger in net.Dischargers)
            {
                var battery = _powerNetBatteryQuery.GetComponent(discharger.Owner);
                netNode.BatterySupplies.Add(battery.NetworkBattery.Id);
                battery.NetworkBattery.LinkedNetworkDischarging = netNode.Id;
            }

            RebuildTopologyEdges(netNode);
            MarkTopologyDirty(netNode.Id);
        }

        private void DoReconnectBasePowerNet<TNetType>(BasePowerNet<TNetType> net, PowerState.Network netNode)
            where TNetType : IBasePowerNet
        {
            foreach (var consumer in net.Consumers)
            {
                netNode.Loads.Add(consumer.NetworkLoad.Id);
                consumer.NetworkLoad.LinkedNetwork = netNode.Id;
            }

            foreach (var supplier in net.Suppliers)
            {
                netNode.Supplies.Add(supplier.NetworkSupply.Id);
                supplier.NetworkSupply.LinkedNetwork = netNode.Id;
            }
        }

        /// <summary>
        /// Validate integrity of the power state data. Throws if an error is found.
        /// </summary>
        public void Validate()
        {
            _solver.Validate(_powerState);
        }

        private void ObservePhase(string phase, Action action)
        {
            _phaseStopwatch.Restart();
            action();
            PowerPhaseDurationMs.WithLabels(phase).Observe(_phaseStopwatch.Elapsed.TotalMilliseconds);
        }

        private void MarkTopologyDirty(PowerState.NodeId networkId, bool requireFullRebuild = false)
        {
            if (_powerState.Networks.Contains(networkId))
                _powerState.DirtyNetworks.Add(networkId);

            _powerState.TopologyVersion++;
            if (requireFullRebuild)
                _powerState.RequireFullTopologyRebuild = true;
        }

        private void RemoveTopologyEdges(PowerState.NodeId networkId)
        {
            if (_powerState.TopologyChildren.TryGetValue(networkId, out var children))
            {
                foreach (var child in children)
                {
                    if (_powerState.TopologyParents.TryGetValue(child, out var parents))
                        parents.Remove(networkId);
                    _powerState.DirtyEdges.Add(new(networkId, child));
                }

                children.Clear();
            }

            if (_powerState.TopologyParents.TryGetValue(networkId, out var parentSet))
            {
                foreach (var parent in parentSet)
                {
                    if (_powerState.TopologyChildren.TryGetValue(parent, out var siblings))
                        siblings.Remove(networkId);
                    _powerState.DirtyEdges.Add(new(parent, networkId));
                }

                parentSet.Clear();
            }
        }

        private void RebuildTopologyEdges(PowerState.Network netNode)
        {
            foreach (var batteryId in netNode.BatteryLoads)
            {
                ref var battery = ref _powerState.Batteries[batteryId];
                var source = battery.LinkedNetworkDischarging;
                if (source == default || !_powerState.Networks.Contains(source))
                    continue;

                if (!_powerState.TopologyChildren.TryGetValue(netNode.Id, out var children))
                    _powerState.TopologyChildren[netNode.Id] = children = new();
                if (!_powerState.TopologyParents.TryGetValue(source, out var parents))
                    _powerState.TopologyParents[source] = parents = new();

                children.Add(source);
                parents.Add(netNode.Id);
                _powerState.DirtyEdges.Add(new(netNode.Id, source));
            }
        }

        [Conditional("DEBUG")]
        private void ValidateTopologyCache()
        {
            foreach (var (parent, children) in _powerState.TopologyChildren)
            {
                DebugTools.Assert(_powerState.Networks.Contains(parent));
                foreach (var child in children)
                {
                    DebugTools.Assert(_powerState.Networks.Contains(child));
                    DebugTools.Assert(_powerState.TopologyParents.TryGetValue(child, out var parents) && parents.Contains(parent));
                }
            }

            foreach (var (child, parents) in _powerState.TopologyParents)
            {
                DebugTools.Assert(_powerState.Networks.Contains(child));
                foreach (var parent in parents)
                {
                    DebugTools.Assert(_powerState.Networks.Contains(parent));
                    DebugTools.Assert(_powerState.TopologyChildren.TryGetValue(parent, out var children) && children.Contains(child));
                }
            }
        }
        // Forge-Change-end
    }

    /// <summary>
    ///     Raised before power network simulation happens, to synchronize battery state from
    ///     components like <see cref="BatteryComponent"/> into <see cref="PowerNetworkBatteryComponent"/>.
    /// </summary>
    public readonly struct NetworkBatteryPreSync
    {
    }

    /// <summary>
    ///     Raised after power network simulation happens, to synchronize battery charge changes from
    ///     <see cref="PowerNetworkBatteryComponent"/> to components like <see cref="BatteryComponent"/>.
    /// </summary>
    public readonly struct NetworkBatteryPostSync
    {
    }

    /// <summary>
    ///     Raised when the amount of receiving power on a <see cref="PowerConsumerComponent"/> changes.
    /// </summary>
    [ByRefEvent]
    public readonly record struct PowerConsumerReceivedChanged(float ReceivedPower, float DrawRate)
    {
        public readonly float ReceivedPower = ReceivedPower;
        public readonly float DrawRate = DrawRate;
    }

    /// <summary>
    /// Raised whenever a <see cref="PowerNetworkBatteryComponent"/> changes from / to 0 CurrentSupply.
    /// </summary>
    [ByRefEvent]
    public readonly record struct PowerNetBatterySupplyEvent(bool Supply)
    {
        public readonly bool Supply = Supply;
    }

    public struct PowerStatistics
    {
        public int CountNetworks;
        public int CountLoads;
        public int CountSupplies;
        public int CountBatteries;
    }

    public struct NetworkPowerStatistics
    {
        public float SupplyCurrent;
        public float SupplyBatteries;
        public float SupplyTheoretical;
        public float Consumption;
        public float InStorageCurrent;
        public float InStorageMax;
        public float OutStorageCurrent;
        public float OutStorageMax;
    }

}

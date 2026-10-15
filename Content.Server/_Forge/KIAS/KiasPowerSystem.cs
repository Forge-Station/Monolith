using System.Linq;
using System.Text;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.NodeGroups;
using Content.Shared._Forge.KIAS;
using Content.Shared.NodeContainer;
using Content.Shared.Power;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Forge.KIAS;

public sealed class KiasPowerSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private PowerNetSystem _power = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private KiasSafetySystem _safety = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private KiasDisplaySystem _display = default!;
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _cables = new();
    private readonly Dictionary<EntityUid, bool[]> _deficits = new();
    private readonly HashSet<EntityUid> _scheduled = new();
    private readonly KiasPeriodicScheduler _samples = new(5);
    public bool HasDeficit(EntityUid grid) => _deficits.TryGetValue(grid, out var values) && values.Any(value => value);

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasPowerCableComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KiasPowerCableComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<KiasPowerCableComponent, EntParentChangedMessage>(OnParent);
        SubscribeLocalEvent<KiasPowerCableComponent, GridUidChangedEvent>(OnGrid);
        SubscribeLocalEvent<KiasPowerCableComponent, AnchorStateChangedEvent>(OnAnchor);
        SubscribeLocalEvent<KiasTopologyChangedEvent>(OnTopology);
        SubscribeLocalEvent<KiasAvailabilityChangedEvent>(OnAvailability);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
    }

    private void OnStartup(Entity<KiasPowerCableComponent> ent, ref ComponentStartup args) => Index(ent);
    private void OnShutdown(Entity<KiasPowerCableComponent> ent, ref ComponentShutdown args) => Unindex(ent);
    private void OnParent(Entity<KiasPowerCableComponent> ent, ref EntParentChangedMessage args) => Index(ent);
    private void OnGrid(Entity<KiasPowerCableComponent> ent, ref GridUidChangedEvent args) => Index(ent);
    private void OnAnchor(Entity<KiasPowerCableComponent> ent, ref AnchorStateChangedEvent args) => Index(ent);

    private void Unindex(Entity<KiasPowerCableComponent> ent)
    {
        if (ent.Comp.IndexedGrid is { } old && _cables.TryGetValue(old, out var cables))
        {
            cables.Remove(ent);
            if (cables.Count == 0)
                _cables.Remove(old);
        }
        ent.Comp.IndexedGrid = null;
    }

    private void Index(Entity<KiasPowerCableComponent> ent)
    {
        Unindex(ent);
        _metadata.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
        if (TerminatingOrDeleted(ent) || !Transform(ent).Anchored || Transform(ent).GridUid is not { } grid)
            return;
        if (!_cables.TryGetValue(grid, out var cables))
            _cables.Add(grid, cables = new());
        cables.Add(ent);
        ent.Comp.IndexedGrid = grid;
    }

    private void OnGridRemoved(GridRemovalEvent args)
    {
        _cables.Remove(args.EntityUid);
        _scheduled.Remove(args.EntityUid);
        _samples.Remove(args.EntityUid);
        _deficits.Remove(args.EntityUid);
    }

    private void OnAvailability(ref KiasAvailabilityChangedEvent args)
    {
        if (!args.Active)
        {
            _scheduled.Remove(args.Grid);
            _samples.Remove(args.Grid);
            _deficits.Remove(args.Grid);
        }
    }

    private void OnTopology(ref KiasTopologyChangedEvent args)
    {
        if (_kias.HasRole(args.Grid, KiasDeviceRole.Power))
        {
            _scheduled.Add(args.Grid);
            _samples.Add(args.Grid, _timing.CurTime);
        }
        else
        {
            _scheduled.Remove(args.Grid);
            _samples.Remove(args.Grid);
            _deficits.Remove(args.Grid);
        }
    }

    public string Describe(EntityUid grid)
    {
        if (!_kias.HasRole(grid, KiasDeviceRole.Power))
            return Loc.GetString("kias-power-unavailable");
        var supply = new float[3];
        var consumption = new float[3];
        var networkDeficits = new bool[3];
        var deficitSupply = new float[3];
        var deficitConsumption = new float[3];
        var seen = new HashSet<IBasePowerNet>();
        if (_cables.TryGetValue(grid, out var cables))
        foreach (var uid in cables)
        {
            if (TerminatingOrDeleted(uid) || !TryComp<CableComponent>(uid, out var cable)
                || cable.CableType == CableType.Data || !TryComp<NodeContainerComponent>(uid, out var nodes))
                continue;
            foreach (var node in nodes.Nodes.Values)
            {
                if (node.NodeGroup is not IBasePowerNet net || !seen.Add(net))
                    continue;
                var stats = _power.GetNetworkStatistics(net.NetworkNode);
                var channel = (int) cable.CableType;
                supply[channel] += stats.SupplyCurrent;
                consumption[channel] += stats.Consumption;
                if (stats.Consumption > stats.SupplyCurrent + 1)
                {
                    networkDeficits[channel] = true;
                    deficitSupply[channel] += stats.SupplyCurrent;
                    deficitConsumption[channel] += stats.Consumption;
                }
            }
        }
        if (!_deficits.TryGetValue(grid, out var deficits))
            _deficits.Add(grid, deficits = new bool[3]);
        var text = new StringBuilder();
        for (var channel = 0; channel < 3; channel++)
        {
            var deficit = networkDeficits[channel];
            var previous = deficits[channel];
            deficits[channel] = deficit;
            if (deficit && !previous)
            {
                var ev = new KiasPowerDeficitEvent(grid, (CableType) channel, deficitSupply[channel], deficitConsumption[channel]);
                RaiseLocalEvent(grid, ref ev, true);
                _safety.Publish(grid, Loc.GetString("kias-power-deficit", ("channel", Loc.GetString($"kias-relay-{((CableType) channel).ToString().ToLowerInvariant()}"))), true, announce: false, key: $"power:{channel}");
            }
            text.AppendLine(Loc.GetString("kias-power-statistics", ("channel", Loc.GetString($"kias-relay-{((CableType) channel).ToString().ToLowerInvariant()}")),
                ("supply", MathF.Round(supply[channel] / 1000, 1)), ("demand", MathF.Round(consumption[channel] / 1000, 1))));
        }
        return text.ToString();
    }

    public override void Update(float frameTime)
    {
        const int gridBudget = 4;
        for (var i = 0; i < gridBudget && _samples.TryDue(_timing.CurTime, out var grid); i++)
        {
            if (TryComp<KiasGridComponent>(grid, out var runtime))
            {
                var text = Describe(grid);
                if (runtime.PowerDetails == text)
                    continue;
                runtime.PowerDetails = text;
                _display.RefreshOpen(grid);
            }
        }
    }
}

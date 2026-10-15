using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared.Interaction;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.NodeContainer;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Map.Components;

namespace Content.Server._Forge.KIAS;

public sealed class KiasRelaySystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private NodeGroupSystem _nodes = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedToolSystem _tools = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    private static readonly CableType[] Channels = Enum.GetValues<CableType>();
    private readonly Dictionary<(EntityUid Grid, Vector2i Tile, CableType Channel), HashSet<EntityUid>> _open = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasRelayComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KiasRelayComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<KiasRelayComponent, AnchorStateChangedEvent>(OnAnchor);
        SubscribeLocalEvent<KiasRelayComponent, EntParentChangedMessage>(OnParent);
        SubscribeLocalEvent<KiasRelayComponent, GridUidChangedEvent>(OnGrid);
        SubscribeLocalEvent<KiasRelayComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<KiasRelayComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<KiasRelayComponent, InteractUsingEvent>(OnTool);
        SubscribeLocalEvent<KiasRelayComponent, ExaminedEvent>(OnExamine);
    }

    private void OnStartup(Entity<KiasRelayComponent> ent, ref ComponentStartup args) => Refresh(ent);
    private void OnShutdown(Entity<KiasRelayComponent> ent, ref ComponentShutdown args) => Unindex(ent);
    private void OnAnchor(Entity<KiasRelayComponent> ent, ref AnchorStateChangedEvent args) => Refresh(ent);
    private void OnParent(Entity<KiasRelayComponent> ent, ref EntParentChangedMessage args) => Refresh(ent);
    private void OnGrid(Entity<KiasRelayComponent> ent, ref GridUidChangedEvent args) => Refresh(ent);
    private void OnMove(Entity<KiasRelayComponent> ent, ref MoveEvent args)
    {
        if (!args.OnlyRotation)
            Refresh(ent);
    }

    public bool IsBlocked(EntityUid grid, Vector2i tile, CableType channel) => _open.ContainsKey((grid, tile, channel));

    public bool IsCableBlocked(EntityUid cable)
    {
        if (_open.Count == 0 || !TryComp<CableComponent>(cable, out var component)
            || Transform(cable).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var map))
            return false;
        return IsBlocked(grid, _map.TileIndicesFor(grid, map, Transform(cable).Coordinates), component.CableType);
    }

    private void Unindex(Entity<KiasRelayComponent> ent)
    {
        if (ent.Comp.Indexed is not { } old)
            return;
        ent.Comp.Indexed = null;
        if (_open.TryGetValue(old, out var relays))
        {
            relays.Remove(ent);
            if (relays.Count == 0)
                _open.Remove(old);
        }
        Invalidate(old.Grid, old.Tile);
    }

    public void Refresh(Entity<KiasRelayComponent> ent)
    {
        Unindex(ent);
        _metadata.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
        var xform = Transform(ent);
        if (TerminatingOrDeleted(ent) || ent.Comp.Closed || !xform.Anchored
            || xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var map))
            return;
        var key = (grid, _map.TileIndicesFor(grid, map, xform.Coordinates), ent.Comp.Channel);
        if (!_open.TryGetValue(key, out var relays))
            _open.Add(key, relays = new());
        relays.Add(ent);
        ent.Comp.Indexed = key;
        Invalidate(grid, key.Item2);
    }

    private void Invalidate(EntityUid grid, Vector2i tile)
    {
        if (!TryComp<MapGridComponent>(grid, out var map) || TerminatingOrDeleted(grid))
            return;
        foreach (var uid in _map.GetAnchoredEntities(grid, map, tile))
        {
            if (!TryComp<NodeContainerComponent>(uid, out var container))
                continue;
            foreach (var node in container.Nodes.Values)
                _nodes.QueueReflood(node);
        }
        _kias.Invalidate(grid);
    }

    private bool CanOperate(EntityUid uid) => !TerminatingOrDeleted(uid) && Transform(uid).Anchored
        && Transform(uid).GridUid is { } grid && _kias.ActiveGrids.Contains(grid) && _power.IsPowered(uid);

    public bool SetClosed(EntityUid uid, bool closed)
    {
        if (!CanOperate(uid) || !TryComp<KiasRelayComponent>(uid, out var relay))
            return false;
        if (relay.Closed == closed) return true;
        relay.Closed = closed;
        Refresh((uid, relay));
        return true;
    }

    public bool SetChannel(EntityUid uid, CableType channel, EntityUid? actor = null)
    {
        if (!CanOperate(uid) || !Enum.IsDefined(channel) || !TryComp<KiasRelayComponent>(uid, out var relay)
            || actor is { } user && !CanAdjust(uid, user))
            return false;
        if (relay.Channel == channel) return true;
        relay.Channel = channel;
        Refresh((uid, relay));
        if (actor is { } recipient) _popup.PopupEntity(Loc.GetString("kias-relay-channel-changed", ("channel", ChannelName(channel))), uid, recipient);
        return true;
    }

    private bool CanAdjust(EntityUid uid, EntityUid actor) => !TerminatingOrDeleted(uid) && !TerminatingOrDeleted(actor) && Transform(uid).GridUid is { } grid
        && _kias.CanConfigure(grid, actor) && _interaction.InRangeUnobstructed(actor, uid);

    private string ChannelName(CableType channel) => Loc.GetString($"kias-relay-channel-{channel.ToString().ToLowerInvariant()}");

    private void OnExamine(Entity<KiasRelayComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange) return;
        args.PushText(Loc.GetString("kias-relay-examine", ("channel", ChannelName(ent.Comp.Channel)),
            ("state", Loc.GetString(ent.Comp.Closed ? "kias-relay-closed" : "kias-relay-opened"))));
    }

    private void OnVerbs(Entity<KiasRelayComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !CanOperate(ent) || !CanAdjust(ent, args.User))
            return;
        var uid = ent.Owner;
        var actor = args.User;
        var closed = !ent.Comp.Closed;
        args.Verbs.Add(new AlternativeVerb { Text = Loc.GetString(closed ? "kias-relay-close" : "kias-relay-open"), Act = () => { if (CanAdjust(uid, actor)) SetClosed(uid, closed); } });
        foreach (var channel in Channels)
        {
            var selected = channel;
            args.Verbs.Add(new AlternativeVerb { Text = Loc.GetString($"kias-relay-{channel.ToString().ToLowerInvariant()}"), Act = () => SetChannel(uid, selected, actor) });
        }
    }

    private void OnTool(Entity<KiasRelayComponent> ent, ref InteractUsingEvent args)
    {
        if (!args.Handled && _tools.HasQuality(args.Used, "Screwing") && CanOperate(ent) && CanAdjust(ent, args.User))
        {
            args.Handled = SetChannel(ent, Channels[(Array.IndexOf(Channels, ent.Comp.Channel) + 1) % Channels.Length], args.User);
        }
    }
}

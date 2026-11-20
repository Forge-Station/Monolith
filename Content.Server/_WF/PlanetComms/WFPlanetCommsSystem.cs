using Content.Server.Power.Components;
using Content.Server.Radio;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetComms;

/// <summary>
/// Cuts a world off when none of its mass transceivers is powered and whole: headset radio on it goes dead and
/// announcements from off-world stop at its edge. The crew channel and handheld radios are exempt.
/// </summary>
public sealed partial class WFPlanetCommsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Channels that never need a transceiver: the private crew link and the handheld band.</summary>
    public static readonly HashSet<ProtoId<RadioChannelPrototype>> ExemptChannels = new() { "WFCrew", "Handheld" };

    /// <summary>The least time between two no-signal popups to one speaker.</summary>
    public static readonly TimeSpan NoSignalCooldown = TimeSpan.FromSeconds(5);

    private bool _enabled;
    private int _ungated;
    private bool _dirty = true;
    private GameTick _builtTick;
    private readonly HashSet<EntityUid> _upNetworks = new();
    private readonly Dictionary<EntityUid, TimeSpan> _nextNoSignal = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, PlanetCommsCVars.Enabled, value => _enabled = value, true);

        SubscribeLocalEvent<WFMassTransceiverComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFMassTransceiverComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WFMassTransceiverComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<WFMassTransceiverComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<WFMassTransceiverComponent, ExaminedEvent>(OnExamined);

        SubscribeLocalEvent<RadioSendAttemptEvent>(OnRadioSend);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnRadioReceive);
    }

    private void OnStartup(Entity<WFMassTransceiverComponent> ent, ref ComponentStartup args) => _dirty = true;

    private void OnShutdown(Entity<WFMassTransceiverComponent> ent, ref ComponentShutdown args) => _dirty = true;

    private void OnPowerChanged(Entity<WFMassTransceiverComponent> ent, ref PowerChangedEvent args) => _dirty = true;

    private void OnAnchorChanged(Entity<WFMassTransceiverComponent> ent, ref AnchorStateChangedEvent args) => _dirty = true;

    private void OnExamined(Entity<WFMassTransceiverComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        string key;
        if (ent.Comp.Broken)
            key = "wf-mass-transceiver-examine-broken";
        else if (NetworkOf(Transform(ent).MapUid) == null)
            key = "wf-mass-transceiver-examine-offworld";
        else if (Carries(ent))
            key = "wf-mass-transceiver-examine-up";
        else
            key = "wf-mass-transceiver-examine-down";

        args.PushMarkup(Loc.GetString(key));
    }

    /// <summary>Whether a transceiver is whole and powered.</summary>
    public bool Carries(Entity<WFMassTransceiverComponent> ent)
    {
        return !ent.Comp.Broken && TryComp<ApcPowerReceiverComponent>(ent, out var power) && power.Powered;
    }

    /// <summary>Whether the world an entity, or a map, sits on has comms; anything off a planet always does.</summary>
    public bool IsCommsUp(EntityUid uid)
    {
        if (!_enabled || TerminatingOrDeleted(uid))
            return true;

        return NetworkOf(Transform(uid).MapUid) is not { } network || IsNetworkUp(network);
    }

    /// <summary>Whether a session's attached entity has comms; a session with none always does.</summary>
    public bool IsCommsUp(ICommonSession session)
    {
        return session.AttachedEntity is not { } entity || IsCommsUp(entity);
    }

    /// <summary>
    /// Whether an announcement from <paramref name="source"/> reaches <paramref name="recipient"/>: anything on the
    /// same world is wired and always does, otherwise both ends need comms. A null source is off-world.
    /// </summary>
    public bool CanReach(EntityUid? source, EntityUid recipient)
    {
        if (!_enabled || _ungated > 0 || HasComp<GhostComponent>(recipient))
            return true;

        var to = NetworkOf(Transform(recipient).MapUid);
        var from = source is { } src && !TerminatingOrDeleted(src) ? NetworkOf(Transform(src).MapUid) : null;
        if (to != null && to == from)
            return true;

        return (to is not { } toNet || IsNetworkUp(toNet)) && (from is not { } fromNet || IsNetworkUp(fromNet));
    }

    /// <summary>The filter without the sessions an announcement from <paramref name="source"/> can't reach; the same filter if none drop.</summary>
    public Filter Reachable(Filter filter, EntityUid? source)
    {
        return TryNarrow(filter, source, out var narrowed) ? narrowed : filter;
    }

    /// <summary>Narrows a filter as <see cref="Reachable"/> does; false, with the filter untouched, when nobody drops.</summary>
    public bool TryNarrow(Filter filter, EntityUid? source, out Filter narrowed)
    {
        narrowed = filter;
        if (!_enabled || _ungated > 0)
            return false;

        List<ICommonSession>? cut = null;
        foreach (var session in filter.Recipients)
        {
            if (session.AttachedEntity is { } entity && !CanReach(source, entity))
                (cut ??= new List<ICommonSession>()).Add(session);
        }

        if (cut == null)
            return false;

        narrowed = filter.Clone().RemovePlayers(cut);
        return true;
    }

    /// <summary>Runs an out-of-character announcement that rides the announcement path with the gate open.</summary>
    public void Ungated(Action action)
    {
        _ungated++;
        try
        {
            action();
        }
        finally
        {
            _ungated--;
        }
    }

    /// <summary>Whether a radio channel needs a transceiver at all.</summary>
    public static bool Gates(RadioChannelPrototype channel) => !ExemptChannels.Contains(channel.ID);

    /// <summary>A handheld radio: a telecom-exempt item with its own mic or speaker. Intercoms are fixed, so not one.</summary>
    public bool IsHandheld(EntityUid radio)
    {
        return HasComp<TelecomExemptComponent>(radio) && HasComp<ItemComponent>(radio)
            && (HasComp<RadioMicrophoneComponent>(radio) || HasComp<RadioSpeakerComponent>(radio));
    }

    /// <summary>Whether this radio can put a message out on this channel from where it is.</summary>
    public bool CanTransmit(RadioChannelPrototype channel, EntityUid radio)
    {
        return !Gates(channel) || IsHandheld(radio) || IsCommsUp(radio);
    }

    /// <summary>Whether this radio can hear this channel where it is.</summary>
    public bool CanHear(RadioChannelPrototype channel, EntityUid radio)
    {
        return !Gates(channel) || IsHandheld(radio) || HasComp<GhostComponent>(radio) || IsCommsUp(radio);
    }

    private void OnRadioSend(ref RadioSendAttemptEvent args)
    {
        if (args.Cancelled || CanTransmit(args.Channel, args.RadioSource))
            return;

        args.Cancelled = true;
        NotifyNoSignal(args.RadioSource);
    }

    private void OnRadioReceive(ref RadioReceiveAttemptEvent args)
    {
        if (!args.Cancelled && !CanHear(args.Channel, args.RadioReceiver))
            args.Cancelled = true;
    }

    /// <summary>Tells whoever carries a radio that it has no signal, at most once per <see cref="NoSignalCooldown"/>.</summary>
    private void NotifyNoSignal(EntityUid radio)
    {
        var target = radio;
        for (var depth = 0; !HasComp<ActorComponent>(target); depth++)
        {
            var parent = Transform(target).ParentUid;
            if (depth >= 4 || !parent.IsValid() || HasComp<MapGridComponent>(parent) || HasComp<MapComponent>(parent))
                return;
            target = parent;
        }

        var now = _timing.CurTime;
        if (_nextNoSignal.TryGetValue(target, out var next) && now < next)
            return;

        if (_nextNoSignal.Count > 256)
            _nextNoSignal.Clear();
        _nextNoSignal[target] = now + NoSignalCooldown;
        _popup.PopupEntity(Loc.GetString("wf-planet-comms-no-signal"), target, target, PopupType.SmallCaution);
    }

    /// <summary>The planet network a map belongs to, or null for any map that is not a planet layer.</summary>
    private EntityUid? NetworkOf(EntityUid? map)
    {
        if (map is not { } mapUid || !TryComp<WFPlanetLayerComponent>(mapUid, out var layer) || layer.Network is not { } net)
            return null;

        return TryGetEntity(net, out var network) ? network : null;
    }

    private bool IsNetworkUp(EntityUid network)
    {
        // Rebuilt at most once a tick, and on any transceiver change, so a hull carrying one between worlds is caught.
        if (_dirty || _builtTick != _timing.CurTick)
        {
            _upNetworks.Clear();
            var query = EntityQueryEnumerator<WFMassTransceiverComponent, ApcPowerReceiverComponent, TransformComponent>();
            while (query.MoveNext(out _, out var transceiver, out var power, out var xform))
            {
                if (!transceiver.Broken && power.Powered && NetworkOf(xform.MapUid) is { } up)
                    _upNetworks.Add(up);
            }

            _dirty = false;
            _builtTick = _timing.CurTick;
        }

        return _upNetworks.Contains(network);
    }
}

using Content.Server._NF.Radio;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Chat.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Radio.EntitySystems;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Betrayal;
using Content.Shared.Doors.Components;
using Content.Shared.GameTicking;
using Content.Shared.Chat;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Betrayal;

/// <summary>A crewman who died this round, for the lies a breaking mind is told.</summary>
public readonly record struct WFDeadCrew(string Name, string? Line);

/// <summary>
/// Betrayal by the dead's own voices. Each player's last few lines of chat are kept, in memory only. A crewman who turns into a
/// Remade sometimes calls out one of them in the corridors, in their name, when crew are near and it is not fighting. A dead
/// crewman's headset, on the body or lying where it fell, now and then pleads over its channels in their name, garbled, from
/// where they died: a few times, then no more.
/// </summary>
public sealed partial class WFBetrayalVoiceSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public const int KeptLines = 5;
    public const int MinLine = 4;
    public const int MaxLine = 140;
    public const float CallRange = 15f;
    public const float CallMin = 60f;
    public const float CallMax = 180f;
    public const int LureMin = 3;
    public const int LureMax = 5;
    public const float LureGapMin = 40f;
    public const float LureGapMax = 110f;
    public const float LureCorruption = 0.07f;
    private const int Pleas = 6;
    private const int RememberedDead = 24;
    private const string EarsSlot = "ears";

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    private readonly List<WFDeadCrew> _dead = new();
    private readonly HashSet<Entity<DoorComponent>> _doors = new();
    private TimeSpan _next;

    /// <summary>Crew who have died this round, oldest first.</summary>
    public IReadOnlyList<WFDeadCrew> Dead => _dead;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<WFRemadeRisenEvent>(OnRisen);
        SubscribeLocalEvent<WFLureComponent, RadioTransformMessageEvent>(OnTransform);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _dead.Clear());
    }

    private void OnSpoke(EntitySpokeEvent ev)
    {
        if (!ev.IsWhisper && HasComp<ActorComponent>(ev.Source))
            Remember(ev.Source, ev.Message);
    }

    /// <summary>Keeps a line a player said, if it is worth calling out later.</summary>
    public void Remember(EntityUid uid, string message)
    {
        var line = message.Trim();
        if (line.Length < MinLine || line.Length > MaxLine || line[0] is '*' or '/' or '[' or '@')
            return;

        var comp = EnsureComp<WFLastWordsComponent>(uid);
        comp.Lines.Remove(line);
        comp.Lines.Add(line);
        if (comp.Lines.Count > KeptLines)
            comp.Lines.RemoveAt(0);
    }

    private void OnMobState(MobStateChangedEvent ev)
    {
        if (!HasComp<HumanoidAppearanceComponent>(ev.Target) || !HasComp<ActorComponent>(ev.Target) && !HasComp<WFLastWordsComponent>(ev.Target))
            return;

        if (!_inventory.TryGetSlotEntity(ev.Target, EarsSlot, out var ears) || !HasComp<HeadsetComponent>(ears))
            ears = null;

        if (ev.NewMobState != MobState.Dead)
        {
            if (ears is { } worn)
                RemComp<WFLureComponent>(worn);

            return;
        }

        var lines = CompOrNull<WFLastWordsComponent>(ev.Target)?.Lines;
        _dead.Add(new WFDeadCrew(Name(ev.Target), lines is { Count: > 0 } ? lines[^1] : null));
        if (_dead.Count > RememberedDead)
            _dead.RemoveAt(0);

        if (ears is not { } headset)
            return;

        var lure = EnsureComp<WFLureComponent>(headset);
        lure.Name = Name(ev.Target);
        lure.Area = AreaOf(ev.Target);
        lure.Remaining = _random.Next(LureMin, LureMax + 1);
        lure.Next = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(LureGapMin, LureGapMax));
    }

    /// <summary>Whether a body lies where the Monolith reaches: on a map with a stirring Monolith, or a grid with the Remade on it.</summary>
    public bool Haunted(EntityUid body)
    {
        var xform = Transform(body);
        var monoliths = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (monoliths.MoveNext(out _, out var monolith, out var stone))
        {
            if (monolith.Stage != WFMonolithStage.Quiet && stone.MapID == xform.MapID
                && (!monolith.Local || stone.GridUid == xform.GridUid))
                return true;
        }

        if (xform.GridUid is not { } grid)
            return false;

        var remade = EntityQueryEnumerator<WFRemadeComponent, TransformComponent>();
        while (remade.MoveNext(out _, out _, out var other))
        {
            if (other.GridUid == grid)
                return true;
        }

        return false;
    }

    private void OnTransform(Entity<WFLureComponent> ent, ref RadioTransformMessageEvent args)
    {
        args.Name = ent.Comp.Name;
    }

    private void OnRisen(ref WFRemadeRisenEvent ev)
    {
        if (!TryComp<WFLastWordsComponent>(ev.Corpse, out var words) || words.Lines.Count == 0)
            return;

        var caller = EnsureComp<WFCallerComponent>(ev.Remade);
        caller.Name = Name(ev.Corpse);
        caller.Lines = new List<string>(words.Lines);
        caller.NextCall = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(CallMin / 2f, CallMax / 2f));
    }

    /// <summary>Where a body lies, as the dying would say it: by the nearest named door, or else aboard the ship.</summary>
    public string AreaOf(EntityUid body)
    {
        var xform = Transform(body);
        _doors.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, 9f, _doors);
        Entity<DoorComponent>? best = null;
        var bestDistance = float.MaxValue;
        var here = _transform.GetWorldPosition(xform);
        foreach (var door in _doors)
        {
            var name = Name(door);
            if (name.Length == 0 || name is "airlock" or "door" or "glass airlock" or "shutter")
                continue;

            var distance = (_transform.GetWorldPosition(door) - here).LengthSquared();
            if (distance < bestDistance)
            {
                best = door;
                bestDistance = distance;
            }
        }

        if (best is { } named)
            return Loc.GetString("wf-betrayal-area-door", ("door", Name(named)));

        if (xform.GridUid is { } grid && !string.IsNullOrWhiteSpace(Name(grid)))
            return Loc.GetString("wf-betrayal-area-grid", ("ship", Name(grid)));

        return Loc.GetString("wf-betrayal-area-unknown");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Tick;
        UpdateCallers(now);
        UpdateLures(now);
    }

    private void UpdateCallers(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFCallerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var caller, out var xform))
        {
            if (now < caller.NextCall)
                continue;

            if (_mobState.IsDead(uid) || HasComp<WFRemadePlayingDeadComponent>(uid) || caller.Lines.Count == 0
                || InCombat(uid) || !CrewNear(uid, xform, CallRange))
            {
                continue;
            }

            Call((uid, caller));
            caller.NextCall = now + TimeSpan.FromSeconds(_random.NextFloat(CallMin, CallMax));
        }
    }

    /// <summary>The Remade calls out one of the crewman's last lines, as speech, in their name.</summary>
    public void Call(Entity<WFCallerComponent> ent)
    {
        var line = _random.Pick(ent.Comp.Lines);
        ent.Comp.LastLine = line;
        ent.Comp.Calls++;
        _chat.TrySendInGameICMessage(ent, line, InGameICChatType.Speak, ChatTransmitRange.Normal, hideLog: true,
            nameOverride: ent.Comp.Name, checkRadioPrefix: false, ignoreActionBlocker: true);
    }

    /// <summary>True if the Remade has something it is hunting down or is fighting.</summary>
    public bool InCombat(EntityUid uid)
    {
        return TryComp<HTNComponent>(uid, out var htn) && htn.Blackboard.TryGetValue<EntityUid>("Target", out _, EntityManager);
    }

    public bool CrewNear(EntityUid uid, TransformComponent xform, float range)
    {
        var here = _transform.GetMapCoordinates(uid, xform);
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out _, out var other))
        {
            if (other.MapID != here.MapId || HasComp<WFRemadeComponent>(player) || _mobState.IsDead(player))
                continue;

            if ((_transform.GetWorldPosition(other) - here.Position).LengthSquared() <= range * range)
                return true;
        }

        return false;
    }

    private bool AnyoneListening(MapId map)
    {
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out _, out var xform))
        {
            if (xform.MapID == map && !HasComp<WFRemadeComponent>(player) && !_mobState.IsDead(player))
                return true;
        }

        return false;
    }

    private void UpdateLures(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFLureComponent, EncryptionKeyHolderComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var lure, out var keys, out var xform))
        {
            if (lure.Remaining <= 0)
            {
                RemCompDeferred<WFLureComponent>(uid);
                continue;
            }

            if (now < lure.Next || !Unclaimed(uid) || !AnyoneListening(xform.MapID))
                continue;

            Plead((uid, lure), keys);
            lure.Next = now + TimeSpan.FromSeconds(_random.NextFloat(LureGapMin, LureGapMax));
        }
    }

    /// <summary>True unless a living person has the headset on or in hand: the dead and the Remade don't count.</summary>
    private bool Unclaimed(EntityUid headset)
    {
        var parent = Transform(headset).ParentUid;
        return !TryComp<MobStateComponent>(parent, out var state) || state.CurrentState == MobState.Dead || HasComp<WFRemadeComponent>(parent);
    }

    /// <summary>Sends one garbled plea on one of the headset's channels, in the dead crewman's name.</summary>
    public bool Plead(Entity<WFLureComponent> ent, EncryptionKeyHolderComponent keys)
    {
        var channels = new List<RadioChannelPrototype>();
        foreach (var id in keys.Channels)
        {
            if (_proto.TryIndex<RadioChannelPrototype>(id, out var channel))
                channels.Add(channel);
        }

        if (channels.Count == 0)
            return false;

        var channelPick = _random.Pick(channels);
        var text = Loc.GetString($"wf-betrayal-lure-{_random.Next(1, Pleas + 1)}", ("area", ent.Comp.Area));
        text = WFBetrayalMath.Corrupt(text, LureCorruption, () => _random.NextFloat());
        ent.Comp.Remaining--;
        ent.Comp.Sent++;
        ent.Comp.LastMessage = text;
        ent.Comp.LastChannel = channelPick.ID;
        _radio.SendRadioMessage(ent, text, channelPick, ent);
        return true;
    }
}

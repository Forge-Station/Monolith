using System.Linq;
using Content.Server.Radio;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Radio.Components;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    [Dependency] private SharedTransformSystem _crewTransform = default!;

    /// <summary>The shared input channel, routed to a distinct membership group for each crew.</summary>
    public const string CrewChannel = "WFCrew";
    private readonly HashSet<EntityUid> _crewRadios = new();
    private readonly HashSet<NetUserId> _signalRecipients = new();
    private TimeSpan _nextCrewSignal;

    private void InitializeCrewSignals()
    {
        SubscribeLocalEvent<RadioSendAttemptEvent>(OnCrewRadioSend);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnCrewRadioReceive);
    }

    /// <summary>Uses the same radio pipeline, with membership checked at both ends on every message.</summary>
    private void OnCrewRadioSend(ref RadioSendAttemptEvent args)
    {
        if (args.Channel.ID == CrewChannel && CrewForBody(args.RadioSource) == null)
            args.Cancelled = true;
    }

    private void OnCrewRadioReceive(ref RadioReceiveAttemptEvent args)
    {
        // Ghosts hear every crew, so staff can moderate the channel.
        if (args.Channel.ID == CrewChannel && !HasComp<GhostComponent>(args.RadioReceiver) &&
            (CrewForBody(args.RadioSource) is not { } crew || CrewForBody(args.RadioReceiver) != crew))
            args.Cancelled = true;
    }

    /// <summary>Names the logical crew channel without creating mutable radio prototypes.</summary>
    public string CrewRadioName(EntityUid source) => CrewForBody(source) is { } crew
        ? Loc.GetString("wf-round-setup-radio-channel", ("crew", CrewName(crew)))
        : Loc.GetString("wf-round-setup-radio");

    private string CrewName(Lobby lobby) => string.IsNullOrWhiteSpace(lobby.Title) ? lobby.Members[lobby.Host].Name : lobby.Title;

    private Lobby? CrewForBody(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || HasComp<GhostComponent>(body) ||
            TryComp<MobStateComponent>(body, out var state) && state.CurrentState == MobState.Dead ||
            !TryComp<ActorComponent>(body, out var actor) || FindLobby(actor.PlayerSession.UserId) is not { } lobby ||
            lobby.Members[actor.PlayerSession.UserId] is not { Spawned: true } member || member.Body != body)
            return null;
        return lobby;
    }

    private void UpdateCrewSignals()
    {
        if (_timing.CurTime < _nextCrewSignal)
            return;
        _nextCrewSignal = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        SyncCrewSignals();
    }

    /// <summary>Updates only connected crew members and removes capabilities from departed or previous bodies.</summary>
    private void SyncCrewSignals()
    {
        var bodies = new HashSet<EntityUid>();
        var recipients = new HashSet<NetUserId>();
        foreach (var lobby in _lobbies.Values)
        foreach (var (user, member) in lobby.Members)
        {
            if (!member.Spawned || !_players.TryGetSessionById(user, out var player) ||
                player.Status != SessionStatus.InGame || player.AttachedEntity is not { } body || CrewForBody(body) != lobby)
                continue;
            bodies.Add(body);
            recipients.Add(user);
            _watchers.Add(user);
            InstallCrewRadio(body);
            RaiseNetworkEvent(LeaderSignal(lobby, player, body), player.Channel);
        }
        foreach (var body in _crewRadios.Except(bodies).ToArray())
            RemoveCrewRadio(body);
        foreach (var user in _signalRecipients.Except(recipients))
        {
            if (_players.TryGetSessionById(user, out var player))
                RaiseNetworkEvent(new WFCrewSignalEvent(null, string.Empty, string.Empty, WFCrewLeaderStatus.Unavailable), player.Channel);
        }
        _signalRecipients.Clear();
        _signalRecipients.UnionWith(recipients);
    }

    private WFCrewSignalEvent LeaderSignal(Lobby lobby, ICommonSession player, EntityUid body)
    {
        var leader = lobby.Members[lobby.Host];
        var status = player.UserId == lobby.Host ? WFCrewLeaderStatus.Self : WFCrewLeaderStatus.Unavailable;
        var origin = _crewTransform.GetWorldPosition(body);
        System.Numerics.Vector2? offset = null;
        if (player.UserId != lobby.Host && leader.Body is { } target && !TerminatingOrDeleted(target))
        {
            status = WFCrewLeaderStatus.Elsewhere;
            if (Transform(body).MapID == Transform(target).MapID)
            {
                offset = _crewTransform.GetWorldPosition(target) - origin;
                status = TryComp<MobStateComponent>(target, out var state) && state.CurrentState == MobState.Dead
                    ? WFCrewLeaderStatus.Down : WFCrewLeaderStatus.Tracking;
            }
        }
        return new WFCrewSignalEvent(GetNetEntity(body), CrewName(lobby), leader.Name, status, origin, offset);
    }

    private void InstallCrewRadio(EntityUid body)
    {
        var owned = EnsureComp<WFPlayerCrewRadioComponent>(body);
        if (!TryComp<IntrinsicRadioTransmitterComponent>(body, out var transmitter))
        {
            transmitter = AddComp<IntrinsicRadioTransmitterComponent>(body);
            transmitter.Channels.Clear();
            owned.AddedTransmitter = true;
        }
        transmitter.Channels.Add(CrewChannel);
        if (!HasComp<IntrinsicRadioReceiverComponent>(body))
        {
            AddComp<IntrinsicRadioReceiverComponent>(body);
            owned.AddedReceiver = true;
        }
        if (!TryComp<ActiveRadioComponent>(body, out var active))
        {
            active = AddComp<ActiveRadioComponent>(body);
            owned.AddedActive = true;
        }
        active.Channels.Add(CrewChannel);
        _crewRadios.Add(body);
    }

    private void RemoveCrewRadio(EntityUid body)
    {
        _crewRadios.Remove(body);
        if (TerminatingOrDeleted(body) || !TryComp<WFPlayerCrewRadioComponent>(body, out var owned))
            return;
        if (TryComp<IntrinsicRadioTransmitterComponent>(body, out var transmitter))
        {
            transmitter.Channels.Remove(CrewChannel);
            if (owned.AddedTransmitter && transmitter.Channels.Count == 0)
                RemComp<IntrinsicRadioTransmitterComponent>(body);
        }
        if (TryComp<ActiveRadioComponent>(body, out var active))
        {
            active.Channels.Remove(CrewChannel);
            if (active.Channels.Count == 0 && !active.ReceiveAllChannels)
            {
                if (owned.AddedActive) RemComp<ActiveRadioComponent>(body);
                if (owned.AddedReceiver) RemComp<IntrinsicRadioReceiverComponent>(body);
            }
        }
        RemComp<WFPlayerCrewRadioComponent>(body);
    }
}

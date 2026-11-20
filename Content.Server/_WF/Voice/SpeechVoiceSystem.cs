using System.Numerics;
using Content.Server._EinsteinEngines.Language;
using Content.Server.Chat.Systems;
using Content.Server.Radio;
using Content.Shared._EinsteinEngines.Language;
using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared._WF.RMCCore.Deafness;
using Content.Shared._WF.Voice;
using Content.Shared.Chat;
using Content.Shared.Cloning;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Preferences;
using Content.Shared.Radio.Components;
using Content.Shared.Speech;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WF.Voice;

/// <summary>Gives characters their profile's voice and sends each listener the lines they hear.</summary>
public sealed partial class SpeechVoiceSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private LanguageSystem _language = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityQuery<ActorComponent> _actorQuery;
    private EntityQuery<DeafComponent> _deafQuery;
    private EntityQuery<GhostComponent> _ghostQuery;
    private EntityQuery<RadioSpeakerComponent> _speakerQuery;

    /// <summary>Lines sent this tick, keyed by speaker and escaped text, so one heard twice plays once.</summary>
    private readonly Dictionary<(EntityUid Source, string Text), Line> _lines = new();

    /// <summary>Radio lines received this tick, sent together in <see cref="Update"/>.</summary>
    private readonly Dictionary<(EntityUid Source, string Text), RadioLine> _radio = new();

    private readonly Dictionary<string, Filter> _groups = new();
    private readonly Dictionary<string, Filter> _inEar = new();

    /// <summary>Players a radio line could still reach through a loudspeaker. Shrinks as they are served.</summary>
    private readonly List<(ICommonSession Session, MapId Map, Vector2 Position)> _listeners = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<SpeechVoiceComponent, ProfileLoadFinishedEvent>(OnProfileLoadFinished);
        SubscribeLocalEvent<SpeechVoiceComponent, CloningEvent>(OnCloning);
        SubscribeLocalEvent<SpeechVoiceComponent, EntitySpokeEvent>(OnSpoke);
        SubscribeLocalEvent<ActiveRadioComponent, RadioReceiveEvent>(OnRadioReceive);

        _actorQuery = GetEntityQuery<ActorComponent>();
        _deafQuery = GetEntityQuery<DeafComponent>();
        _ghostQuery = GetEntityQuery<GhostComponent>();
        _speakerQuery = GetEntityQuery<RadioSpeakerComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var (key, radio) in _radio)
        {
            SendRadio(key, radio);
        }

        _radio.Clear();
        _lines.Clear();
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        ApplyProfile(args.Mob, args.Profile);
    }

    /// <summary>A later profile load, such as a DNA scrambler, brings a new voice.</summary>
    private void OnProfileLoadFinished(Entity<SpeechVoiceComponent> ent, ref ProfileLoadFinishedEvent args)
    {
        if (args.Profile is { } profile)
            ApplyProfile(ent, profile);
    }

    private void ApplyProfile(EntityUid uid, HumanoidCharacterProfile profile)
    {
        var comp = EnsureComp<SpeechVoiceComponent>(uid);
        comp.Voice = profile.SpeechVoice.Clamped();
        Dirty(uid, comp);
    }

    private void OnCloning(Entity<SpeechVoiceComponent> ent, ref CloningEvent args)
    {
        var clone = EnsureComp<SpeechVoiceComponent>(args.Target);
        clone.Voice = ent.Comp.Voice.Clone();
        Dirty(args.Target, clone);
    }

    private void OnSpoke(Entity<SpeechVoiceComponent> ent, ref EntitySpokeEvent args)
    {
        if (!ent.Comp.Voice.Enabled || string.IsNullOrWhiteSpace(args.Message) || !args.Language.SpeechOverride.RequireSpeech)
            return;

        var line = LineFor((ent, FormattedMessage.EscapeText(args.Message)));
        var voice = VoiceFor(ent, line);
        var range = args.IsWhisper ? SpeechVoice.WhisperRange : SpeechVoice.VoiceRange;
        string? scrambled = null;
        string? muffled = null;
        string? muffledScrambled = null;

        _groups.Clear();
        foreach (var session in Filter.Empty().AddInRange(_transform.GetMapCoordinates(ent), range).Recipients)
        {
            if (line.Served.Contains(session) || IsDeaf(session))
                continue;

            var understands = Understands(session, args.Language);
            if (!understands)
                scrambled ??= _language.ObfuscateSpeech(args.Message, args.Language);

            string text;
            if (!args.IsWhisper || InClearRange(session, ent))
            {
                // Only clear listeners count as served, so a muffled one can still get the line by radio.
                line.Served.Add(session);
                text = understands ? args.Message : scrambled!;
            }
            else
            {
                text = understands
                    ? (muffled ??= _chat.ObfuscateMessageReadability(args.Message))
                    : (muffledScrambled ??= _chat.ObfuscateMessageReadability(scrambled!));
            }

            AddTo(_groups, text, session);
        }

        var source = GetNetEntity(ent);
        var sounds = CompOrNull<SpeechComponent>(ent)?.SpeechSounds;
        foreach (var (text, filter) in _groups)
        {
            RaiseNetworkEvent(new SpeechVoiceSpokeEvent(source, text, args.IsWhisper, voice, sounds), filter);
        }
    }

    private void OnRadioReceive(Entity<ActiveRadioComponent> ent, ref RadioReceiveEvent args)
    {
        if (!TryComp<SpeechVoiceComponent>(args.MessageSource, out var talker) || !talker.Voice.Enabled)
            return;

        // The same loudspeakers RadioDeviceSystem keeps quiet.
        if (_speakerQuery.TryComp(ent, out var speaker)
            && (ent.Owner == args.RadioSource || speaker.PowerRequired && !_power.IsPowered(ent.Owner)))
        {
            return;
        }

        var key = (args.MessageSource, args.OriginalChatMsg.Message);
        if (!_radio.TryGetValue(key, out var radio))
        {
            radio = new RadioLine(args.LanguageObfuscatedChatMsg.Message, args.Language);
            _radio[key] = radio;
        }

        radio.Receivers.Add(ent);
    }

    /// <summary>Plays the voice in each receiving player's ear, and from each loudspeaker to players near it.</summary>
    private void SendRadio((EntityUid Source, string Text) key, RadioLine radio)
    {
        if (string.IsNullOrWhiteSpace(key.Text) || !TryComp<SpeechVoiceComponent>(key.Source, out var comp))
            return;

        var line = LineFor(key);
        var voice = VoiceFor(key.Source, line) ?? comp.Voice;
        var listenersFound = false;
        _inEar.Clear();

        foreach (var receiver in radio.Receivers)
        {
            // Ghosts hear every channel, which would fill their voice cap. They still get the text.
            if (_ghostQuery.HasComp(receiver))
                continue;

            if (_speakerQuery.HasComp(receiver))
            {
                if (!listenersFound)
                {
                    FindListeners(line);
                    listenersFound = true;
                }

                SendFromSpeaker(receiver, key.Text, radio, line, voice);
                continue;
            }

            if ((_actorQuery.TryComp(receiver, out var actor)
                 || _actorQuery.TryComp(Transform(receiver).ParentUid, out actor))
                && !IsDeaf(actor.PlayerSession)
                && line.Served.Add(actor.PlayerSession))
            {
                AddTo(_inEar, RadioText(actor.PlayerSession, key.Text, radio), actor.PlayerSession);
            }
        }

        foreach (var (text, filter) in _inEar)
        {
            RaiseNetworkEvent(new SpeechVoiceRadioEvent(NetEntity.Invalid, text, voice), filter);
        }
    }

    /// <summary>Records where each player who could hear a loudspeaker stands, once per line.</summary>
    private void FindListeners(Line line)
    {
        _listeners.Clear();
        foreach (var session in _player.NetworkedSessions)
        {
            if (session.AttachedEntity is not { } entity
                || line.Served.Contains(session)
                || IsDeaf(session)
                || !TryComp(entity, out TransformComponent? xform))
            {
                continue;
            }

            _listeners.Add((session, xform.MapID, _transform.GetWorldPosition(xform)));
        }
    }

    /// <summary>Plays the line from a loudspeaker to the remaining listeners near it.</summary>
    private void SendFromSpeaker(EntityUid speaker, string text, RadioLine radio, Line line, SpeechVoice voice)
    {
        if (_listeners.Count == 0)
            return;

        var coords = _transform.GetMapCoordinates(speaker);
        _groups.Clear();

        for (var i = _listeners.Count - 1; i >= 0; i--)
        {
            var (session, map, position) = _listeners[i];
            if (map != coords.MapId || (position - coords.Position).Length() >= SpeechVoice.VoiceRange)
                continue;

            _listeners.RemoveSwap(i);
            if (line.Served.Add(session))
                AddTo(_groups, RadioText(session, text, radio), session);
        }

        var source = GetNetEntity(speaker);
        foreach (var (message, filter) in _groups)
        {
            RaiseNetworkEvent(new SpeechVoiceRadioEvent(source, message, voice), filter);
        }
    }

    private string RadioText(ICommonSession session, string text, RadioLine radio)
    {
        return Understands(session, radio.Language) ? text : radio.Scrambled;
    }

    private Line LineFor((EntityUid Source, string Text) key)
    {
        if (!_lines.TryGetValue(key, out var line))
        {
            line = new Line();
            _lines[key] = line;
        }

        return line;
    }

    /// <summary>The voice a mask gives, worked out once per line. Null when the speaker uses their own.</summary>
    private SpeechVoice? VoiceFor(EntityUid speaker, Line line)
    {
        if (!line.VoiceResolved)
        {
            line.Voice = MaskedVoice(speaker);
            line.VoiceResolved = true;
        }

        return line.Voice;
    }

    /// <summary>The named character's own voice if one exists, else a voice made from the mask's name.</summary>
    private SpeechVoice? MaskedVoice(EntityUid speaker)
    {
        var name = Name(speaker);
        var ev = new TransformSpeakerNameEvent(speaker, name);
        RaiseLocalEvent(speaker, ev);
        if (ev.VoiceName == name)
            return null;

        EntityUid? match = null;
        SpeechVoice? voice = null;
        var query = EntityQueryEnumerator<SpeechVoiceComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var other, out var meta))
        {
            // The lowest uid wins, so two characters with the same name always give the same answer.
            if (uid != speaker && meta.EntityName == ev.VoiceName && (match is not { } best || uid.Id < best.Id))
            {
                match = uid;
                voice = other.Voice;
            }
        }

        return voice ?? SpeechVoice.FromName(ev.VoiceName);
    }

    private bool IsDeaf(ICommonSession session)
    {
        return session.AttachedEntity is { } listener && _deafQuery.HasComp(listener);
    }

    private bool Understands(ICommonSession session, LanguagePrototype language)
    {
        return session.AttachedEntity is not { } listener || _language.CanUnderstand(listener, language.ID);
    }

    private bool InClearRange(ICommonSession session, EntityUid speaker)
    {
        return session.AttachedEntity is { } listener
               && _transform.InRange(listener, speaker, SpeechVoice.WhisperClearRange);
    }

    private static void AddTo(Dictionary<string, Filter> groups, string text, ICommonSession session)
    {
        if (!groups.TryGetValue(text, out var filter))
        {
            filter = Filter.Empty();
            groups[text] = filter;
        }

        filter.AddPlayer(session);
    }

    /// <summary>The players a line has reached this tick, and the voice it plays in.</summary>
    private sealed class Line
    {
        public readonly HashSet<ICommonSession> Served = new();
        public SpeechVoice? Voice;
        public bool VoiceResolved;
    }

    /// <summary>A radio line's scrambled text, language and receivers, gathered over one tick.</summary>
    private sealed class RadioLine(string scrambled, LanguagePrototype language)
    {
        public readonly string Scrambled = scrambled;
        public readonly LanguagePrototype Language = language;
        public readonly List<EntityUid> Receivers = new();
    }
}

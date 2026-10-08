using System.Threading.Tasks;
using Content.Server._EinsteinEngines.Language;
using Content.Server.Chat.Systems;
using Content.Shared.Radio.Components;
using Content.Shared._EinsteinEngines.Language;
using Content.Shared._EinsteinEngines.Language.Components;
using Content.Shared._EinsteinEngines.Language.Systems;
using Content.Shared._Forge.CCVars;
using Content.Shared._Forge.TTS;
using Content.Shared.Chat;
using Content.Shared.Silicons.StationAi;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Players.RateLimiting;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Shared.Radio.Components;

namespace Content.Server._Forge.TTS;

// ReSharper disable once InconsistentNaming
public sealed partial class TTSSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly INetConfigurationManager _netCfg = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly TTSManager _ttsManager = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly IRobustRandom _rng = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;

    private readonly List<string> _sampleText =
        new()
        {
            "Съешь же ещё этих мягких французских булок, да выпей чаю.",
            "Клоун, прекрати разбрасывать банановые кожурки офицерам под ноги!",
            "Капитан, вы уверены что хотите назначить клоуна на должность главы персонала?",
            "Эс Бэ! Тут человек в сером костюме, с тулбоксом и в маске! Помогите!!",
            "Учёные, тут странная аномалия в баре! Она уже съела мима!",
            "Я надеюсь что инженеры внимательно следят за сингулярностью...",
            "Вы слышали эти странные крики в техах? Мне кажется туда ходить небезопасно.",
            "Вы не видели Гамлета? Мне кажется он забегал к вам на кухню.",
            "Здесь есть доктор? Человек умирает от отравленного пончика! Нужна помощь!",
            "Вам нужно согласие и печать квартирмейстера, если вы хотите сделать заказ на партию дробовиков.",
            "Возле эвакуационного шаттла разгерметизация! Инженеры, нам срочно нужна ваша помощь!",
            "Бармен, налей мне самого крепкого вина, которое есть в твоих запасах!"
        };

    private const int MaxMessageChars = 100 * 2; // same as SingleBubbleCharLimit * 2
    private bool _isEnabled = false;

    public override void Initialize()
    {
        _cfg.OnValueChanged(ForgeCCVars.TTSEnabled, v => _isEnabled = v, true);

        SubscribeLocalEvent<TransformSpeechEvent>(OnTransformSpeech);
        SubscribeLocalEvent<TTSComponent, EntitySpokeEvent>(OnEntitySpoke);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        SubscribeNetworkEvent<RequestPreviewTTSEvent>(OnRequestPreviewTTS);

        RegisterRateLimits();
    }


    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _ttsManager.ResetCache();
    }

    private async void OnRequestPreviewTTS(RequestPreviewTTSEvent ev, EntitySessionEventArgs args)
    {
        if (!_isEnabled ||
            !_prototypeManager.TryIndex<TTSVoicePrototype>(ev.VoiceId, out var protoVoice))
            return;

        if (HandleRateLimit(args.SenderSession) != RateLimitStatus.Allowed)
            return;

        var previewText = _rng.Pick(_sampleText);
        var soundData = await GenerateTTS(previewText, protoVoice.Speaker);
        if (soundData is null)
            return;

        RaiseNetworkEvent(new PlayTTSEvent(soundData), Filter.SinglePlayer(args.SenderSession));
    }

    private async void OnEntitySpoke(EntityUid uid, TTSComponent component, EntitySpokeEvent args)
    {
        // cheap bail-outs first, so we don't snapshot recipients for nothing.
        if (!_isEnabled || args.Message.Length > MaxMessageChars)
            return;

        if (TryComp<MindContainerComponent>(uid, out var mindCon)
            && mindCon.Mind is { } mindUid
            && TryComp<MindComponent>(mindUid, out var mind)
            && mind.UserId is { } userId
            && _player.TryGetSessionById(userId, out var session))
        {
            if (!_netCfg.GetClientCVar(session.Channel, ForgeCCVars.LocalTTSEnabled))
                return;
        }

        // snapshot the listener set on the speaking tick, before any await below.
        // Audio must reach exactly who received the text; recomputing it after the TTS API
        // round-trip would use world state up to several seconds newer than the chat message.
        var recipients = CaptureRecipients(args.Source, args.IsWhisper, args.Recipients, out var inPvs);
        if (recipients.Count == 0)
            return;

        if (HasComp<ActiveRadioComponent>(uid))
            await Task.Delay(1000);

        var voiceId = component.VoicePrototypeId;
        if (voiceId == null)
            return;

        var voiceEv = new TransformSpeakerVoiceEvent(uid, voiceId);
        RaiseLocalEvent(uid, voiceEv);
        voiceId = voiceEv.VoiceId;

        if (!_prototypeManager.TryIndex<TTSVoicePrototype>(voiceId, out var protoVoice))
            return;

        var obfuscatedMessage = _language.ObfuscateSpeech(args.Message, args.Language);

        await Handle(args.Source, args.Message, protoVoice.Speaker, args.IsWhisper, obfuscatedMessage, args.Language, recipients, inPvs);
    }

    /// <summary>
    ///     builds the set of sessions that should hear this line.
    ///     Must be called synchronously on the speaking tick.
    /// </summary>
    private Dictionary<ICommonSession, ChatSystem.ICChatRecipientData> CaptureRecipients(
        EntityUid source,
        bool isWhisper,
        Dictionary<ICommonSession, ChatSystem.ICChatRecipientData>? fromChat,
        out HashSet<ICommonSession> inPvs)
    {
        var result = new Dictionary<ICommonSession, ChatSystem.ICChatRecipientData>();

        // a positional PlayTTSEvent only plays if the client actually knows the
        // source entity, and an AI's PVS follows its remote eye rather than its brain. Record who
        // has the speaker in PVS right now so Handle can pick positional vs. source-less audio.
        inPvs = Exists(source)
            ? [.. Filter.Pvs(source).Recipients]
            : [];

        // Reuse what ChatSystem already computed for this message: it has run the camera and
        // station-AI expansion once already, so we neither repeat that station-wide scan nor
        // end up delivering audio to a different set of players than the text went to.
        // Copied rather than aliased, since other EntitySpokeEvent handlers may still mutate it.
        if (fromChat != null)
        {
            foreach (var (session, data) in fromChat)
            {
                // Distant ghost observers are added with Range -1: they get the text, not the audio.
                if (data.Range < 0)
                    continue;

                result[session] = data;
            }

            return result;
        }

        // Fallback: the event came from something other than the local say/whisper paths, so
        // there is no precomputed set and we have to build (and expand) one ourselves.
        if (!Exists(source))
            return result;

        var xformQuery = GetEntityQuery<TransformComponent>();
        var sourceXform = xformQuery.GetComponent(source);
        var sourcePos = _xforms.GetWorldPosition(sourceXform, xformQuery);
        var sourceMap = sourceXform.MapID;
        var range = isWhisper ? SharedChatSystem.WhisperMuffledRange : ChatSystem.VoiceRange;

        foreach (var session in inPvs)
        {
            if (session.AttachedEntity is not { } listener ||
                !xformQuery.TryGetComponent(listener, out var xform) ||
                xform.MapID != sourceMap)
            {
                continue;
            }

            var distance = (sourcePos - _xforms.GetWorldPosition(xform, xformQuery)).Length();
            if (distance > range)
                continue;

            result.TryAdd(session, new ChatSystem.ICChatRecipientData(distance, false));
        }

        RaiseLocalEvent(new ExpandICChatRecipientsEvent(source, source,
            isWhisper ? ChatChannel.Whisper : ChatChannel.Local, range, result));

        return result;
    }

    private async Task Handle(
        EntityUid uid,
        string message,
        string speaker,
        bool isWhisper,
        string obfuscatedMessage,
        LanguagePrototype language,
        Dictionary<ICommonSession, ChatSystem.ICChatRecipientData> recipients,
        HashSet<ICommonSession> inPvs
        )
    {
        var fullSoundData = await GenerateTTS(message, speaker, isWhisper);
        if (fullSoundData is null) return;
        await Task.Delay(70);

        var obfSoundData = await GenerateTTS(obfuscatedMessage, speaker, isWhisper);
        if (obfSoundData is null) return;

        if (!Exists(uid))
            return;

        var fullTtsEvent = new PlayTTSEvent(fullSoundData, GetNetEntity(uid), isWhisper);
        var obfTtsEvent = new PlayTTSEvent(obfSoundData, GetNetEntity(uid), isWhisper);

        foreach (var (session, recipient) in recipients)
        {
            if (session.AttachedEntity is not { } listener) continue;
            var canUnderstand = CanUnderstandLanguage(listener, language.ID);
            var getsClearWhisper = !isWhisper || recipient.Range <= ChatSystem.WhisperClearRange;

            // Forge-Change: relayed audio (cameras, AI eyes) and any listener that doesn't have the
            // speaker in PVS must get a source-less event, because a positional one references an
            // entity their client doesn't know and would simply not play. Everyone else keeps
            // directional audio - including an AI whose eye is watching the speaker directly.
            if (recipient.HearingEntity != null || !inPvs.Contains(session))
            {
                RaiseNetworkEvent(new PlayTTSEvent(canUnderstand && getsClearWhisper
                    ? fullSoundData : obfSoundData, isWhisper: isWhisper), session);
                continue;
            }

            RaiseNetworkEvent(canUnderstand && getsClearWhisper ? fullTtsEvent : obfTtsEvent, session);
        }
    }

    private bool CanUnderstandLanguage(EntityUid listener, string languageId)
    {
        if (languageId == SharedLanguageSystem.UniversalPrototype || languageId == SharedLanguageSystem.PsychomanticPrototype)
            return true;

        if (TryComp<UniversalLanguageSpeakerComponent>(listener, out var universal) && universal.Enabled)
            return true;

        return TryComp<LanguageSpeakerComponent>(listener, out var speaker)
               && speaker.UnderstoodLanguages.Contains(languageId);
    }

    // ReSharper disable once InconsistentNaming
    private async Task<byte[]?> GenerateTTS(string text, string speaker, bool isWhisper = false)
    {
        var textSanitized = Sanitize(text);
        if (textSanitized == "") return null;
        if (char.IsLetter(textSanitized[^1]))
            textSanitized += ".";

        var ssmlTraits = SoundTraits.RateFast;
        if (isWhisper)
            ssmlTraits = SoundTraits.PitchVerylow;
        var textSsml = ToSsmlText(textSanitized, ssmlTraits);

        return await _ttsManager.ConvertTextToSpeech(speaker, textSanitized);
    }

    public void OnlyPlayerTTS(EntityUid source, string message, string? voiceId, ICommonSession session, bool ifWhisper, LanguagePrototype language, bool isRadio = false)
    {
        _ = OnlyPlayerTTSAsync(source, message, voiceId, session, ifWhisper, language, isRadio);
    }

    private async Task OnlyPlayerTTSAsync(EntityUid source, string message, string? voiceId, ICommonSession session, bool ifWhisper, LanguagePrototype language, bool isRadio = false)
    {
        if (!_netCfg.GetClientCVar(session.Channel, ForgeCCVars.LocalTTSEnabled))
            return;

        if (HasComp<ActiveRadioComponent>(source))
            await Task.Delay(1000);

        if (!_isEnabled || message.Length > MaxMessageChars || string.IsNullOrWhiteSpace(voiceId))
            return;

        if (!_prototypeManager.TryIndex<TTSVoicePrototype>(voiceId, out var  protoVoice))
            return;

        var fullSoundData = await GenerateTTS(message, protoVoice.Speaker, ifWhisper);

        if (fullSoundData == null)
            return;

        var obfMessage = _language.ObfuscateSpeech(message, language);

        var obfSoundData = await GenerateTTS(obfMessage, protoVoice.Speaker, ifWhisper);

        if (obfSoundData == null)
            return;

        if (session.AttachedEntity is not {
            Valid: true
            } listener)
            return;

        var ttsEvent = CanUnderstandLanguage(listener, language.ID)
        ? new PlayTTSEvent(fullSoundData, GetNetEntity(source), ifWhisper, isRadio)
        : new PlayTTSEvent(obfSoundData, GetNetEntity(source), ifWhisper, isRadio);

        RaiseNetworkEvent(ttsEvent, session);
    }
}

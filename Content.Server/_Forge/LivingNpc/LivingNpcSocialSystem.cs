using Content.Server.Chat.Systems;
using Content.Server.Speech;
using Content.Shared._Forge.LivingNpc;
using Content.Shared._Forge.LivingNpc.Components;
using Content.Shared._Forge.LivingNpc.Prototypes;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.LivingNpc;

public sealed partial class LivingNpcSocialSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private LivingNpcMemorySystem _memory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LivingNpcComponent, ListenEvent>(OnListen);
        SubscribeLocalEvent<LivingNpcComponent, InteractHandEvent>(OnInteract);
    }

    private void OnInteract(Entity<LivingNpcComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || !_mobState.IsAlive(ent.Owner))
            return;

        if (!TryComp<LivingNpcMemoryComponent>(ent, out var memory) ||
            !TryComp<LivingNpcSocialComponent>(ent, out var social))
            return;

        _memory.NoteSeen(ent, memory, args.User);
        social.Greeted.Add(args.User);
        ent.Comp.ConversationPartner = args.User;
        ent.Comp.CurrentTarget = args.User;

        var name = Identity.Name(args.User, EntityManager, ent);
        var reputation = _memory.GetReputation(memory, args.User);
        var kind = reputation <= -0.35f
            ? LivingNpcSpeechKind.Insult
            : LivingNpcSpeechKind.Greeting;

        QueueReply(ent, memory, args.User, kind, name);
        SwitchToConverse(ent.Comp, args.User);
    }

    private void OnListen(Entity<LivingNpcComponent> ent, ref ListenEvent args)
    {
        if (args.Source == ent.Owner || !_mobState.IsAlive(ent.Owner))
            return;

        if (!TryComp<LivingNpcMemoryComponent>(ent, out var memory) ||
            !TryComp<LivingNpcSocialComponent>(ent, out var social))
            return;

        if (!InRange(ent, args.Source, social.ChatRange + 1.5f))
            return;

        _memory.NoteSeen(ent, memory, args.Source);
        memory.LastSpeaker = args.Source;
        memory.LastHeardPhrase = args.Message;

        var kind = Classify(ent, args.Message);
        if (kind == LivingNpcSpeechKind.None)
            return;

        // Don't let every NPC in a crowd answer at once.
        var chance = kind == LivingNpcSpeechKind.NameCall
            ? 1f
            : 0.35f + ent.Comp.Personality.Extraversion * 0.5f;

        if (kind is LivingNpcSpeechKind.Other && !_random.Prob(chance * 0.4f))
            return;

        if (kind is not LivingNpcSpeechKind.NameCall && !_random.Prob(chance))
            return;

        var name = Identity.Name(args.Source, EntityManager, ent);
        QueueReply(ent, memory, args.Source, kind, name);
        SwitchToConverse(ent.Comp, args.Source);
        social.Greeted.Add(args.Source);
    }

    public bool TrySay(EntityUid uid, LivingNpcComponent npc, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || _timing.CurTime < npc.NextUtterance)
            return false;

        if (!_mobState.IsAlive(uid))
            return false;

        _chat.TrySendInGameICMessage(uid, message, InGameICChatType.Speak, false);
        var gap = MathHelper.Lerp(
            4.5f,
            14f,
            1f - npc.Personality.Extraversion);
        npc.NextUtterance = _timing.CurTime + TimeSpan.FromSeconds(gap + _random.NextFloat(0f, 4f));
        npc.LastSocialAt = _timing.CurTime;
        npc.Mood.SocialNeed = Math.Clamp(npc.Mood.SocialNeed - 0.18f, 0f, 1f);
        npc.Mood.Happiness = Math.Clamp(npc.Mood.Happiness + 0.03f, 0f, 1f);
        npc.QueuedSpeech = null;
        return true;
    }

    public bool TryEmote(EntityUid uid, LivingNpcComponent npc, string emoteId)
    {
        if (string.IsNullOrWhiteSpace(emoteId) || _timing.CurTime < npc.NextEmote)
            return false;

        _chat.TryEmoteWithChat(uid, emoteId, ignoreActionBlocker: true);
        npc.NextEmote = _timing.CurTime + TimeSpan.FromSeconds(6f + _random.NextFloat(0f, 8f));
        npc.QueuedEmoteId = null;
        return true;
    }

    public bool TryCustomEmote(EntityUid uid, LivingNpcComponent npc, string locId)
    {
        if (_timing.CurTime < npc.NextEmote)
            return false;

        _chat.TrySendInGameICMessage(uid, Loc.GetString(locId), InGameICChatType.Emote, false);
        npc.NextEmote = _timing.CurTime + TimeSpan.FromSeconds(8f + _random.NextFloat(0f, 6f));
        return true;
    }

    public string? PickLine(LivingNpcComponent npc, LivingNpcSpeechKind kind, string? name = null)
    {
        if (!_prototypes.TryIndex(npc.DialogueId, out var dialogue))
            return null;

        var lines = kind switch
        {
            LivingNpcSpeechKind.Greeting => dialogue.Greetings,
            LivingNpcSpeechKind.Farewell => dialogue.Farewells,
            LivingNpcSpeechKind.HowAreYou => dialogue.HowAreYou,
            LivingNpcSpeechKind.Thanks => dialogue.Thanks,
            LivingNpcSpeechKind.Insult => dialogue.Insults,
            LivingNpcSpeechKind.Help => dialogue.Help,
            LivingNpcSpeechKind.Work => dialogue.Work,
            LivingNpcSpeechKind.Hunger => dialogue.Hunger,
            LivingNpcSpeechKind.Fear => dialogue.Fear,
            LivingNpcSpeechKind.Anger => dialogue.Anger,
            LivingNpcSpeechKind.SmallTalk => dialogue.SmallTalk,
            LivingNpcSpeechKind.NpcChat => dialogue.NpcChat,
            LivingNpcSpeechKind.Idle => dialogue.Idle,
            _ => dialogue.Idle,
        };

        if (lines.Count == 0)
            lines = dialogue.Idle;
        if (lines.Count == 0)
            return null;

        var id = _random.Pick(lines);
        return name == null
            ? Loc.GetString(id)
            : Loc.GetString(id, ("name", name));
    }

    public LivingNpcSpeechKind Classify(Entity<LivingNpcComponent> npc, string message)
    {
        var text = message.Trim().ToLowerInvariant();
        if (text.Length == 0)
            return LivingNpcSpeechKind.None;

        if (ContainsAny(text, "привет", "здарова", "здравствуй", "здравствуйте", "хай", "hello", "howdy", "доброе утро", "добрый день", "добрый вечер")
            || HasWord(text, "hi", "hey", "yo", "ку"))
            return LivingNpcSpeechKind.Greeting;

        if (ContainsAny(text, "пока", "прощай", "удачи", "bye", "goodbye", "see you", "до встречи"))
            return LivingNpcSpeechKind.Farewell;

        if (ContainsAny(text, "как дела", "как ты", "как жизнь", "how are you", "what's up", "whats up", "hows it"))
            return LivingNpcSpeechKind.HowAreYou;

        if (ContainsAny(text, "спасибо", "благодар", "thanks", "thank you", "thx"))
            return LivingNpcSpeechKind.Thanks;

        if (ContainsAny(text, "помоги", "на помощь", "help me", "help!", "hold on"))
            return LivingNpcSpeechKind.Help;

        if (ContainsAny(text, "дурак", "идиот", "мудак", "сука", "fuck you", "idiot", "asshole", "stupid"))
            return LivingNpcSpeechKind.Insult;

        var ownName = MetaData(npc).EntityName;
        if (ownName.Length > 2)
        {
            var first = ownName.Split(' ', 2)[0];
            if (first.Length > 2 && text.Contains(first.ToLowerInvariant(), StringComparison.Ordinal))
                return LivingNpcSpeechKind.NameCall;
        }

        return LivingNpcSpeechKind.Other;
    }

    public void QueueReply(
        Entity<LivingNpcComponent> npc,
        LivingNpcMemoryComponent memory,
        EntityUid source,
        LivingNpcSpeechKind kind,
        string name)
    {
        if (kind == LivingNpcSpeechKind.Insult)
        {
            npc.Comp.Mood.Anger = Math.Clamp(npc.Comp.Mood.Anger + 0.2f * npc.Comp.Personality.Temper, 0f, 1f);
            npc.Comp.Mood.Happiness = Math.Clamp(npc.Comp.Mood.Happiness - 0.12f, 0f, 1f);
            _memory.GetOrCreate(memory, source).Reputation =
                Math.Clamp(_memory.GetReputation(memory, source) - 0.2f, -1f, 1f);
        }
        else if (kind is LivingNpcSpeechKind.Greeting or LivingNpcSpeechKind.Thanks or LivingNpcSpeechKind.HowAreYou)
        {
            npc.Comp.Mood.Happiness = Math.Clamp(npc.Comp.Mood.Happiness + 0.06f, 0f, 1f);
            npc.Comp.Mood.SocialNeed = Math.Clamp(npc.Comp.Mood.SocialNeed - 0.1f, 0f, 1f);
            _memory.NoteTalk(npc, memory, source);
        }
        else if (kind == LivingNpcSpeechKind.Help)
        {
            npc.Comp.Mood.Fear = Math.Clamp(npc.Comp.Mood.Fear + 0.08f, 0f, 1f);
        }

        var replyKind = kind switch
        {
            LivingNpcSpeechKind.Greeting => LivingNpcSpeechKind.Greeting,
            LivingNpcSpeechKind.Farewell => LivingNpcSpeechKind.Farewell,
            LivingNpcSpeechKind.HowAreYou => LivingNpcSpeechKind.HowAreYou,
            LivingNpcSpeechKind.Thanks => LivingNpcSpeechKind.Thanks,
            LivingNpcSpeechKind.Insult => npc.Comp.Personality.Agreeableness > 0.6f
                ? LivingNpcSpeechKind.HowAreYou
                : LivingNpcSpeechKind.Insult,
            LivingNpcSpeechKind.Help => LivingNpcSpeechKind.Help,
            LivingNpcSpeechKind.NameCall => LivingNpcSpeechKind.Greeting,
            _ => _random.Prob(npc.Comp.Personality.Humor)
                ? LivingNpcSpeechKind.SmallTalk
                : LivingNpcSpeechKind.Idle,
        };

        npc.Comp.QueuedSpeech = PickLine(npc.Comp, replyKind, name);
        npc.Comp.ConversationPartner = source;
    }

    private void SwitchToConverse(LivingNpcComponent npc, EntityUid partner)
    {
        npc.CurrentIntent = LivingNpcIntent.Converse;
        npc.CurrentTarget = partner;
        npc.IntentStartedAt = _timing.CurTime;
    }

    private bool InRange(EntityUid uid, EntityUid other, float range)
    {
        var a = _transform.GetWorldPosition(uid);
        var b = _transform.GetWorldPosition(other);
        return (a - b).LengthSquared() <= range * range;
    }

    private static bool HasWord(string text, params string[] words)
    {
        var parts = text.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim('.', ',', '!', '?', ':', ';');
            foreach (var word in words)
            {
                if (trimmed.Equals(word, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (text.Contains(needle, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

public enum LivingNpcSpeechKind : byte
{
    None,
    Greeting,
    Farewell,
    HowAreYou,
    Thanks,
    Insult,
    Help,
    NameCall,
    Work,
    Hunger,
    Fear,
    Anger,
    SmallTalk,
    NpcChat,
    Idle,
    Other,
}

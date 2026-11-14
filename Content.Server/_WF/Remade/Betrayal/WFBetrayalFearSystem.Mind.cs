using Content.Server.Administration.Logs;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Betrayal;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Chemistry.Components;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Jittering;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._WF.Remade.Betrayal;

/// <summary>
/// The mind breaks, and lies. A player whose stress passes what the Director calls shaken gets a shaking aim, drops what they hold,
/// bleeds from the nose, twitches or screams without meaning to, and is told things that aren't so: an announcement of a shuttle
/// that never comes, a message on their PDA from someone who died. The lies reach that player's chat and nobody else's.
/// </summary>
public sealed partial class WFBetrayalFearSystem
{
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFBetrayalVoiceSystem _voice = default!;

    public const int Announcements = 8;
    public const int Messages = 8;
    private const float AimFrom = 0.25f;
    private const float AimUntil = 0.15f;
    private const float BloodDrip = 0.4f;
    private const string ScreamEmote = "Scream";

    private static readonly TimeSpan DropGap = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan BleedGap = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan TwitchGap = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan AnnouncementGap = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan MessageGap = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DripGap = TimeSpan.FromSeconds(2.5);
    private static readonly Color PdaColor = Color.FromHex("#6a9fd6");

    private void Mind(EntityUid uid, ActorComponent actor, WFBetrayalSubjectComponent subject, WFMonolithStage stage, float influence,
        float stress, float rate, TimeSpan now)
    {
        var breaking = WFBetrayalMath.Breaking(stress);
        Aim(uid, stage == WFMonolithStage.Quiet ? 0f : breaking);

        var scale = breaking * WFBetrayalMath.MindStage(stage) * (0.4f + 0.6f * influence) * rate;
        if (scale <= 0f)
            return;

        if (now >= subject.NextDrop && Roll(WFBetrayalMath.MindPerMinute(MindEffect.Drop) * scale))
        {
            subject.NextDrop = now + DropGap;
            Drop(uid, subject);
        }

        if (now >= subject.NextBleed && Roll(WFBetrayalMath.MindPerMinute(MindEffect.Nosebleed) * scale))
        {
            subject.NextBleed = now + BleedGap;
            Nosebleed(uid, subject, now);
        }

        if (now >= subject.NextTwitch && Roll(WFBetrayalMath.MindPerMinute(MindEffect.Twitch) * scale))
        {
            subject.NextTwitch = now + TwitchGap;
            Twitch(uid, subject, breaking);
        }

        if (now >= subject.NextAnnouncement && Roll(WFBetrayalMath.MindPerMinute(MindEffect.Announcement) * scale))
        {
            subject.NextAnnouncement = now + AnnouncementGap;
            FakeAnnouncement(uid, actor, subject);
        }

        if (now >= subject.NextMessage && Roll(WFBetrayalMath.MindPerMinute(MindEffect.Message) * scale))
        {
            subject.NextMessage = now + MessageGap;
            FakeMessage(uid, actor, subject);
        }
    }

    /// <summary>Gives a shaking hand to a player who is breaking, widening the spread of every gun they hold, and takes it back as they calm.</summary>
    public void Aim(EntityUid uid, float breaking)
    {
        if (breaking >= AimFrom)
        {
            var shaken = EnsureComp<WFShakenAimComponent>(uid);
            var spread = WFBetrayalMath.AimSpread(breaking);
            if (MathF.Abs(shaken.Spread - spread) < 0.25f && shaken.Spread > 1f)
                return;

            shaken.Spread = spread;
            Dirty(uid, shaken);
            RefreshGun(uid);
        }
        else if (breaking < AimUntil)
        {
            ReleaseAim(uid);
        }
    }

    private void ReleaseAim(EntityUid uid)
    {
        if (!HasComp<WFShakenAimComponent>(uid))
            return;

        RemComp<WFShakenAimComponent>(uid);
        RefreshGun(uid);
    }

    private void RefreshGun(EntityUid uid)
    {
        if (_hands.TryGetActiveItem(uid, out var held) && HasComp<GunComponent>(held))
            _gun.RefreshModifiers(new Entity<GunComponent?>(held.Value, null));
    }

    /// <summary>The hand lets go of what it holds.</summary>
    public bool Drop(EntityUid uid, WFBetrayalSubjectComponent subject)
    {
        if (!_hands.TryGetActiveItem(uid, out var held) || !_hands.TryDrop(uid, held.Value))
            return false;

        subject.Drops++;
        _popup.PopupEntity(Loc.GetString("wf-betrayal-drop", ("item", held.Value)), uid, uid, PopupType.MediumCaution);
        return true;
    }

    /// <summary>The nose starts to bleed: a few drips of blood over the next while, and a small bleed.</summary>
    public void Nosebleed(EntityUid uid, WFBetrayalSubjectComponent subject, TimeSpan now)
    {
        if (!HasComp<BloodstreamComponent>(uid))
            return;

        subject.Bleeds++;
        subject.Drips = _random.Next(4, 8);
        subject.NextDrip = now;
        _bloodstream.TryModifyBleedAmount(uid, 0.15f);
        _popup.PopupEntity(Loc.GetString("wf-betrayal-nosebleed"), uid, uid, PopupType.MediumCaution);
    }

    private void Drip(EntityUid uid, WFBetrayalSubjectComponent subject, TimeSpan now)
    {
        if (subject.Drips <= 0 || now < subject.NextDrip)
            return;

        subject.Drips--;
        subject.NextDrip = now + DripGap;
        if (!TryComp<BloodstreamComponent>(uid, out var blood))
        {
            subject.Drips = 0;
            return;
        }

        var drop = new Solution(blood.BloodReagent, FixedPoint2.New(BloodDrip));
        _puddle.TrySpillAt(uid, drop, out _, false);
        _bloodstream.TryModifyBloodLevel(uid, -FixedPoint2.New(BloodDrip), blood);
    }

    /// <summary>A twitch of the body or a cry they didn't mean to make. The harder they break, the likelier the cry.</summary>
    public void Twitch(EntityUid uid, WFBetrayalSubjectComponent subject, float breaking)
    {
        subject.Twitches++;
        if (_random.Prob(0.25f + 0.5f * breaking) && _proto.TryIndex<EmotePrototype>(ScreamEmote, out var scream) && scream != null)
        {
            _chat.TryEmoteWithChat(uid, scream, hideLog: true);
            return;
        }

        _jitter.DoJitter(uid, TimeSpan.FromSeconds(_random.NextFloat(0.8f, 1.6f)), false, 6f, 10f);
        _popup.PopupEntity(Loc.GetString("wf-betrayal-twitch"), uid, uid, PopupType.SmallCaution);
    }

    /// <summary>An announcement that never happened, shown to this player alone with the real announcement's wrap and sound.</summary>
    public string FakeAnnouncement(EntityUid uid, ActorComponent actor, WFBetrayalSubjectComponent subject)
    {
        var text = Loc.GetString($"wf-betrayal-announce-{_random.Next(1, Announcements + 1)}");
        var sender = Loc.GetString("chat-manager-sender-announcement");
        var wrapped = Loc.GetString("chat-manager-sender-announcement-wrap-message", ("sender", sender), ("message", FormattedMessage.EscapeText(text)));
        _chatManager.ChatMessageToOne(ChatChannel.Radio, text, wrapped, default, false, actor.PlayerSession.Channel);
        _audio.PlayGlobal(new SoundPathSpecifier(ChatSystem.DefaultAnnouncementSound), actor.PlayerSession, AudioParams.Default.WithVolume(-2f));
        subject.Announcements++;
        subject.LastAnnouncement = text;
        _adminLog.Add(LogType.Chat, LogImpact.Low, $"Remade false announcement shown only to {ToPrettyString(uid):user}: {text}");
        return text;
    }

    /// <summary>A PDA message from someone who is dead, shown to this player alone. Half the time it is what they last said.</summary>
    public string? FakeMessage(EntityUid uid, ActorComponent actor, WFBetrayalSubjectComponent subject)
    {
        var dead = _voice.Dead;
        if (dead.Count == 0)
            return null;

        var from = dead[_random.Next(dead.Count)];
        var text = from.Line != null && _random.Prob(0.5f)
            ? from.Line
            : Loc.GetString($"wf-betrayal-pda-{_random.Next(1, Messages + 1)}");
        var wrapped = Loc.GetString("wf-betrayal-pda-wrap", ("name", from.Name), ("message", FormattedMessage.EscapeText(text)));
        _chatManager.ChatMessageToOne(ChatChannel.Radio, text, wrapped, default, false, actor.PlayerSession.Channel, PdaColor);
        _audio.PlayGlobal(new SoundPathSpecifier("/Audio/Machines/twobeep.ogg"), actor.PlayerSession, AudioParams.Default.WithVolume(-4f));
        subject.Messages++;
        _adminLog.Add(LogType.Chat, LogImpact.Low, $"Remade false PDA message from {from.Name} shown only to {ToPrettyString(uid):user}: {text}");
        return text;
    }
}

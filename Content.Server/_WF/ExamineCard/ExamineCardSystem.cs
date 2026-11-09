using Content.Server._WF.Genitals;
using Content.Server.Chat.Managers;
using Content.Shared._WF.ExamineCard;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Chat;
using Content.Shared.Ghost;
using Content.Shared.IdentityManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.ExamineCard;

/// <summary>Sends a character's examine card to whoever examined them, and tells the character they were looked at.</summary>
public sealed partial class ExamineCardSystem : SharedExamineCardSystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GenitalConsentSystem _genitalConsent = default!;

    /// <summary>The same examiner looking again within this time doesn't tell the character twice.</summary>
    private static readonly TimeSpan NoticeCooldown = TimeSpan.FromSeconds(20);

    private readonly Dictionary<(EntityUid Examiner, EntityUid Target), TimeSpan> _lastNotice = new();

    public bool Enabled => _config.GetCVar(ExamineCardCVars.Enabled);

    /// <summary>Sends the card to a player. Not tied to the detailed examine option, which players turned off to stop the spam.</summary>
    public void SendCard(EntityUid target, EntityUid examiner, ICommonSession session, bool detailed = true)
    {
        var card = BuildCard(target, examiner, detailed);
        if (detailed)
            card.NsfwText = NsfwTextFor(target, examiner);

        RaiseNetworkEvent(card, session);
        NotifyExamined(target, examiner, detailed);
    }

    /// <summary>
    /// The target's adult description if the examiner may read it: the anatomy rules apply, and like the normal
    /// description it needs the face to show. Null otherwise.
    /// </summary>
    public string? NsfwTextFor(EntityUid target, EntityUid examiner)
    {
        if (!TryComp<NsfwDescriptionComponent>(target, out var nsfw)
            || Identity.Name(target, EntityManager) != Name(target)
            || !_genitalConsent.CanExamine(examiner, target))
            return null;

        return nsfw.Text;
    }

    /// <summary>Tells a player, in chat, that someone looked them over or glanced at them.</summary>
    public void NotifyExamined(EntityUid target, EntityUid examiner, bool detailed)
    {
        if (target == examiner
            || !TryComp<ActorComponent>(target, out var actor)
            || HasComp<GhostComponent>(examiner)
            || !_config.GetCVar(ExamineCardCVars.Notice))
            return;

        var now = _timing.CurTime;
        if (_lastNotice.TryGetValue((examiner, target), out var last) && now - last < NoticeCooldown)
            return;

        _lastNotice[(examiner, target)] = now;
        PruneNotices(now);

        var name = FormattedMessage.EscapeText(Identity.Name(examiner, EntityManager, target));
        var message = Loc.GetString(detailed ? "wf-examine-card-notice-close" : "wf-examine-card-notice-distant",
            ("examiner", name));
        _chat.ChatMessageToOne(ChatChannel.Emotes, message, message, EntityUid.Invalid, false,
            actor.PlayerSession.Channel, recordReplay: false);
    }

    private void PruneNotices(TimeSpan now)
    {
        if (_lastNotice.Count < 256)
            return;

        var stale = new List<(EntityUid, EntityUid)>();
        foreach (var (key, time) in _lastNotice)
        {
            if (now - time >= NoticeCooldown)
                stale.Add(key);
        }

        foreach (var key in stale)
        {
            _lastNotice.Remove(key);
        }
    }
}

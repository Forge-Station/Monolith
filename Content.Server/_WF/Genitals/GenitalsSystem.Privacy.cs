using System.Linq;
using Content.Server._Common.Consent;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Genitals;

// Privacy filter: GenitalsComponent state goes only to its owner, and to anatomy viewers while the owner is present.
// Field deltas assume a client holds every earlier state, which a newly allowed viewer or a returning owner's viewers do
// not, so both resend full states in the same tick.
public sealed partial class GenitalsSystem
{
    [Dependency] private IServerConsentManager _consentManager = default!;
    [Dependency] private GenitalConsentSystem _consent = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    /// <summary>Every networked field of GenitalsComponent; filled on first use.</summary>
    private string[]? _networkedFields;

    /// <summary>Players the filter allowed at their last check; only a newly allowed player causes a resend.</summary>
    private readonly HashSet<NetUserId> _anatomyViewers = new();

    private void InitializePrivacy()
    {
        SubscribeLocalEvent<GenitalsComponent, ComponentGetStateAttemptEvent>(OnGetStateAttempt);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<GenitalConsentChangedEvent>(OnPrivacyConsentChanged);
    }

    /// <summary>
    /// Whether the session may receive the body's GenitalsComponent: the body is its attached entity, or the session is an
    /// anatomy viewer and the body's player is present (an SSD body counts as not consenting). A null session is allowed.
    /// </summary>
    /// <remarks>
    /// Called from PVS worker threads, so it only reads. Replay recording never calls it (RobustToolbox skips the
    /// session-specific check without a player), so replays hold every body's anatomy and must stay admin-only.
    /// </remarks>
    public bool CanReceiveAnatomy(ICommonSession? session, EntityUid body)
    {
        return session == null
               || session.AttachedEntity == body
               || (IsAnatomyViewer(session) && _consent.IsActivelyPresent(body));
    }

    /// <summary>
    /// A humanoid attached entity must be adult (ghosts and observers pass, as on the client), and Adult content must be
    /// on for it or for the body its mind owns: a visiting ghost has no toggles of its own. <paramref name="changed"/> reads
    /// as <paramref name="changedOn"/>, for a toggle that is not assigned yet.
    /// </summary>
    private bool IsAnatomyViewer(ICommonSession session, EntityUid? changed = null, bool changedOn = false)
    {
        var viewer = session.AttachedEntity;
        if (viewer != null && TryComp<HumanoidAppearanceComponent>(viewer, out var humanoid) && !IsAdult(humanoid.Age))
            return false;

        if (viewer != null && MasterOn(viewer.Value, changed, changedOn))
            return true;

        return session.ContentData()?.Mind is { } mindId
               && TryComp<MindComponent>(mindId, out var mind)
               && mind.OwnedEntity is { } owned
               && owned != viewer
               && MasterOn(owned, changed, changedOn);
    }

    private bool MasterOn(EntityUid uid, EntityUid? changed, bool changedOn)
    {
        return uid == changed ? changedOn : _consent.HasMaster(uid);
    }

    /// <summary>The player's saved Adult content switch. False while the player is disconnected.</summary>
    public bool PlayerHasMaster(NetUserId userId)
    {
        return _consentManager.GetPlayerConsentSettings(userId).Toggles.TryGetValue(Settings.MasterConsent, out var state)
               && state == "on";
    }

    /// <summary>
    /// Marks every networked field of the body's GenitalsComponent dirty, so a session whose last copy is older gets a
    /// full state, never a single-field delta it has no base for.
    /// </summary>
    public void ResendAnatomy(Entity<GenitalsComponent> ent)
    {
        _networkedFields ??= EntityManager.ComponentFactory.GetRegistration<GenitalsComponent>()
            .NetworkedFieldLookup.Keys
            .ToArray();

        DirtyFields(ent.Owner, ent.Comp, null, _networkedFields);
    }

    /// <summary>Resends every body, paused ones included: a viewer was just allowed and may hold no copy, or stale ones.</summary>
    public void ResendAllAnatomy()
    {
        var query = AllEntityQuery<GenitalsComponent>();
        while (query.MoveNext(out var uid, out var genitals))
        {
            if (!TerminatingOrDeleted(uid))
                ResendAnatomy((uid, genitals));
        }
    }

    /// <summary>Runs on PVS worker threads.</summary>
    private void OnGetStateAttempt(Entity<GenitalsComponent> ent, ref ComponentGetStateAttemptEvent args)
    {
        if (!CanReceiveAnatomy(args.Player, ent.Owner))
            args.Cancelled = true;
    }

    /// <summary>
    /// The attached body is resent: its viewers received nothing while it was SSD, and its owner may hold an old copy. The
    /// viewer check then follows the new entity: a ghost after a minor body, or a visit that reads the owned body's
    /// toggles. A mind transfer clears the old body's toggles before the new body gets them, so it counts as a new viewer
    /// and resends once.
    /// </summary>
    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        if (TryComp<GenitalsComponent>(ev.Entity, out var genitals))
            ResendAnatomy((ev.Entity, genitals));

        RefreshViewer(ev.Player, IsAnatomyViewer(ev.Player));
    }

    /// <summary>
    /// The entity's master toggle is changing; ConsentSystem assigns it after the event. Checked now, not with the deferred
    /// consent reaction, so a resend lands before the first PVS pass that allows the viewer.
    /// </summary>
    private void OnMasterToggleUpdated(EntityUid entity, bool on)
    {
        RefreshViewersOf(entity, entity, on);
    }

    /// <summary>Toggles set without EntityConsentToggleUpdatedEvent (test fixtures) raise only this; other changes were checked already.</summary>
    private void OnPrivacyConsentChanged(ref GenitalConsentChangedEvent ev)
    {
        RefreshViewersOf(ev.Body, null, false);
    }

    /// <summary>Re-checks the players whose filter reads this entity's toggles: its own, and one whose mind owns it while visiting elsewhere.</summary>
    private void RefreshViewersOf(EntityUid entity, EntityUid? changed, bool changedOn)
    {
        if (TryComp<ActorComponent>(entity, out var actor))
            RefreshViewer(actor.PlayerSession, IsAnatomyViewer(actor.PlayerSession, changed, changedOn));

        if (_mind.TryGetMind(entity, out _, out var mind)
            && _player.TryGetSessionById(mind.UserId, out var owner)
            && owner.AttachedEntity != entity)
        {
            RefreshViewer(owner, IsAnatomyViewer(owner, changed, changedOn));
        }
    }

    /// <summary>Records the filter's answer for the player; a newly allowed player gets every body's full state.</summary>
    private void RefreshViewer(ICommonSession session, bool allowed)
    {
        if (!allowed)
            _anatomyViewers.Remove(session.UserId);
        else if (_anatomyViewers.Add(session.UserId))
            ResendAllAnatomy();
    }
}

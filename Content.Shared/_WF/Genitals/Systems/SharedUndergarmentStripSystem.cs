using Content.Shared._WF.Genitals.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Strip;
using Content.Shared.Verbs;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Genitals.Systems;

/// <summary>Other players removing or putting back a consenting target's undergarments: verbs, DoAfter, popups, logs and revocation.</summary>
/// <remarks>
/// Undergarments are markings, not inventory items, so the inventory strip pipeline does not apply. The owner's own path
/// is the Anatomy panel. Popups reach only the actor and the target, never bystanders.
/// </remarks>
public sealed partial class SharedUndergarmentStripSystem : EntitySystem
{
    [Dependency] private GenitalConsentSystem _consent = default!;
    [Dependency] private GenitalCoverageSystem _coverage = default!;
    [Dependency] private SharedGenitalsSystem _genitals = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStrippableSystem _strippable = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;

    /// <summary>Context menu category of the undergarment verbs.</summary>
    public static readonly VerbCategory UndergarmentCategory =
        new("wf-verb-category-undergarments", "/Textures/Interface/VerbIcons/outfit.svg.192dpi.png");

    private const string FailCovered = "wf-undergarment-fail-covered";
    private const string FailConsent = "wf-undergarment-fail-consent";
    private const string FailCooldown = "wf-undergarment-fail-cooldown";

    /// <summary>Completed actions per actor, target and kind (true = removal) this round, for the admin log. Server only.</summary>
    private readonly Dictionary<(EntityUid Actor, EntityUid Target, bool Remove), int> _counts = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GenitalsComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<GenitalsComponent, DoAfterAttemptEvent<UndergarmentStripDoAfterEvent>>(OnStripAttempt);
        SubscribeLocalEvent<GenitalsComponent, UndergarmentStripDoAfterEvent>(OnStripDoAfter);
        SubscribeLocalEvent<GenitalConsentChangedEvent>(OnConsentChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>Removals the actor completed on the target this round. Counted on the server only.</summary>
    public int GetRemovalCount(EntityUid actor, EntityUid target)
    {
        return _counts.TryGetValue((actor, target, true), out var count) ? count : 0;
    }

    /// <summary>Starts another player's removal or put-back after re-checking every condition; tells the actor why not.</summary>
    public bool TryStartStrip(EntityUid user, EntityUid target, UndergarmentSlot slot, bool remove)
    {
        if (!TryComp<GenitalsComponent>(target, out var genitals))
            return false;

        if (GetFailure(user, (target, genitals), slot, remove, true) is { } failure)
        {
            _popup.PopupClient(Loc.GetString(failure), user);
            return false;
        }

        var baseDelay = TimeSpan.FromSeconds(_genitals.Settings.StripDelay);
        var (delay, _) = _strippable.GetStripTimeModifiers(user, target, null, baseDelay);
        var ev = new UndergarmentStripDoAfterEvent { Slot = slot, Remove = remove };
        var args = new DoAfterArgs(EntityManager, user, delay, ev, target, target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            RequireCanInteract = true,
            BlockDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
            AttemptFrequency = AttemptFrequency.EveryTick,
        };

        if (!_doAfter.TryStartDoAfter(args))
            return false;

        // Stealth from the strip time modifiers only shortens the time: the target is always told.
        NotifyParticipants(user,
            target,
            remove ? "wf-undergarment-start-user" : "wf-undergarment-replace-start-user",
            remove ? "wf-undergarment-start-target" : "wf-undergarment-replace-start-target");

        var action = remove ? "remove" : "put back";
        var slotName = SlotName(slot);
        _adminLog.Add(LogType.Stripping,
            LogImpact.Low,
            $"{ToPrettyString(user):actor} is trying to {action} the {slotName} undergarment of {ToPrettyString(target):target}");
        return true;
    }

    /// <summary>Adds the verbs only when CanStrip holds, so nothing appears for anyone without consent.</summary>
    private void OnGetVerbs(Entity<GenitalsComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (args.User == args.Target || !args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        if (!_consent.CanStrip(args.User, ent.Owner) || !TryComp<HumanoidAppearanceComponent>(ent.Owner, out var humanoid))
            return;

        AddVerb(args, ent, humanoid, UndergarmentSlot.Top);
        AddVerb(args, ent, humanoid, UndergarmentSlot.Bottom);
    }

    /// <summary>Adds the remove or put-back verb for one worn undergarment, disabled with a reason while blocked.</summary>
    private void AddVerb(GetVerbsEvent<Verb> args, Entity<GenitalsComponent> ent, HumanoidAppearanceComponent humanoid, UndergarmentSlot slot)
    {
        if (!GenitalCoverageSystem.HasVisibleUndergarment(humanoid, slot))
            return;

        var user = args.User;
        var target = ent.Owner;
        var remove = !IsRemoved(ent.Comp, slot);
        var verb = new Verb
        {
            Text = Loc.GetString(VerbText(slot, remove)),
            Category = UndergarmentCategory,
            Act = () => TryStartStrip(user, target, slot, remove),
        };

        if (GetBlocker(ent, slot, remove, true) is { } blocker)
        {
            verb.Disabled = true;
            verb.Message = Loc.GetString(blocker);
        }

        args.Verbs.Add(verb);
    }

    /// <summary>Every tick: cancels once consent, presence, life, the undergarment or bare clothing no longer allow the action.</summary>
    /// <remarks>
    /// The server sees these changes first and cancels in the same tick, so the actor's client never runs this check for
    /// them. The reason therefore goes from the server to the actor's session only.
    /// </remarks>
    private void OnStripAttempt(Entity<GenitalsComponent> ent, ref DoAfterAttemptEvent<UndergarmentStripDoAfterEvent> args)
    {
        var user = args.DoAfter.Args.User;
        if (GetFailure(user, ent, args.Event.Slot, args.Event.Remove, false) is not { } failure)
            return;

        args.Cancel();
        if (_net.IsServer)
            _popup.PopupCursor(Loc.GetString(failure), user);
    }

    /// <summary>Applies a completed action. The check only catches another actor who finished the same undergarment first.</summary>
    private void OnStripDoAfter(Entity<GenitalsComponent> ent, ref UndergarmentStripDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var user = args.User;
        if (GetFailure(user, ent, args.Slot, args.Remove, false) != null)
            return;

        args.Handled = true;
        Apply(ent, user, args.Slot, args.Remove);
    }

    /// <summary>Removal sets Removed and ByOther and starts the cooldown; put-back clears both bits. Then visuals, popups and the log.</summary>
    private void Apply(Entity<GenitalsComponent> ent, EntityUid user, UndergarmentSlot slot, bool remove)
    {
        var target = ent.Owner;
        var bits = UndergarmentSlots.SlotBits(slot);
        if (remove)
        {
            ent.Comp.Undergarments |= bits;
            ent.Comp.StripCooldownUntil = _timing.CurTime + TimeSpan.FromSeconds(_genitals.Settings.StripCooldown);
            DirtyField(target, ent.Comp, nameof(GenitalsComponent.StripCooldownUntil));
        }
        else
        {
            ent.Comp.Undergarments &= ~bits;
        }

        DirtyField(target, ent.Comp, nameof(GenitalsComponent.Undergarments));
        RaiseVisualsChanged(target);

        NotifyParticipants(user,
            target,
            remove ? "wf-undergarment-done-user" : "wf-undergarment-replace-done-user",
            remove ? "wf-undergarment-done-target" : "wf-undergarment-replace-done-target");

        var count = CountCompleted(user, target, remove);
        var action = remove ? "removed" : "put back";
        var slotName = SlotName(slot);
        _adminLog.Add(LogType.Stripping,
            LogImpact.Medium,
            $"{ToPrettyString(user):actor} {action} the {slotName} undergarment of {ToPrettyString(target):target} ({count} this round)");
    }

    /// <summary>One tick after a toggle change: master off puts back everything, UndergarmentStrip off puts back what others removed.</summary>
    private void OnConsentChanged(ref GenitalConsentChangedEvent ev)
    {
        var body = ev.Body;
        if (!TryComp<GenitalsComponent>(body, out var genitals) || genitals.Undergarments == UndergarmentFlags.None)
            return;

        var flags = genitals.Undergarments;
        if (!_consent.HasMaster(body))
            flags = UndergarmentFlags.None;
        else if (_genitals.Settings.StripConsent is not { } strip || !_consent.HasStrictConsent(body, strip))
            flags = WithoutRemovalsByOthers(flags);

        if (flags == genitals.Undergarments)
            return;

        genitals.Undergarments = flags;
        DirtyField(body, genitals, nameof(GenitalsComponent.Undergarments));
        RaiseVisualsChanged(body);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _counts.Clear();
    }

    /// <summary>Why the actor cannot act on this undergarment now (loc id), or null. The consent reason is generic, so no setting of the target leaks.</summary>
    private string? GetFailure(EntityUid user, Entity<GenitalsComponent> target, UndergarmentSlot slot, bool remove, bool checkCooldown)
    {
        if (user == target.Owner
            || !_consent.CanStrip(user, target.Owner)
            || !TryComp<HumanoidAppearanceComponent>(target.Owner, out var humanoid)
            || !GenitalCoverageSystem.HasVisibleUndergarment(humanoid, slot)
            || IsRemoved(target.Comp, slot) == remove)
            return FailConsent;

        return GetBlocker(target, slot, remove, checkCooldown);
    }

    /// <summary>Why an allowed action cannot run now (loc id): clothing over the region, or the removal cooldown. Put-back is never on cooldown.</summary>
    private string? GetBlocker(Entity<GenitalsComponent> target, UndergarmentSlot slot, bool remove, bool checkCooldown)
    {
        if (_coverage.IsRegionCoveredByClothing(target.Owner, UndergarmentSlots.RegionOf(slot)))
            return FailCovered;

        if (checkCooldown && remove && target.Comp.StripCooldownUntil > _timing.CurTime)
            return FailCooldown;

        return null;
    }

    /// <summary>Tells the actor (predicted, on their client only) and the target (from the server, to their session only). Names respect identity.</summary>
    private void NotifyParticipants(EntityUid user, EntityUid target, string userMessage, string targetMessage)
    {
        _popup.PopupClient(Loc.GetString(userMessage, ("target", Identity.Entity(target, EntityManager))), target, user);
        _popup.PopupEntity(Loc.GetString(targetMessage, ("user", Identity.Entity(user, EntityManager))),
            target,
            target,
            PopupType.MediumCaution);
    }

    /// <summary>Lets a predicting client refresh the sprite at once.</summary>
    private void RaiseVisualsChanged(EntityUid body)
    {
        var ev = new GenitalsVisualsChangedEvent();
        RaiseLocalEvent(body, ref ev);
    }

    /// <summary>Counts a completed action on the server, where admin logs are kept.</summary>
    private int CountCompleted(EntityUid user, EntityUid target, bool remove)
    {
        if (!_net.IsServer)
            return 0;

        var key = (user, target, remove);
        _counts.TryGetValue(key, out var count);
        count++;
        _counts[key] = count;
        return count;
    }

    private static bool IsRemoved(GenitalsComponent genitals, UndergarmentSlot slot)
    {
        return (genitals.Undergarments & UndergarmentSlots.RemovedFlag(slot)) != 0;
    }

    /// <summary>Clears the slots another player removed; the owner's own removals stay.</summary>
    private static UndergarmentFlags WithoutRemovalsByOthers(UndergarmentFlags flags)
    {
        if ((flags & UndergarmentFlags.TopByOther) != 0)
            flags &= ~UndergarmentSlots.SlotBits(UndergarmentSlot.Top);

        if ((flags & UndergarmentFlags.BottomByOther) != 0)
            flags &= ~UndergarmentSlots.SlotBits(UndergarmentSlot.Bottom);

        return flags;
    }

    private static string SlotName(UndergarmentSlot slot)
    {
        return slot == UndergarmentSlot.Top ? "top" : "bottom";
    }

    private static string VerbText(UndergarmentSlot slot, bool remove)
    {
        if (slot == UndergarmentSlot.Top)
            return remove ? "wf-undergarment-verb-remove-top" : "wf-undergarment-verb-replace-top";

        return remove ? "wf-undergarment-verb-remove-bottom" : "wf-undergarment-verb-replace-bottom";
    }
}

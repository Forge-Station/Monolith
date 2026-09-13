using Content.Shared._WF.Genitals.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Robust.Shared.Player;

namespace Content.Shared._WF.Genitals.Systems;

/// <summary>
/// Owner requests from the Anatomy panel. The predicting client and the server validate them the same way, and every
/// request acts only on the sender's attached entity.
/// </summary>
public abstract partial class SharedGenitalsSystem
{
    [Dependency] private GenitalConsentSystem _consent = default!;
    [Dependency] private SharedArousalSystem _arousal = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;

    private void InitializeRequests()
    {
        SubscribeAllEvent<AnatomySetArousalRequestEvent>(OnSetArousal);
        SubscribeAllEvent<AnatomySetRevealModeRequestEvent>(OnSetRevealMode);
        SubscribeAllEvent<AnatomySetVisibilityRequestEvent>(OnSetVisibility);
        SubscribeAllEvent<AnatomySetUndergarmentRequestEvent>(OnSetUndergarment);
    }

    /// <summary>Rate-limit slot for requests that apply to the whole body.</summary>
    protected const int NoRequestSlot = -1;

    /// <summary>
    /// The sender's attached entity, when the kill switch is on, the request passes the rate limit for its type and slot,
    /// and the entity has GenitalsComponent (so it is an eligible adult) and strict master consent.
    /// </summary>
    protected bool TryGetOwnGenitals(ICommonSession session, Type request, int slot, out Entity<GenitalsComponent> ent)
    {
        ent = default;
        return AnatomyEnabled && AllowRequest(session, request, slot) && TryGetOwnBody(session, out ent);
    }

    /// <summary>The sender's attached entity, when the kill switch is on and it has GenitalsComponent and strict master consent.</summary>
    protected bool TryGetOwnBody(ICommonSession session, out Entity<GenitalsComponent> ent)
    {
        ent = default;
        if (!AnatomyEnabled
            || session.AttachedEntity is not { } uid
            || !TryComp<GenitalsComponent>(uid, out var genitals)
            || !_consent.HasMaster(uid))
            return false;

        ent = (uid, genitals);
        return true;
    }

    private void OnSetArousal(AnatomySetArousalRequestEvent ev, EntitySessionEventArgs args)
    {
        HandleArousalRequest(args.SenderSession, ev.Value);
    }

    /// <summary>Applies an arousal request under the rate limit. The server override keeps a refused value instead of dropping it.</summary>
    protected virtual void HandleArousalRequest(ICommonSession session, byte value)
    {
        if (AnatomyEnabled && AllowRequest(session, typeof(AnatomySetArousalRequestEvent), NoRequestSlot))
            SetOwnArousal(session, value);
    }

    /// <summary>Sets the sender's own arousal (TryGetOwnBody), clamped to 0..100; refused while dead or with nothing arousable.</summary>
    protected void SetOwnArousal(ICommonSession session, byte value)
    {
        if (TryGetOwnBody(session, out var ent))
            _arousal.SetArousal(ent, Math.Min(value, SharedArousalSystem.MaxArousal));
    }

    private void OnSetRevealMode(AnatomySetRevealModeRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetOwnGenitals(args.SenderSession, typeof(AnatomySetRevealModeRequestEvent), NoRequestSlot, out var ent)
            || !Enum.IsDefined(ev.Mode)
            || ent.Comp.RevealMode == ev.Mode)
            return;

        ent.Comp.RevealMode = ev.Mode;
        DirtyField(ent.Owner, ent.Comp, nameof(GenitalsComponent.RevealMode));
        RaiseAnatomyVisualsChanged(ent.Owner);
    }

    /// <summary>Ignored for a slot without an organ and for the womb, which has no visibility.</summary>
    private void OnSetVisibility(AnatomySetVisibilityRequestEvent ev, EntitySessionEventArgs args)
    {
        // The slot keys the rate limit, so an undefined one is refused before it can add an entry.
        if (!Enum.IsDefined(ev.Slot)
            || !TryGetOwnGenitals(args.SenderSession, typeof(AnatomySetVisibilityRequestEvent), (int) ev.Slot, out var ent)
            || !Enum.IsDefined(ev.Visibility)
            || !HasVisibilityOrgan(ent.Comp, ev.Slot)
            || ent.Comp.Visibility.Get(ev.Slot) == ev.Visibility)
            return;

        ent.Comp.Visibility = ent.Comp.Visibility.With(ev.Slot, ev.Visibility);
        DirtyField(ent.Owner, ent.Comp, nameof(GenitalsComponent.Visibility));
        RaiseAnatomyVisualsChanged(ent.Owner);

        if (ev.Visibility == GenitalVisibility.ShowThroughClothing)
        {
            var slotName = ev.Slot.ToString().ToLowerInvariant();
            _adminLog.Add(LogType.WFAnatomy,
                LogImpact.Low,
                $"{ToPrettyString(ent.Owner):player} set their {slotName} to show through clothing");
        }
    }

    /// <summary>
    /// Removes or puts back one of the owner's undergarments; ignored without a marking of that category. A put-back also
    /// clears the removal by another player and starts the strip cooldown.
    /// </summary>
    private void OnSetUndergarment(AnatomySetUndergarmentRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!Enum.IsDefined(ev.Slot)
            || !TryGetOwnGenitals(args.SenderSession, typeof(AnatomySetUndergarmentRequestEvent), (int) ev.Slot, out var ent)
            || !HasUndergarmentMarking(ent.Owner, ev.Slot))
            return;

        var removed = UndergarmentSlots.RemovedFlag(ev.Slot);
        var flags = ent.Comp.Undergarments;

        if (ev.Worn)
        {
            if ((flags & removed) == 0)
                return;

            ent.Comp.Undergarments = flags & ~UndergarmentSlots.SlotBits(ev.Slot);
            ent.Comp.StripCooldownUntil = Timing.CurTime + TimeSpan.FromSeconds(Settings.StripCooldown);
            DirtyField(ent.Owner, ent.Comp, nameof(GenitalsComponent.StripCooldownUntil));
        }
        else
        {
            if ((flags & removed) != 0)
                return;

            ent.Comp.Undergarments = flags | removed;
        }

        DirtyField(ent.Owner, ent.Comp, nameof(GenitalsComponent.Undergarments));
        RaiseAnatomyVisualsChanged(ent.Owner);

        if (!ev.Worn)
        {
            var slotName = ev.Slot.ToString().ToLowerInvariant();
            _adminLog.Add(LogType.WFAnatomy,
                LogImpact.Low,
                $"{ToPrettyString(ent.Owner):player} removed their {slotName} undergarment");
        }
    }

    /// <summary>Lets a predicting client refresh the sprite and the panel at once.</summary>
    private void RaiseAnatomyVisualsChanged(EntityUid body)
    {
        var ev = new GenitalsVisualsChangedEvent();
        RaiseLocalEvent(body, ref ev);
    }

    /// <summary>An organ that has a visibility setting occupies the slot; the womb never does.</summary>
    private static bool HasVisibilityOrgan(GenitalsComponent genitals, GenitalSlot slot)
    {
        return slot != GenitalSlot.Womb && genitals.HasOrgan(slot);
    }

    /// <summary>The body wears a visible marking of the slot's undergarment category.</summary>
    private bool HasUndergarmentMarking(EntityUid uid, UndergarmentSlot slot)
    {
        return TryComp<HumanoidAppearanceComponent>(uid, out var humanoid)
               && GenitalCoverageSystem.HasVisibleUndergarment(humanoid, slot);
    }
}

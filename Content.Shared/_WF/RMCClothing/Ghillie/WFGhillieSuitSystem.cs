using Content.Shared._WF.RMCCore.Stealth;
using Content.Shared.Actions;
using Content.Shared.Buckle.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.DoAfter;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._WF.RMCClothing.Ghillie;

/// <summary>
/// The ghillie suit's prepare position action, after RMC-14: a do-after, then RMC stealth on the wearer until
/// the cover breaks.
/// </summary>
public sealed partial class WFGhillieSuitSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFGhillieSuitComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<WFGhillieSuitComponent, WFGhilliePreparePositionEvent>(OnPreparePosition);
        SubscribeLocalEvent<WFGhillieSuitComponent, WFGhillieSuitDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<WFGhillieSuitComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<WFGhillieSuitComponent, InventoryRelayedEvent<SelfBeforeGunShotEvent>>(OnShoot);

        SubscribeLocalEvent<WFGhillieHiddenComponent, MoveInputEvent>(OnMoveInput);
        SubscribeLocalEvent<WFGhillieHiddenComponent, BuckledEvent>(OnBuckled);
        SubscribeLocalEvent<WFGhillieHiddenComponent, PullStartedMessage>(OnPulled);
        SubscribeLocalEvent<WFGhillieHiddenComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnGetActions(Entity<WFGhillieSuitComponent> ent, ref GetItemActionsEvent args)
    {
        if (!TryComp<ClothingComponent>(ent, out var clothing) || args.SlotFlags is not { } slot ||
            (clothing.Slots & slot) == 0)
            return;

        args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnPreparePosition(Entity<WFGhillieSuitComponent> ent, ref WFGhilliePreparePositionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var user = args.Performer;

        if (ent.Comp.Enabled)
        {
            SetHidden(ent, user, false);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, ent.Comp.UseDelay, new WFGhillieSuitDoAfterEvent(), ent, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            CancelDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameTool,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
        {
            _popup.PopupPredicted(Loc.GetString("wf-ghillie-activate-self"),
                Loc.GetString("wf-ghillie-activate-others", ("user", user)), user, user, PopupType.Medium);
        }
    }

    private void OnDoAfter(Entity<WFGhillieSuitComponent> ent, ref WFGhillieSuitDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        // The suit may have come off during the do-after without the wearer moving.
        if (IsWorn(ent, args.User))
            SetHidden(ent, args.User, true);
    }

    /// <summary>Whether the item is worn by the user in one of its clothing slots.</summary>
    private bool IsWorn(EntityUid item, EntityUid user)
    {
        return TryComp<ClothingComponent>(item, out var clothing) &&
               _inventory.TryGetContainingSlot(item, out var slot) &&
               Transform(item).ParentUid == user &&
               (clothing.Slots & slot.SlotFlags) != 0;
    }

    private void OnUnequipped(Entity<WFGhillieSuitComponent> ent, ref GotUnequippedEvent args)
    {
        SetHidden(ent, args.Equipee, false);
    }

    private void OnShoot(Entity<WFGhillieSuitComponent> ent, ref InventoryRelayedEvent<SelfBeforeGunShotEvent> args)
    {
        var user = args.Args.Shooter;
        if (!ent.Comp.Enabled ||
            !TryComp<EntityActiveInvisibleComponent>(user, out var invisible) ||
            !TryComp<RMCPassiveStealthComponent>(user, out var passive))
            return;

        // Each shot adds a step of visibility, which then fades back out over the break delay.
        invisible.Opacity = Math.Min(1f, invisible.Opacity + ent.Comp.AddedOpacityOnShoot);
        Dirty(user, invisible);

        passive.MaxOpacity = invisible.Opacity;
        passive.Delay = ent.Comp.InvisibilityBreakDelay;
        passive.ToggleTime = _timing.CurTime;
        Dirty(user, passive);
    }

    private void OnMoveInput(Entity<WFGhillieHiddenComponent> ent, ref MoveInputEvent args)
    {
        if (args.HasDirectionalMovement)
            Reveal(ent);
    }

    private void OnBuckled(Entity<WFGhillieHiddenComponent> ent, ref BuckledEvent args)
    {
        Reveal(ent);
    }

    private void OnPulled(Entity<WFGhillieHiddenComponent> ent, ref PullStartedMessage args)
    {
        if (args.PulledUid == ent.Owner)
            Reveal(ent);
    }

    private void OnMobStateChanged(Entity<WFGhillieHiddenComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Reveal(ent);
    }

    private void Reveal(Entity<WFGhillieHiddenComponent> ent)
    {
        if (TryComp<WFGhillieSuitComponent>(ent.Comp.Suit, out var suit))
            SetHidden((ent.Comp.Suit.Value, suit), ent, false);
    }

    /// <summary>Hides or reveals the wearer. The server owns the stealth components; clients get them by state.</summary>
    public void SetHidden(Entity<WFGhillieSuitComponent> ent, EntityUid user, bool hidden)
    {
        if (ent.Comp.Enabled == hidden || _net.IsClient)
            return;

        if (hidden)
        {
            var current = CompOrNull<WFGhillieHiddenComponent>(user);

            // Another stealth source owns the stealth components; leave it alone.
            if (current == null &&
                (HasComp<RMCPassiveStealthComponent>(user) || HasComp<EntityActiveInvisibleComponent>(user)))
            {
                _popup.PopupEntity(Loc.GetString("wf-ghillie-already-hidden"), user, user);
                return;
            }

            // A suit and a cloak share one hidden state; the newer item takes it over.
            if (current?.Suit is { } other && other != ent.Owner &&
                TryComp<WFGhillieSuitComponent>(other, out var otherSuit))
            {
                otherSuit.Enabled = false;
                Dirty(other, otherSuit);
            }

            ent.Comp.Enabled = true;
            Dirty(ent);

            var addedTurnInvisible = current?.AddedTurnInvisible ?? !HasComp<EntityTurnInvisibleComponent>(user);
            var turnInvisible = EnsureComp<EntityTurnInvisibleComponent>(user);
            turnInvisible.Enabled = true;
            turnInvisible.UncloakTime = _timing.CurTime;
            Dirty(user, turnInvisible);

            // Fade from fully visible to the suit's opacity over the invisibility delay.
            var passive = EnsureComp<RMCPassiveStealthComponent>(user);
            passive.MaxOpacity = 1f;
            passive.MinOpacity = ent.Comp.Opacity;
            passive.Delay = ent.Comp.InvisibilityDelay;
            passive.Enabled = true;
            passive.ToggleTime = _timing.CurTime;
            Dirty(user, passive);

            var invisible = EnsureComp<EntityActiveInvisibleComponent>(user);
            invisible.Opacity = 1f;
            invisible.DisableMobCollision = true;
            Dirty(user, invisible);

            var marker = EnsureComp<WFGhillieHiddenComponent>(user);
            marker.Suit = ent;
            marker.AddedTurnInvisible = addedTurnInvisible;
            Dirty(user, marker);
            return;
        }

        ent.Comp.Enabled = false;
        Dirty(ent);

        // Only the hidden state this suit set up is torn down.
        if (!TryComp<WFGhillieHiddenComponent>(user, out var hiddenMarker) || hiddenMarker.Suit != ent.Owner)
            return;

        var removeTurnInvisible = hiddenMarker.AddedTurnInvisible;
        RemComp<WFGhillieHiddenComponent>(user);
        RemComp<RMCPassiveStealthComponent>(user);
        RemComp<EntityActiveInvisibleComponent>(user);
        if (removeTurnInvisible)
            RemComp<EntityTurnInvisibleComponent>(user);

        _popup.PopupEntity(Loc.GetString("wf-ghillie-fail-self"), user, user, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("wf-ghillie-fail-others", ("user", user)), user,
            Filter.PvsExcept(user), true, PopupType.Medium);
    }
}

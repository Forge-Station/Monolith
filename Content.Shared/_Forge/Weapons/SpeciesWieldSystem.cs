using System.Linq;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;

namespace Content.Shared._Forge.Weapons;

public sealed class SpeciesWieldSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedWieldableSystem _wieldable = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    private readonly Dictionary<EntityUid, EntityUid> _pendingDrops = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SpeciesWieldComponent, GettingPickedUpAttemptEvent>(OnPickupAttempt);
        SubscribeLocalEvent<SpeciesWieldComponent, GotEquippedHandEvent>(OnEquippedHand);
        SubscribeLocalEvent<SpeciesWieldComponent, GotUnequippedHandEvent>(OnUnequippedHand);
        SubscribeLocalEvent<SpeciesWieldComponent, UseInHandEvent>(OnUseInHand, before: [typeof(SharedWieldableSystem)]);
        SubscribeLocalEvent<SpeciesWieldComponent, ItemUnwieldedEvent>(OnUnwielded);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingDrops.Count > 0)
        {
            foreach (var (item, user) in _pendingDrops)
            {
                if (Deleted(item) ||
                    !user.IsValid() ||
                    !TryComp<HandsComponent>(user, out var handsComponent) ||
                    !_hands.IsHolding(user, item, out _, handsComponent))
                    continue;

                _hands.TryDrop(user, item);
            }

            _pendingDrops.Clear();
        }

        var query = EntityQueryEnumerator<SpeciesWieldComponent, WieldableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var speciesOneHandedComponent, out var wieldableComponent, out var transformComponent))
        {
            var user = transformComponent.ParentUid;
            if (!user.IsValid() ||
                !TryComp<HumanoidAppearanceComponent>(user, out var humanoidAppearanceComponent) ||
                !TryComp<HandsComponent>(user, out var handsComponent) ||
                !_hands.IsHolding(user, uid, out _, handsComponent))
                continue;

            if (speciesOneHandedComponent.ExemptSpecies.Contains(humanoidAppearanceComponent.Species))
            {
                if (wieldableComponent.Wielded)
                    _wieldable.TryUnwield(uid, wieldableComponent, user);
                continue;
            }

            if (wieldableComponent.Wielded)
                continue;

            if (handsComponent.Hands.Values.Any(hand => hand.HeldEntity == null))
                _wieldable.TryWield(uid, wieldableComponent, user);
            else
                _hands.TryDrop(user, uid);
        }
    }

    private void OnPickupAttempt(EntityUid uid, SpeciesWieldComponent speciesOneHandedComponent, GettingPickedUpAttemptEvent args)
    {
        if (args.Cancelled ||
            !TryComp<HumanoidAppearanceComponent>(args.User, out var humanoidAppearanceComponent) ||
            !TryComp<HandsComponent>(args.User, out var handsComponent) ||
            speciesOneHandedComponent.ExemptSpecies.Contains(humanoidAppearanceComponent.Species) ||
            handsComponent.Hands.Values.Count(hand => hand.HeldEntity == null) >= 2)
            return;

        _popup.PopupClient("Этот предмет требует две руки! Освободите обе руки.", args.User, args.User);
        args.Cancel();
    }

    private void OnUnequippedHand(EntityUid uid, SpeciesWieldComponent speciesOneHandedComponent, GotUnequippedHandEvent args)
    {
        if (TryComp<WieldableComponent>(uid, out var wieldable) && wieldable.Wielded)
            _wieldable.TryUnwield(uid, wieldable, args.User);
    }

    private void OnUseInHand(EntityUid uid, SpeciesWieldComponent speciesOneHandedComponent, UseInHandEvent args)
    {
        if (!TryComp<WieldableComponent>(uid, out _))
            return;

        args.Handled = true;
    }

    private void OnUnwielded(EntityUid uid, SpeciesWieldComponent speciesOneHandedComponent, ItemUnwieldedEvent args)
    {
        if (!args.User.IsValid())
            return;

        _pendingDrops[uid] = args.User;
    }

    private void OnEquippedHand(EntityUid uid, SpeciesWieldComponent speciesOneHandedComponent, GotEquippedHandEvent args)
    {
        if (!TryComp<HumanoidAppearanceComponent>(args.User, out var humanoidAppearanceComponent))
            return;

        if (!speciesOneHandedComponent.ExemptSpecies.Contains(humanoidAppearanceComponent.Species)
            && TryComp<HandsComponent>(args.User, out var handsComponent))
            _hands.SetActiveHand(args.User, args.Hand, handsComponent);
    }
}

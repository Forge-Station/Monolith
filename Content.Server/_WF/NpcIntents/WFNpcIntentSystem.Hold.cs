using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server.Cargo.Systems;
using Content.Server.Stack;
using Content.Shared._WF.NpcIntents;
using Content.Shared.Cargo.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.NpcIntents;

public sealed partial class WFNpcIntentSystem
{
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private StackSystem _stack = default!;

    /// <summary>Tiles round the holder that thrown-down cash counts from.</summary>
    private const float DropRange = 1.8f;

    /// <summary>How far a warning shot lands to the side of its target.</summary>
    private const float MissBy = 3f;

    private readonly HashSet<Entity<CashComponent>> _cash = new();

    private void TickDemand(Entity<WFNpcIntentComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        var target = comp.Target!.Value;
        var set = _proto.Index(comp.Set);

        if (IsDowned(target))
        {
            Spare(ent, target);
            Reset(ent);
            return;
        }

        var here = _transform.GetMapCoordinates(ent);
        var there = _transform.GetMapCoordinates(target);
        var near = there.MapId == here.MapId ? Vector2.Distance(here.Position, there.Position) : float.MaxValue;

        // They ran, drew on him, or sat on it too long.
        if (near > set.RunRange || now >= comp.PhaseEnd || Armed(target))
        {
            Refuse(ent);
            return;
        }

        if (near > 0.1f)
            _rotate.TryFaceCoordinates(ent, there.Position);

        if (!comp.Follower && PickUpDropped(ent))
            Settle(ent);
    }

    /// <summary>Whether a player holds a gun with their weapon hand up.</summary>
    private bool Armed(EntityUid target) => _combat.IsInCombatMode(target) && _gun.TryGetGun(target, out _);

    /// <summary>Takes thrown-down cash near the holder toward what is owed.</summary>
    private bool PickUpDropped(Entity<WFNpcIntentComponent> ent)
    {
        _cash.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, DropRange, _cash);

        var owed = ent.Comp.Demand - ent.Comp.Paid;
        var found = 0;
        foreach (var cash in _cash)
        {
            if (_container.IsEntityInContainer(cash) || !TryComp(cash, out StackComponent? stack) || stack.StackTypeId != "Credit")
                continue;

            found += stack.Count;
        }

        if (found < owed)
            return false;

        foreach (var cash in _cash)
        {
            if (owed <= 0)
                break;

            if (_container.IsEntityInContainer(cash) || !TryComp(cash, out StackComponent? stack) || stack.StackTypeId != "Credit")
                continue;

            var take = Math.Min(stack.Count, owed);
            _stack.Use(cash, take, stack);
            owed -= take;
        }

        Receive(ent, ent.Comp.Demand - ent.Comp.Paid);
        ent.Comp.Paid = ent.Comp.Demand;
        return true;
    }

    /// <summary>Puts thalers paid to the NPC in its pockets or hands.</summary>
    private void Receive(Entity<WFNpcIntentComponent> ent, int amount)
    {
        if (amount <= 0)
            return;

        foreach (var cash in _stack.SpawnMultiple("SpaceCash", amount, Transform(ent).Coordinates))
        {
            Stow(ent, cash);
            ent.Comp.Took.Add(cash);
        }

        ent.Comp.TookCash += amount;
    }

    /// <summary>The demand is met: the whole gang lowers its guns, thanks the player and walks off.</summary>
    public void Settle(Entity<WFNpcIntentComponent> ent)
    {
        var set = _proto.Index(ent.Comp.Set);
        var target = ent.Comp.Target;
        Say(ent, set.PaidLines);

        var query = EntityQueryEnumerator<WFNpcIntentComponent>();
        while (query.MoveNext(out var uid, out var other))
        {
            if (other.Target != target || other.Phase != WFNpcIntentPhase.Demanding)
                continue;

            if (target is { } mark)
                Spare((uid, other), mark);

            Leave((uid, other));
        }
    }

    private void OnInteractUsing(Entity<WFNpcIntentComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Phase != WFNpcIntentPhase.Demanding || args.User != ent.Comp.Target)
            return;

        args.Handled = true;
        var set = _proto.Index(ent.Comp.Set);
        var owed = ent.Comp.Demand - ent.Comp.Paid;

        if (HasComp<CashComponent>(args.Used) && TryComp(args.Used, out StackComponent? stack) && stack.StackTypeId == "Credit")
        {
            var give = Math.Min(stack.Count, owed);
            _stack.Use(args.Used, give, stack);
            ent.Comp.Paid += give;
            Receive(ent, give);
        }
        else if (_pricing.GetPrice(args.Used) >= owed)
        {
            _container.TryRemoveFromContainer(args.Used);
            Stow(ent, args.Used);
            ent.Comp.Took.Add(args.Used);
            ent.Comp.Paid = ent.Comp.Demand;
        }

        if (ent.Comp.Paid >= ent.Comp.Demand)
            Settle(ent);
        else
            Say(ent, set.ShortLines, ("amount", ent.Comp.Demand - ent.Comp.Paid));
    }

    private void OnVerbs(Entity<WFNpcIntentComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (ent.Comp.Phase != WFNpcIntentPhase.Demanding || args.User != ent.Comp.Target || !args.CanAccess || !args.CanInteract)
            return;

        var owed = ent.Comp.Demand - ent.Comp.Paid;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("wf-npc-intent-verb-pay", ("amount", owed)),
            Act = () => PayFromBank(ent, owed),
        });
    }

    /// <summary>Pays what is owed from the held-up player's bank account.</summary>
    public bool PayFromBank(Entity<WFNpcIntentComponent> ent, int amount)
    {
        if (ent.Comp.Phase != WFNpcIntentPhase.Demanding || ent.Comp.Target is not { } payer)
            return false;

        if (!_bank.TryBankWithdraw(payer, amount))
        {
            Say(ent, _proto.Index(ent.Comp.Set).ShortLines, ("amount", amount));
            return false;
        }

        ent.Comp.Paid += amount;
        Receive(ent, amount);
        if (ent.Comp.Paid >= ent.Comp.Demand)
            Settle(ent);

        return true;
    }

    private void TickWarning(Entity<WFNpcIntentComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        var target = comp.Target!.Value;
        var set = _proto.Index(comp.Set);

        if (IsDowned(target))
        {
            Down(ent, target);
            return;
        }

        var here = _transform.GetMapCoordinates(ent);
        var there = _transform.GetMapCoordinates(target);
        var near = there.MapId == here.MapId ? Vector2.Distance(here.Position, there.Position) : float.MaxValue;

        if (near > set.WarnRange)
        {
            Say(ent, set.WarnOffLines);
            Spare(ent, target);
            Leave(ent);
            return;
        }

        if (near > 0.1f)
            _rotate.TryFaceCoordinates(ent, there.Position);

        if (!comp.SecondShot && now >= comp.PhaseEnd - TimeSpan.FromSeconds(set.WarnSeconds / 2f))
        {
            comp.SecondShot = true;
            FireWarning(ent, target);
        }

        if (now < comp.PhaseEnd)
            return;

        // They stayed: drive them out.
        ReleaseHold(ent);
        _factions.UnignoreEntity(ent.Owner, target);
        _factions.AggroEntity(ent.Owner, target);
        comp.Phase = WFNpcIntentPhase.Fighting;
        comp.LastSeen = now;
    }

    /// <summary>Fires a held gun at a spot beside the target, to be seen and not to hit.</summary>
    private void FireWarning(Entity<WFNpcIntentComponent> ent, EntityUid target)
    {
        if (!_gun.TryGetGun(ent, out var gun))
            return;

        var here = _transform.GetMapCoordinates(ent);
        var there = _transform.GetMapCoordinates(target);
        if (there.MapId != here.MapId || Transform(ent).MapUid is not { } map)
            return;

        var line = there.Position - here.Position;
        if (line.LengthSquared() < 0.01f)
            return;

        var aside = Vector2.Normalize(new Vector2(-line.Y, line.X)) * (_random.Prob(0.5f) ? MissBy : -MissBy);
        _gun.AttemptShoot(ent, gun, gun.Comp, new EntityCoordinates(map, there.Position + aside));
    }
}

using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.CombatMode;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Power.Components;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Crew who take someone alive. Each step reads the target's state, not a saved stage, so a pull that breaks or cuffs
/// slipped mid-drag put the crewman back on the right step by themselves.
/// </summary>
public sealed partial class WFCrewGrabberSystem : EntitySystem
{
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedCuffableSystem _cuffable = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCrewWeaponSystem _weapons = default!;
    [Dependency] private WFCrewWorkSystem _work = default!;

    /// <summary>How close a grabber must stand to act.</summary>
    private const float GrabRange = 1.5f;

    /// <summary>Swings before a grabber tries the cuffs on someone still standing.</summary>
    private const int SwingsBeforeCuffing = 8;

    private static readonly TimeSpan BuckleRetry = TimeSpan.FromSeconds(1);

    /// <summary>Handcuffs take about this long, with a margin; stunned targets go quicker.</summary>
    private static readonly TimeSpan CuffRetry = TimeSpan.FromSeconds(4);

    private static readonly string[] CuffSlots = { "pocket1", "pocket2" };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewNonLethalComponent, MeleeAttackEvent>(OnMeleeAttack);
    }

    /// <summary>Their batons never run flat, wielded by them: one looted off a body is an ordinary baton.</summary>
    private void OnMeleeAttack(Entity<WFCrewNonLethalComponent> ent, ref MeleeAttackEvent args)
    {
        if (!TryComp<BatteryComponent>(args.Weapon, out var battery))
            return;

        _battery.SetCharge(args.Weapon, battery.MaxCharge, battery);
        _toggle.TryActivate(args.Weapon, predicted: false);
    }

    /// <summary>Sends a crewman after someone.</summary>
    public void Order(EntityUid mob, EntityUid target, EntityUid chair)
    {
        var grabber = EnsureComp<WFCrewGrabberComponent>(mob);
        grabber.Target = target;
        grabber.Chair = chair;
        grabber.Swings = 0;
        grabber.NextTry = TimeSpan.Zero;
    }

    /// <summary>Calls a crewman off: lets go of anyone he drags and puts his baton away.</summary>
    public void StandDown(EntityUid mob)
    {
        if (!TryComp<WFCrewGrabberComponent>(mob, out var grabber))
            return;

        if (TryComp<PullableComponent>(grabber.Target, out var pullable) && pullable.Puller == mob)
            _pulling.TryStopPull(grabber.Target, pullable, mob);

        RemComp<WFCrewGrabberComponent>(mob);
        _combat.SetInCombatMode(mob, false);
        _weapons.TryHolster(mob);
        PocketCuffs(mob);
    }

    public bool IsCuffed(EntityUid target)
    {
        return TryComp<CuffableComponent>(target, out var cuffs) && cuffs.CuffedHandCount > 0;
    }

    /// <summary>The next place to walk to: the target, or with the target in tow the chair.</summary>
    public bool Destination(EntityUid mob, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!TryComp<WFCrewGrabberComponent>(mob, out var grabber) || !Valid(mob, grabber))
            return false;

        var goal = Drags(mob, grabber) ? grabber.Chair : grabber.Target;
        coordinates = _work.Waypoint(mob, Transform(goal).Coordinates, GrabRange);
        return true;
    }

    /// <summary>One look at the job from where he stands. True when he must walk again before he can do more.</summary>
    public bool Perform(EntityUid mob)
    {
        if (!TryComp<WFCrewGrabberComponent>(mob, out var grabber) || !Valid(mob, grabber))
            return true;

        // Done, or a mate has it in hand: nothing left for this one.
        var target = grabber.Target;
        if (CompOrNull<BuckleComponent>(target)?.BuckledTo == grabber.Chair
            || CompOrNull<PullableComponent>(target)?.Puller is { } puller && puller != mob)
        {
            StandDown(mob);
            return true;
        }

        if (Drags(mob, grabber))
            return Haul(mob, grabber);

        if (!_interaction.InRangeUnobstructed(mob, target, GrabRange))
            return true;

        if (IsCuffed(target))
        {
            _weapons.TryHolster(mob);
            _pulling.TryStartPull(mob, target);
            return true;
        }

        if (IsDown(target) || grabber.Swings >= SwingsBeforeCuffing)
            Cuff(mob, grabber);
        else
            Swing(mob, grabber);

        return false;
    }

    private bool Valid(EntityUid mob, WFCrewGrabberComponent grabber)
    {
        return _mobs.IsAlive(mob) && !TerminatingOrDeleted(grabber.Target) && !TerminatingOrDeleted(grabber.Chair)
               && !_mobs.IsDead(grabber.Target);
    }

    private bool Drags(EntityUid mob, WFCrewGrabberComponent grabber)
    {
        return CompOrNull<PullableComponent>(grabber.Target)?.Puller == mob;
    }

    private bool IsDown(EntityUid target)
    {
        return HasComp<StunnedComponent>(target) || CompOrNull<StaminaComponent>(target)?.Critical == true || _mobs.IsCritical(target);
    }

    /// <summary>At the chair: lets go and straps the prisoner in.</summary>
    private bool Haul(EntityUid mob, WFCrewGrabberComponent grabber)
    {
        if (!_transform.InRange(Transform(mob).Coordinates, Transform(grabber.Chair).Coordinates, GrabRange)
            || !_interaction.InRangeUnobstructed(mob, grabber.Chair, GrabRange))
            return true;

        if (_timing.CurTime < grabber.NextTry)
            return false;

        grabber.NextTry = _timing.CurTime + BuckleRetry;
        if (TryComp<PullableComponent>(grabber.Target, out var pullable))
            _pulling.TryStopPull(grabber.Target, pullable, mob);

        // The prisoner trails a pace behind and the chair may be out of their reach: they are sat down on it, or
        // he'd let go and take them up again for good. Failing that, the next look starts the pull over.
        _transform.SetCoordinates(grabber.Target, Transform(grabber.Chair).Coordinates);
        if (_buckle.TryBuckle(grabber.Target, mob, grabber.Chair, popup: false))
            StandDown(mob);

        return true;
    }

    private void Swing(EntityUid mob, WFCrewGrabberComponent grabber)
    {
        if (!_weapons.TryDraw(mob) || _hands.GetActiveItem(mob) is not { } held || !TryComp<MeleeWeaponComponent>(held, out var melee))
            return;

        _combat.SetInCombatMode(mob, true);
        // A swing still on its cooldown returns false and doesn't count.
        if (_melee.AttemptLightAttack(mob, held, melee, grabber.Target))
            grabber.Swings++;
    }

    private void Cuff(EntityUid mob, WFCrewGrabberComponent grabber)
    {
        if (_timing.CurTime < grabber.NextTry)
            return;

        grabber.NextTry = _timing.CurTime + CuffRetry;
        if (TakeCuffs(mob) is { } cuffs && _hands.TrySelect(mob, cuffs))
            _cuffable.TryCuffing(mob, grabber.Target, cuffs);
    }

    /// <summary>Cuffs he took out and didn't use go back in a free pocket, leaving his hands free for his other work.</summary>
    private void PocketCuffs(EntityUid mob)
    {
        foreach (var held in _hands.EnumerateHeld(mob).ToArray())
        {
            if (!HasComp<HandcuffComponent>(held))
                continue;

            foreach (var slot in CuffSlots)
            {
                if (_inventory.TryGetSlotEntity(mob, slot, out _) || !_hands.TryDrop(mob, held, checkActionBlocker: false))
                    continue;

                _inventory.TryEquip(mob, held, slot, silent: true, force: true);
                break;
            }
        }
    }

    /// <summary>A pair of cuffs in hand, out of a pocket if need be. The baton goes back to make room.</summary>
    private EntityUid? TakeCuffs(EntityUid mob)
    {
        foreach (var held in _hands.EnumerateHeld(mob))
        {
            if (HasComp<HandcuffComponent>(held))
                return held;
        }

        foreach (var slot in CuffSlots)
        {
            if (!_inventory.TryGetSlotEntity(mob, slot, out var item) || !HasComp<HandcuffComponent>(item))
                continue;

            // Pockets don't hand things straight over as a belt does.
            if (!_inventory.TryUnequip(mob, slot, silent: true, force: true))
                continue;

            _weapons.TryHolster(mob);
            if (_hands.TryPickupAnyHand(mob, item.Value, checkActionBlocker: false, animateUser: false, animate: false))
                return item;

            _inventory.TryEquip(mob, item.Value, slot, silent: true, force: true);
        }

        return null;
    }
}

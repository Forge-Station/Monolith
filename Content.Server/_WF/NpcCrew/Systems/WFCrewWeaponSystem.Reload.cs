using Content.Server._WF.NpcCrew.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Robust.Shared.Player;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Mobs.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared._WF.NpcCrew;
using System.Linq;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Reloads from real equipment slots and falls back to another weapon when ammunition is exhausted.</summary>
public sealed partial class WFCrewWeaponSystem
{
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private Content.Server.Atmos.EntitySystems.AtmosphereSystem _atmos = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private WFCrewEvaSystem _eva = default!;
    [Dependency] private WFCrewStealthSystem _stealth = default!;
    [Dependency] private Robust.Shared.Timing.IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _xformSystem = default!;
    [Dependency] private Robust.Shared.Random.IRobustRandom _random = default!;

    private const string MagazineSlot = "gun_magazine";
    private const float LookupRange = 10f;
    private static readonly TimeSpan ThreatCacheTime = TimeSpan.FromSeconds(0.5);
    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Threat)> _threats = new();

    /// <summary>Faction members found by one lookup, reused so a threat check allocates no set.</summary>
    private readonly HashSet<Entity<NpcFactionMemberComponent>> _nearbyMembers = new();

    /// <summary>Spare candidate lists; a target check may run another crewman's, so each check rents its own.</summary>
    private readonly Stack<List<EntityUid>> _candidateLists = new();
    private float _reloadTimer;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _reloadTimer += frameTime;
        if (_reloadTimer < 0.5f)
            return;
        _reloadTimer = 0;
        foreach (var (mob, cached) in _threats)
        {
            if (_timing.CurTime >= cached.Until)
                _threats.Remove(mob);
        }
        PruneAdvance();
        var query = EntityQueryEnumerator<WFCrewWeaponComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (HasComp<ActorComponent>(uid) || !_mobs.IsAlive(uid))
                continue;
            // Components made before the skill changed keep up; new ones start with it (OnRangedInit).
            if (TryComp<Content.Server.NPC.Components.NPCRangedCombatComponent>(uid, out var ranged) && TryComp<WFCrewComponent>(uid, out var shooter))
                ranged.ShootDelay = WFCrewSkills.Of(shooter.Skill).ShootDelay;
            if (TryComp<HTNComponent>(uid, out var plan)
                && plan.Blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager))
            {
                if (!CanEngage(uid, target))
                {
                    EntityManager.System<HTNSystem>().Replan(plan);
                    EntityManager.System<Content.Server.NPC.Systems.NPCSteeringSystem>().Unregister(uid);
                    plan.Blackboard.Remove<EntityUid>("Target");
                    RemCompDeferred<NPCRangedCombatComponent>(uid);
                    RemCompDeferred<NPCMeleeCombatComponent>(uid);
                }
                else
                    HoldAdvance(uid, target);
            }
            if (HasLiveThreat(uid))
                TryReloadOrSwitch(uid);
            // A grabber's baton is out for someone who isn't a threat; holstering it here also restarts its cooldown for good.
            else if (!HasComp<WFCrewGrabberComponent>(uid))
            {
                RemCompDeferred<NPCRangedCombatComponent>(uid);
                RemCompDeferred<NPCMeleeCombatComponent>(uid);
                TryHolster(uid);
            }
        }
    }

    /// <summary>Only living hostile targets in the crewman's current vision justify drawing or reloading.</summary>
    public bool HasLiveThreat(EntityUid uid)
    {
        var now = _timing.CurTime;
        if (_threats.TryGetValue(uid, out var cached) && now < cached.Until)
            return cached.Threat;
        var threat = PickTarget(uid) != null;
        // Spread over a second, so the crew's checks don't all fall due on the same tick.
        _threats[uid] = (now + ThreatCacheTime * (1 + _random.NextFloat()), threat);
        return threat;
    }

    /// <summary>
    /// Hostiles within normal vision: the set the faction lookup gives, without its allocations. The crew's explicit,
    /// shared and radio targets are walked by <see cref="PickTarget"/> itself.
    /// </summary>
    private void NearbyHostiles(EntityUid uid, NpcFactionMemberComponent member, List<EntityUid> candidates)
    {
        var range = TryComp<HTNComponent>(uid, out var htn)
            && htn.Blackboard.TryGetValue<float>("VisionRadius", out var vision, EntityManager) ? vision : LookupRange;
        // Alert-widened vision would turn the faction lookup into a scan of every faction member on the map.
        var hostileFactions = member.HostileFactions;
        _nearbyMembers.Clear();
        _lookup.GetEntitiesInRange(_xformSystem.GetMapCoordinates(uid), Math.Min(range, LookupRange), _nearbyMembers);
        foreach (var near in _nearbyMembers)
        {
            if (near.Owner != uid && hostileFactions.Overlaps(near.Comp.Factions)
                && !_factions.IsEntityFriendly((uid, member), (near.Owner, near.Comp)) && !_factions.IsIgnored(uid, near.Owner))
                candidates.Add(near.Owner);
        }
    }

    /// <summary>The faction lookup's hostility rule for one known mob.</summary>
    private bool IsHostile(EntityUid uid, NpcFactionMemberComponent? member, EntityUid target)
    {
        if (target == uid || TerminatingOrDeleted(target) || _factions.IsIgnored(uid, target))
            return false;
        if (_factions.GetHostiles(uid).Contains(target))
            return true;
        return member != null && _factions.IsMemberOfAny(target, member.HostileFactions)
            && !_factions.IsEntityFriendly(uid, target);
    }

    /// <summary>Crew defend their assigned ship without pursuing targets across docking connections.</summary>
    public bool CanEngage(EntityUid uid, EntityUid target)
    {
        if (TerminatingOrDeleted(target) || !_mobs.IsAlive(target) || Transform(uid).GridUid is not { } grid)
            return false;

        // Someone it has finished with, or a boarding party's mark that has not hit it (NpcIntents).
        if (_factions.IsIgnored(uid, target) || EntityManager.System<Content.Server._WF.NpcIntents.WFNpcIntentSystem>().HoldsFireOn(uid, target))
            return false;

        if (HasComp<WFCrewNonLethalComponent>(uid) && CompOrNull<Content.Shared.Cuffs.Components.CuffableComponent>(target)?.CuffedHandCount > 0)
            return false;

        TryComp<WFCrewComponent>(uid, out var crew);
        if (OnSight(uid, crew, grid, target))
            return true;
        if (Transform(target).GridUid != grid)
            return false;

        var attacked = WasAttackedBy(uid, target);
        // Off his post's grid a crewman starts nothing. He answers whoever attacked him, in sight, only when sent
        // there on a job or a raid, or when the attacker is his faction's enemy anyway, as for a raider carried off;
        // one who only defends his ship goes home instead of following anyone off it.
        var away = crew?.Post is { } post && post.EntityId != grid;
        if (away && (!attacked || !OnErrand(uid, crew!, grid) && !IsFactionEnemy(uid, target)))
            return false;
        if (IsStationCrew(uid) && !attacked)
            return false;
        if (CanSee(uid, target))
            return true;
        if (away)
            return false;

        // Out of sight. Whoever was shot at goes after the shooter; of the rest one at a time goes looking while
        // the others hold where they are. Nobody leaves good air to follow anyone into a spaced compartment.
        if (!attacked && !EntityManager.System<WFCrewCommsSystem>().Knows(uid, target))
            return false;
        if (crew is { Pursues: false } || HasAir(uid) && !HasAir(target))
            return false;

        // Only a fresh attack sends him straight after someone he cannot see; an old grudge waits its turn in the hunt.
        return RecentlyAttackedBy(uid, target) || AdvanceFree(uid, target);
    }

    /// <summary>How long after an attack a crewman goes after an attacker he cannot see, outside the one-man hunt.</summary>
    private static readonly TimeSpan PursuitWindow = TimeSpan.FromSeconds(10);

    /// <summary>Whether the target struck this crewman himself within the pursuit window; a crewmate's attack does not count.</summary>
    private bool RecentlyAttackedBy(EntityUid uid, EntityUid target)
    {
        return TryComp<WFCrewComponent>(uid, out var crew) && crew.Struck.TryGetValue(target, out var at)
               && _timing.CurTime < at + PursuitWindow;
    }

    /// <summary>
    /// Whether an on-sight crewman opens fire on a faction enemy he sees: anywhere for the hands, only aboard for the
    /// helm, guns and radio, which stay manned against anyone outside.
    /// </summary>
    private bool OnSight(EntityUid uid, WFCrewComponent? crew, EntityUid grid, EntityUid target)
    {
        if (!TryComp<WFCrewOnSightComponent>(crew?.Post?.EntityId ?? grid, out var onSight)
            || Transform(target).GridUid != grid && IsStationCrew(uid)
            || !IsFactionEnemy(uid, target) || !CanSee(uid, target))
            return false;

        var company = WFCompanyFamily.Of(CompOrNull<Content.Shared._Mono.Company.CompanyComponent>(target)?.CompanyName.Id);
        return company.Length == 0 || !onSight.Spares.Any(spared => WFCompanyFamily.Of(spared) == company);
    }

    /// <summary>Whether the target belongs to a faction the crewman's own is hostile to, attack or no attack.</summary>
    private bool IsFactionEnemy(EntityUid uid, EntityUid target)
    {
        return TryComp<NpcFactionMemberComponent>(uid, out var member)
               && _factions.IsMemberOfAny(target, member.HostileFactions) && !_factions.IsEntityFriendly(uid, target);
    }

    /// <summary>Whether a crewman off his post's grid was sent aboard it: a work job, or his crew raiding it.</summary>
    private bool OnErrand(EntityUid uid, WFCrewComponent crew, EntityUid grid)
    {
        if (EntityManager.System<WFCrewWorkSystem>().HasJob(uid))
            return true;

        return crew.Post is { } post
               && EntityManager.System<WFCrewObjectiveSystem>().IsRaiding(post.EntityId, crew.Group, grid);
    }

    /// <summary>The longest one crewman keeps the hunt for an unseen hostile without getting sight of it.</summary>
    private static readonly TimeSpan AdvanceLease = TimeSpan.FromSeconds(15);

    private readonly Dictionary<EntityUid, (EntityUid Crew, TimeSpan Until)> _advancing = new();

    /// <summary>Whether the hunt for a hostile is open to this crewman: nobody holds it, the lease ran out, or it is his.</summary>
    private bool AdvanceFree(EntityUid uid, EntityUid target)
    {
        return !_advancing.TryGetValue(target, out var slot) || slot.Crew == uid || _timing.CurTime >= slot.Until
            || TerminatingOrDeleted(slot.Crew) || !_mobs.IsAlive(slot.Crew);
    }

    /// <summary>
    /// Takes the one place in the hunt for a hostile nobody can see. Only a plan that commits to the target calls
    /// this; an existing lease is never extended here, so a holder who gets nowhere lets go after the lease.
    /// </summary>
    public void ClaimAdvance(EntityUid uid, EntityUid target)
    {
        if (RecentlyAttackedBy(uid, target) || CanSee(uid, target) || !AdvanceFree(uid, target))
            return;

        var now = _timing.CurTime;
        if (_advancing.TryGetValue(target, out var slot) && slot.Crew == uid && now < slot.Until)
            return;

        _advancing[target] = (uid, now + AdvanceLease);
    }

    /// <summary>
    /// A crewman fighting a hostile in sight keeps the hunt for it, so nobody else sets out meanwhile. One who
    /// cannot find a way to an unseen hostile gives the hunt up.
    /// </summary>
    private void HoldAdvance(EntityUid uid, EntityUid target)
    {
        if (!AdvanceFree(uid, target))
            return;

        var now = _timing.CurTime;
        var held = _advancing.TryGetValue(target, out var slot) && slot.Crew == uid && now < slot.Until;
        if (CanSee(uid, target))
        {
            var until = now + AdvanceLease;
            _advancing[target] = (uid, held && slot.Until > until ? slot.Until : until);
        }
        else if (held && (Unreachable(uid)
                     || TryComp<NPCSteeringComponent>(uid, out var steering) && steering.Status == SteeringStatus.NoPath))
            _advancing[target] = (uid, now);
    }

    private bool Unreachable(EntityUid uid)
    {
        return TryComp<NPCRangedCombatComponent>(uid, out var ranged) && ranged.Status == CombatStatus.TargetUnreachable
            || TryComp<NPCMeleeCombatComponent>(uid, out var melee) && melee.Status == CombatStatus.TargetUnreachable;
    }

    /// <summary>Drops leases that ran out and those whose crewman or hostile is gone.</summary>
    private void PruneAdvance()
    {
        var now = _timing.CurTime;
        foreach (var (target, slot) in _advancing)
        {
            if (now >= slot.Until || TerminatingOrDeleted(target) || !_mobs.IsAlive(target)
                || TerminatingOrDeleted(slot.Crew) || !_mobs.IsAlive(slot.Crew))
                _advancing.Remove(target);
        }
    }

    /// <summary>Whether a mob stands where there is air to breathe, or needs none.</summary>
    private bool HasAir(EntityUid mob)
    {
        if (_eva.IsImmune(mob))
            return true;
        var xform = Transform(mob);
        if (xform.GridUid is not { } grid || !TryComp<Robust.Shared.Map.Components.MapGridComponent>(grid, out var map))
            return false;

        var air = _atmos.GetTileMixture(grid, xform.MapUid, _maps.LocalToTile(grid, map, xform.Coordinates));
        return air != null && WFCrewPlannerSystem.IsBreathable(air, air.Pressure);
    }

    /// <summary>Station operators leave their job only to defend themselves against a personal attacker.</summary>
    public bool IsStationCrew(EntityUid uid) => TryComp<WFCrewComponent>(uid, out var crew)
        && (crew.Duty is WFCrewDuties.Pilot or WFCrewDuties.Gunnery
            || crew.Role == WFCrewRoles.Captain || crew.Role == WFCrewRoles.RadioOperator);

    private bool WasAttackedBy(EntityUid uid, EntityUid target) => TryComp<NPCRetaliationComponent>(uid, out var retaliation)
        && retaliation.AttackMemories.Any(memory => memory.Key == target && _timing.CurTime < memory.Value);

    /// <summary>
    /// Walls and closed opaque doors conceal boarders; radio awareness does not extend eyesight. Only what is built
    /// into the ship blocks the view: a crewmate, a body, a crate or a locker in between does not. A stealthed mob that
    /// has done nothing hostile lately is not seen, unless <paramref name="ignoreStealth"/>.
    /// </summary>
    public bool CanSee(EntityUid uid, EntityUid target, bool ignoreStealth = false) => !TerminatingOrDeleted(target)
        && (ignoreStealth || !_stealth.Unseen(target))
        && _interaction.InRangeUnobstructed(uid, target, 10f, Content.Shared.Physics.CollisionGroup.Opaque,
            predicate: blocker => !Transform(blocker).Anchored);

    /// <summary>Chooses a living hostile aboard the crewman's own ship.</summary>
    public EntityUid? PickTarget(EntityUid uid)
    {
        TryComp<NpcFactionMemberComponent>(uid, out var member);
        if (member != null)
        {
            // The lookup's set is shared, so its hits are copied out before any check can run another lookup.
            var nearby = _candidateLists.TryPop(out var spare) ? spare : new List<EntityUid>();
            try
            {
                NearbyHostiles(uid, member, nearby);
                foreach (var target in nearby)
                {
                    if (CanEngage(uid, target))
                        return target;
                }
            }
            finally
            {
                nearby.Clear();
                _candidateLists.Push(nearby);
            }

            foreach (var target in _factions.GetHostiles(uid))
            {
                if (!_factions.IsIgnored(uid, target) && CanEngage(uid, target))
                    return target;
            }
        }

        if (!TryComp<WFCrewComponent>(uid, out var crew) || Transform(uid).GridUid is not { } grid)
            return null;
        foreach (var target in EntityManager.System<WFCrewAlertSystem>().GetSharedHostiles(grid, crew.Group))
        {
            if (IsHostile(uid, member, target) && CanEngage(uid, target))
                return target;
        }
        foreach (var target in crew.RadioSightings.Keys)
        {
            if (IsHostile(uid, member, target) && CanEngage(uid, target))
                return target;
        }
        return null;
    }

    /// <summary>Racks a loaded gun only when its chamber is empty or its bolt is open.</summary>
    private void ReadyChamber(EntityUid user, EntityUid gun)
    {
        if (!TryComp<ChamberMagazineAmmoProviderComponent>(gun, out var chamber))
            return;
        if (chamber.BoltClosed == false)
            _guns.SetBoltClosed(gun, chamber, true, user);
        else if (_slots.TryGetSlot(gun, "gun_chamber", out var slot)
                 && (!slot.HasItem || slot.Item is { } round && TryComp<CartridgeAmmoComponent>(round, out var cartridge) && cartridge.Spent))
        {
            if (chamber.BoltClosed != null)
            {
                _guns.SetBoltClosed(gun, chamber, false, user);
                _guns.SetBoltClosed(gun, chamber, true, user);
                return;
            }
            RaiseLocalEvent(gun, new UseInHandEvent(user));
        }
    }

    /// <summary>Returns the ammunition reported by the item's ordinary provider.</summary>
    public int AmmoCount(EntityUid item)
    {
        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(item, ref ev);
        if (HasComp<ChamberMagazineAmmoProviderComponent>(item)
            && _slots.TryGetSlot(item, "gun_chamber", out var chamber) && chamber.Item is { } round
            && TryComp<CartridgeAmmoComponent>(round, out var cartridge) && cartridge.Spent)
            return Math.Max(0, ev.Count - 1);
        return ev.Count;
    }

    private bool FindMagazineForOwner(EntityUid gun)
    {
        return Transform(gun).ParentUid is var owner && HasComp<WFCrewComponent>(owner)
            && FindMagazine(owner, gun, out _, out _);
    }

    private bool FindMagazine(EntityUid owner, EntityUid gun, out EntityUid magazine, out string equipmentSlot)
    {
        magazine = default;
        equipmentSlot = string.Empty;
        if (!_slots.TryGetSlot(gun, MagazineSlot, out var slot))
            return false;
        foreach (var name in WeaponSlots)
        {
            if (!_inventory.TryGetSlotEntity(owner, name, out var candidate) || candidate == gun
                || AmmoCount(candidate.Value) <= 0 || !_slots.CanInsert(gun, candidate.Value, owner, slot, swap: true))
                continue;
            magazine = candidate.Value;
            equipmentSlot = name;
            return true;
        }
        return false;
    }

    /// <summary>Reloads the held gun or holsters it and selects a usable fallback, leaving empty hands for melee.</summary>
    public bool TryReloadOrSwitch(EntityUid uid)
    {
        if (!TryComp<WFCrewWeaponComponent>(uid, out var weapon) || weapon.Drawn is not { } gun
            || !IsDrawn(uid) || !HasComp<GunComponent>(gun))
            return false;
        if (AmmoCount(gun) > 0)
        {
            ReadyChamber(uid, gun);
            return true;
        }

        if (FindMagazine(uid, gun, out var magazine, out var equipmentSlot)
            && TakeMagazine(uid, magazine, equipmentSlot))
        {
            _hands.TrySelect(uid, magazine);
            if (_slots.TryGetSlot(gun, MagazineSlot, out var slot)
                && (!slot.HasItem || _slots.TryEject(gun, slot, uid, out _)))
            {
                _interaction.InteractUsing(uid, magazine, gun, Transform(uid).Coordinates);
                if (slot.Item == magazine)
                {
                    EntityManager.System<WFCrewSpeechSystem>().Say(uid, "reload");
                    ReadyChamber(uid, gun);
                    _hands.TrySelect(uid, gun);
                    return AmmoCount(gun) > 0;
                }
            }
            _inventory.TryEquip(uid, magazine, equipmentSlot, silent: true);
            _hands.TrySelect(uid, gun);
        }

        RemCompDeferred<NPCRangedCombatComponent>(uid);
        if (TryComp<HTNComponent>(uid, out var htn))
            EntityManager.System<HTNSystem>().Replan(htn);
        EntityManager.System<WFCrewSpeechSystem>().Say(uid, "empty");
        TryHolster(uid);
        TryDraw(uid);
        return false;
    }

    private bool TakeMagazine(EntityUid uid, EntityUid magazine, string slot)
    {
        if (_hands.TryPickupAnyHand(uid, magazine, animateUser: false))
            return true;
        if (!_inventory.TryUnequip(uid, slot, silent: true))
            return false;
        if (_hands.TryPickupAnyHand(uid, magazine, animateUser: false))
            return true;
        _inventory.TryEquip(uid, magazine, slot, silent: true);
        return false;
    }
}

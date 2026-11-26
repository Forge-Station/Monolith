using Content.Server._Forge.Drones;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.NPC.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Stops autonomous crew damage to allies before armor, wounds and retaliation process the hit.</summary>
public sealed partial class WFCrewFriendlyFireSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private WFCrewSecuritySystem _security = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private WFCrewEscortSystem _escorts = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedProjectileSystem _projectiles = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WoundDamageRoutingSystem _wounds = default!;
    [Dependency] private DroneInnerZoneFleeSystem _droneFlee = default!;

    /// <summary>How long one crew command attributes the weapon's automatic burst.</summary>
    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnDamage,
            before: [typeof(WoundDamageRoutingSystem), typeof(InventorySystem), typeof(WFCrewSystem), typeof(WFRadioOperatorSystem)]);
        // DamageModifyEvent on DamageableComponent already belongs to the contract perk system.
        SubscribeLocalEvent<DamageableComponent, WFCrewDamageScaleEvent>(OnDamageModify);
        SubscribeLocalEvent<DamageableComponent, PreventCollideEvent>(OnCollision);
        SubscribeLocalEvent<WFCrewShipFireComponent, ShotAttemptedEvent>(OnShotAttempt);
        SubscribeLocalEvent<WFCrewShipFireComponent, AmmoShotEvent>(OnShot);
    }

    /// <summary>Only autonomous crew commands grant protection; a later manual command clears it.</summary>
    public void TrackWeapon(EntityUid weapon, EntityUid user)
    {
        if (!HasComp<WFCrewComponent>(user) || HasComp<ActorComponent>(user))
        {
            RemComp<WFCrewShipFireComponent>(weapon);
            return;
        }
        var firing = EnsureComp<WFCrewShipFireComponent>(weapon);
        firing.Crew = user;
        firing.Launched = false;
        firing.Until = _timing.CurTime + BurstWindow;
    }

    private void OnShotAttempt(Entity<WFCrewShipFireComponent> ent, ref ShotAttemptedEvent args)
    {
        if (args.User != ent.Owner || !Commands(ent.Comp, ent.Owner))
            RemComp<WFCrewShipFireComponent>(ent);
    }

    /// <summary>A weapon stays attributed only to a living gunner of its grid, at its console, during its own burst.</summary>
    private bool Commands(WFCrewShipFireComponent firing, EntityUid weapon)
    {
        var gunner = firing.Crew;
        return _timing.CurTime <= firing.Until && !TerminatingOrDeleted(gunner) && !HasComp<ActorComponent>(gunner)
            && _mobs.IsAlive(gunner) && TryComp<WFCrewComponent>(gunner, out var crew)
            && _crew.HomeGrid(gunner, crew) is { } home && Transform(weapon).GridUid == home
            && (!TryComp<WFGunnerDutyComponent>(gunner, out var duty) || duty.AtConsole);
    }

    private void OnShot(EntityUid uid, WFCrewShipFireComponent component, AmmoShotEvent args)
    {
        foreach (var round in args.FiredProjectiles)
        {
            if (!TryComp<ProjectileComponent>(round, out var projectile))
                continue;
            var firing = EnsureComp<WFCrewShipFireComponent>(round);
            firing.Crew = component.Crew;
            firing.Launched = true;
            _projectiles.SetShooter(round, projectile, component.Crew);
        }
    }

    private void OnCollision(Entity<DamageableComponent> ent, ref PreventCollideEvent args)
    {
        if (ProtectedShot(args.OtherEntity, ent.Owner)
            || TryComp<ProjectileComponent>(args.OtherEntity, out var projectile) && projectile.Shooter is { } shooter
                && Protected(shooter, ent.Owner))
            args.Cancelled = true;
    }

    private void OnDamage(Entity<DamageableComponent> ent, ref BeforeDamageChangedEvent args)
    {
        _droneFlee.OnFleeGridDamage(ent, ref args);
        if (args.Cancelled || !args.Damage.AnyPositive())
            return;
        if (args.Origin is not { } origin)
        {
            ScaleBySkill(ent, ref args);
            return;
        }
        // Hitscan names its gun as the origin; the wielder is the one who shot.
        var shooter = _crew.Wielder(origin);
        if (args.Tool is { } tool && ProtectedShot(tool, ent.Owner) || Protected(origin, ent.Owner)
            || shooter != origin && Protected(shooter, ent.Owner))
        {
            args.Cancelled = true;
            return;
        }
        ScaleBySkill(ent, ref args);
        if (args.Tool is { } beam && HasComp<HitscanBasicDamageComponent>(beam))
            ReportBeamHit(ent.Owner, origin);
        ReportMechHit(ent.Owner, origin);
    }

    /// <summary>Reports a free-flying mech's hit on a ship or anyone aboard as that ship coming under attack.</summary>
    private void ReportMechHit(EntityUid target, EntityUid origin)
    {
        if (Transform(target).GridUid is not { } grid || _alerts.FreeMechOf(origin) is not { } mech)
            return;
        var ev = new WFCrewHullHitEvent(grid, mech);
        RaiseLocalEvent(grid, ref ev, true);
    }

    /// <summary>
    /// NPC crew are frailer than players and hit softer, by their skill. Applied once per hit, to the body: the
    /// wound system then routes the scaled damage to the parts, and its inner passes are left alone. Victims
    /// without wounds are scaled in <see cref="OnDamageModify"/>, the only place their damage can be changed.
    /// </summary>
    private void ScaleBySkill(Entity<DamageableComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (!HasComp<MobStateComponent>(ent) || !HasComp<WoundHostComponent>(ent) || _wounds.IsRouting(ent))
            return;

        var scale = SkillScale(ent, args.Origin);
        if (scale != 1f)
            args.Damage = args.Damage * scale;
    }

    /// <summary>Scales damage to a mob the wound system does not route, after armor, so crew skill reaches borgs and animals too.</summary>
    private void OnDamageModify(Entity<DamageableComponent> ent, ref WFCrewDamageScaleEvent args)
    {
        if (!HasComp<MobStateComponent>(ent) || HasComp<WoundHostComponent>(ent) || !args.Damage.AnyPositive())
            return;

        var scale = SkillScale(ent, args.Origin);
        if (scale != 1f)
            args.Damage = args.Damage * scale;
    }

    /// <summary>A crew victim's frailty, else a crew attacker's softness against anyone who is not crew.</summary>
    private float SkillScale(EntityUid victim, EntityUid? origin)
    {
        if (TryComp<WFCrewComponent>(victim, out var crew) && !HasComp<ActorComponent>(victim))
            return WFCrewSkills.Of(crew.Skill).DamageTaken;

        if (origin is { } source && !TerminatingOrDeleted(source))
        {
            var attacker = _crew.Wielder(source);
            if (TryComp<WFCrewComponent>(attacker, out var dealer) && !HasComp<ActorComponent>(attacker))
                return WFCrewSkills.Of(dealer.Skill).DamageDealt;
        }

        return 1f;
    }

    /// <summary>Reports ship hitscan damage to a hull the way ship-weapon projectile impacts are reported.</summary>
    private void ReportBeamHit(EntityUid target, EntityUid gun)
    {
        if (!TryComp(target, out TransformComponent? xform) || !xform.Anchored || xform.GridUid is not { } grid
            || !_alerts.IsShipWeapon(gun) || Transform(gun).GridUid is not { } attacker || attacker == grid)
            return;
        var ev = new WFCrewHullHitEvent(grid, attacker);
        RaiseLocalEvent(grid, ref ev, true);
    }

    /// <summary>Friendly-fire protection applies to autonomous crew, including allies outside their own group.</summary>
    public bool Protected(EntityUid shooter, EntityUid target)
    {
        return ProtectedShot(shooter, target)
            || !HasComp<ActorComponent>(shooter) && ProtectedCrew(shooter, target, shipWeapon: false);
    }

    private bool ProtectedShot(EntityUid source, EntityUid target)
    {
        return TryComp<WFCrewShipFireComponent>(source, out var firing)
            && (firing.Launched || !HasComp<ActorComponent>(firing.Crew))
            && ProtectedCrew(firing.Crew, target, shipWeapon: true);
    }

    private bool ProtectedCrew(EntityUid shooter, EntityUid target, bool shipWeapon)
    {
        if (!TryComp<WFCrewComponent>(shooter, out var crew))
            return false;
        if (_crew.SameCrew(shooter, target))
            return true;
        var home = _crew.HomeGrid(shooter, crew);
        // Anyone who attacked this crew loses protection, whatever their company or faction.
        if (Attacked(shooter, crew, home, target))
            return false;
        EntityUid? alliedGrid = null;
        if (shipWeapon && home is { } ship
            && (HasComp<MapGridComponent>(target) ? target : Transform(target).GridUid) is { } grid)
        {
            var mob = HasComp<MobStateComponent>(target);
            if (grid == ship && !mob)
                return true;
            var hostile = _objectives.IsAttackTarget(ship, crew.Group, grid)
                || _alerts.IsHostileShip(ship, crew.Group, grid)
                || _security.IsHostileDockingTarget(ship, crew.Group, grid);
            // Own and formation ships shelter their hulls and everyone aboard who hasn't attacked this crew.
            if (!hostile && (grid == ship || _escorts.AreInFormation(ship, grid)))
                return true;
            if (!mob)
            {
                if (hostile)
                    return false;
                alliedGrid = grid;
            }
        }
        if (_security.IsAuthorized(shooter, target) || _factions.IsEntityFriendly(shooter, target))
            return true;
        return alliedGrid is { } friendlyGrid
            && (_security.IsAuthorized(shooter, friendlyGrid) || _factions.IsEntityFriendly(shooter, friendlyGrid));
    }

    /// <summary>Whether the target attacked this crewman or its crew, or was declared a hostile boarder.</summary>
    private bool Attacked(EntityUid shooter, WFCrewComponent crew, EntityUid? home, EntityUid target)
    {
        if (TryComp<NPCRetaliationComponent>(shooter, out var retaliation))
        {
            var memories = retaliation.AttackMemories;
            if (memories.TryGetValue(target, out var until) && _timing.CurTime < until)
                return true;
        }
        return home is { } grid && _alerts.IsSharedHostile(grid, crew.Group, target)
            || _security.IsHostileVisitor(shooter, target);
    }
}

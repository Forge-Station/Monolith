using System.Linq;
using System.Numerics;
using Content.Server._WF.Wolfmed.Gore;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Administration.Logs;
using Content.Server.Body.Components;
using Content.Server.Stunnable.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Database;
using Content.Shared.Execution;
using Content.Shared.Explosion.Components;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Species.Components;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>What a deliberate use of a weapon is made of.</summary>
public enum WolfmedKillKind : byte
{
    /// <summary>Nothing that kills: no round, a spent casing, rubber, a disabler, a launcher's carrier shell.</summary>
    NonLethal,
    Ballistic,
    Energy,
    Blade,
    Blunt,
}

/// <summary>How much of it there is.</summary>
public enum WolfmedKillTier : byte
{
    Weak,
    Medium,
    Heavy,
}

/// <summary>What one deliberate use of a weapon amounts to: its kind, its tier and the damage they were read from.</summary>
public readonly record struct WolfmedKillStrength(WolfmedKillKind Kind, WolfmedKillTier Tier, float Damage = 0f, int Pellets = 1)
{
    public static readonly WolfmedKillStrength None = new(WolfmedKillKind.NonLethal, WolfmedKillTier.Weak);

    public bool Lethal => Kind != WolfmedKillKind.NonLethal;
}

/// <summary>
/// Executions and weapon suicides on a wound host: measures the weapon, kills through the brain, then leaves the
/// gore its strength buys on the head.
/// </summary>
/// <remarks>
/// The kill is always <see cref="WolfmedDyingActionsSystem.EndDeliberately"/>. The gore is direct calls (a wound, an
/// organ out, an amputation), never a big damage number: one hit cannot sever or ash a head through damage. The brain
/// is never deleted, so every outcome can still be undone by somebody doing the work.
/// </remarks>
public sealed partial class WolfmedExecutionSystem : EntitySystem
{
    [Dependency] private AmputationSystem _amputation = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ILocalizationManager _loc = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private WolfmedCharringSystem _charring = default!;
    [Dependency] private WolfmedDismembermentSystem _dismemberment = default!;
    [Dependency] private WolfmedDyingActionsSystem _endings = default!;
    [Dependency] private WolfmedGibDecalSystem _gibs = default!;
    [Dependency] private WolfmedGoreSystem _gore = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private WolfmedWoundSfxSystem _sfx = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WoundFractureSystem _fractures = default!;
    [Dependency] private WoundSystem _wounds = default!;

    /// <summary>A round whose own damage is under this kills nobody: practice, blanks, EMP, a cap gun.</summary>
    public const float NonLethalBelow = 5f;

    /// <summary>A stamina, stun or explosive-carrier round with no point or edge is less-lethal up to this.</summary>
    public const float LessLethalAtMost = 20f;

    private const string ChamberSlot = "gun_chamber";
    private const float BrainThrowSpeed = 5f;
    private const float OrganScatterSpeed = 3f;

    private static readonly EntProtoId Ash = "Ash";
    private static readonly ProtoId<WoundPrototype> Charring = "WFWolfmedCharringWound";
    private static readonly SoundSpecifier GibSound = new SoundCollectionSpecifier("gib");
    private static readonly SoundSpecifier BurnSound = new SoundCollectionSpecifier("MeatLaserImpact");

    /// <summary>The damage types that land on a body part. Structural and Radiation never count.</summary>
    private static readonly HashSet<string> Localized = new() { "Blunt", "Slash", "Piercing", "Heat", "Cold", "Shock", "Caustic" };

    private static readonly HashSet<string> EnergyTypes = new() { "Heat", "Shock", "Cold", "Caustic" };

    // The first wound of each list the head's profile supports is the one it gets: flesh, slime, plant, chassis.
    private static readonly ProtoId<WoundPrototype>[] Hole =
        { "WFWolfmedGunshotWound", "SlimePiercingWound", "PlantPiercingWound", "WFWolfmedBreachWound" };

    private static readonly ProtoId<WoundPrototype>[] Cut =
        { "SlashWound", "SlimeSlashWound", "PlantSlashWound", "WFWolfmedBreachWound" };

    private static readonly ProtoId<WoundPrototype>[] Burn =
        { "BurnWound", "SlimeBurnWound", "PlantBurnWound", "WFWolfmedOverheatingWound" };

    private static readonly ProtoId<WoundPrototype>[] Bruise =
        { "BluntWound", "SlimeBluntWound", "PlantBluntWound", "WFWolfmedDentWound" };

    private static readonly ProtoId<WoundPrototype>[] Artery = { "WFWolfmedArterialBleedWound" };

    #region Measuring

    /// <summary>What one deliberate use of this weapon amounts to now: a gun by its next round, anything else by its swing.</summary>
    public WolfmedKillStrength Measure(EntityUid weapon, EntityUid user)
    {
        if (!TryComp(weapon, out GunComponent? gun))
            return MeasureMelee(weapon, user);

        return PeekRound((weapon, gun)) is { } round ? MeasureRound((weapon, gun), round) : WolfmedKillStrength.None;
    }

    /// <summary>The round a shot just took out of the gun, measured before the caller spends or deletes it.</summary>
    public WolfmedKillStrength MeasureRound(Entity<GunComponent> gun, EntityUid? round)
    {
        if (round is not { } uid || TerminatingOrDeleted(uid) || IsSpent(uid) ||
            MetaData(uid).EntityPrototype is not { } prototype)
            return WolfmedKillStrength.None;

        return MeasureRound(gun, prototype);
    }

    /// <summary>One round of this prototype out of this gun: pellets summed, the gun's damage modifier applied.</summary>
    public WolfmedKillStrength MeasureRound(Entity<GunComponent> gun, EntityPrototype round)
    {
        var bullet = _gun.GetBulletPrototype(round);
        var perRound = 0f;
        var energy = 0f;
        var cuts = false;
        foreach (var (type, amount) in _gun.GetBulletDamage(bullet).DamageDict)
        {
            if (amount <= FixedPoint2.Zero || !Localized.Contains(type))
                continue;

            perRound += amount.Float();
            if (EnergyTypes.Contains(type))
                energy += amount.Float();

            cuts |= type is "Piercing" or "Slash";
        }

        if (perRound < NonLethalBelow || !cuts && perRound <= LessLethalAtMost && IsLessLethal(bullet, energy <= 0f))
            return WolfmedKillStrength.None;

        var pellets = bullet.TryGetComponent(out ProjectileSpreadComponent? spread, _factory) ? Math.Max(1, spread.Count) : 1;
        var modifier = new GunDamageModifierEvent(gun.Comp.DamageModifier);
        RaiseLocalEvent(gun, ref modifier);

        var total = perRound * pellets * modifier.Modifier;
        var kind = energy * 2f > perRound ? WolfmedKillKind.Energy : WolfmedKillKind.Ballistic;
        // "A heavy gun (shotgun)": a spread of pellets is heavy whatever it adds up to.
        var tier = pellets > 1
            ? WolfmedKillTier.Heavy
            : Tier(total, _cfg.GetCVar(WolfmedCVars.ExecutionMedium), _cfg.GetCVar(WolfmedCVars.ExecutionHeavy));
        return new WolfmedKillStrength(kind, tier, total, pellets);
    }

    /// <summary>
    /// A melee weapon by one ordinary swing of this user, wield bonus included: blunt when at least half of it is
    /// Blunt, a blade otherwise. Never non-lethal while the swing does any harm at all.
    /// </summary>
    public WolfmedKillStrength MeasureMelee(EntityUid weapon, EntityUid user)
    {
        if (!TryComp(weapon, out MeleeWeaponComponent? melee))
            return WolfmedKillStrength.None;

        // The execution multiplies the swing by nine while it runs; the tier is about the weapon, not that bonus.
        var executing = TryComp(weapon, out ExecutionComponent? execution) && execution.Executing;
        if (executing)
            execution!.Executing = false;

        var total = 0f;
        var blunt = 0f;
        foreach (var (type, amount) in _melee.GetDamage(weapon, user, melee).DamageDict)
        {
            if (amount <= FixedPoint2.Zero || !Localized.Contains(type))
                continue;

            total += amount.Float();
            if (type == "Blunt")
                blunt += amount.Float();
        }

        if (executing)
            execution!.Executing = true;

        // A foam club: a swing that does nothing is not a way to kill.
        if (total <= 0f)
            return WolfmedKillStrength.None;

        // An even split is a bludgeon: a pickaxe in one hand is half Blunt, half Piercing.
        if (blunt * 2f >= total)
        {
            var bluntTier = Tier(total, _cfg.GetCVar(WolfmedCVars.ExecutionBluntMedium), _cfg.GetCVar(WolfmedCVars.ExecutionBluntHeavy));
            return new WolfmedKillStrength(WolfmedKillKind.Blunt, bluntTier, total);
        }

        var tier = Tier(total, _cfg.GetCVar(WolfmedCVars.ExecutionBladeMedium), _cfg.GetCVar(WolfmedCVars.ExecutionBladeHeavy));
        return new WolfmedKillStrength(WolfmedKillKind.Blade, tier, total);
    }

    private static WolfmedKillTier Tier(float total, float medium, float heavy)
    {
        if (total >= heavy)
            return WolfmedKillTier.Heavy;

        return total >= medium ? WolfmedKillTier.Medium : WolfmedKillTier.Weak;
    }

    private bool IsLessLethal(EntityPrototype bullet, bool bluntOnly)
    {
        // A launcher's shell is Blunt alone and only carries its blast; a plasma round or a fireball burns as well.
        return bullet.TryGetComponent(out StaminaDamageOnCollideComponent? _, _factory) ||
               bullet.TryGetComponent(out StunOnCollideComponent? _, _factory) ||
               bullet.TryGetComponent(out HitscanStaminaDamageComponent? _, _factory) ||
               bluntOnly && bullet.TryGetComponent(out ExplosiveComponent? _, _factory);
    }

    private bool IsSpent(EntityUid round) => TryComp(round, out CartridgeAmmoComponent? cartridge) && cartridge.Spent;

    /// <summary>The round the next shot fires, without taking it.</summary>
    private EntityPrototype? PeekRound(Entity<GunComponent> gun)
    {
        // The shared peek reads the chamber behind a revolver's hammer, and reports a spent casing as a round.
        if (TryComp(gun, out RevolverAmmoProviderComponent? revolver))
            return PeekRevolver(revolver);

        if (_containers.TryGetContainer(gun, ChamberSlot, out var chamber) &&
            chamber is ContainerSlot { ContainedEntity: { } chambered } && IsSpent(chambered))
            return null;

        // A pump or lever action keeps the spent casing on top until it is worked.
        if (TryComp(gun, out BallisticAmmoProviderComponent? tube))
        {
            var loaded = tube.Entities;
            if (loaded.Count > 0 && IsSpent(loaded[tube.FireInLoadOrder ? 0 : loaded.Count - 1]))
                return null;
        }

        return _gun.TryNextShootPrototype((gun.Owner, gun.Comp), out var prototype) ? prototype : null;
    }

    private EntityPrototype? PeekRevolver(RevolverAmmoProviderComponent revolver)
    {
        var index = revolver.CurrentIndex;
        var chambers = revolver.Chambers;
        if (index < 0 || index >= chambers.Length || chambers[index] != true)
            return null;

        var slots = revolver.AmmoSlots;
        if (index < slots.Count && slots[index] is { } loaded)
            return TerminatingOrDeleted(loaded) || IsSpent(loaded) ? null : MetaData(loaded).EntityPrototype;

        return revolver.FillPrototype is { } fill && _prototypes.TryIndex<EntityPrototype>(fill, out var prototype)
            ? prototype
            : null;
    }

    #endregion

    #region Applying

    /// <summary>
    /// A gun fired into a head at the end of an Execute do-after. On a wound host a lethal round kills and leaves its
    /// gore, and a shot at oneself is a suicide: the ghost leaves first and cannot return. False means the caller's
    /// own damage should run instead.
    /// </summary>
    public bool TryGunExecution(EntityUid victim, EntityUid attacker, EntityUid weapon, WolfmedKillStrength strength)
    {
        if (!strength.Lethal || !CanApply(victim))
            return false;

        var subject = ToPrettyString(victim);
        var ending = attacker == victim ? WolfmedEnding.Suicide : WolfmedEnding.Execution;
        if (ending == WolfmedEnding.Suicide && !_mobState.IsDead(victim))
        {
            // Handled already: the gun is the method, so the tongue-bite default and the environment sweep stay out.
            RaiseLocalEvent(victim, new SuicideEvent(victim) { Handled = true });
            RaiseLocalEvent(victim, new SuicideGhostEvent(victim));
        }

        return Apply(victim, attacker, weapon, strength, ending, subject);
    }

    /// <summary>
    /// Kills the victim and applies the gore for that strength. Returns false and does nothing when the strength is
    /// not lethal or Wolfmed does not own this body's death, so callers fall back to what they did before.
    /// </summary>
    public bool Apply(EntityUid victim, EntityUid? attacker, EntityUid weapon, WolfmedKillStrength strength, WolfmedEnding ending)
    {
        return Apply(victim, attacker, weapon, strength, ending, ToPrettyString(victim));
    }

    private bool Apply(
        EntityUid victim,
        EntityUid? attacker,
        EntityUid weapon,
        WolfmedKillStrength strength,
        WolfmedEnding ending,
        EntityStringRepresentation? subject)
    {
        if (!strength.Lethal || !CanApply(victim))
            return false;

        // The kill first, on every tier and every species: a slime's and a chassis's core is not in the head.
        // A body that was already dead keeps the gore: the gun verb lets a corpse be shot.
        var killed = _endings.EndDeliberately(victim);
        var outcome = "no head to mark";
        if (_body.GetBodyChildrenOfType(victim, BodyPartType.Head).FirstOrNull() is { } head)
        {
            var machine = _traits.IsMechanical(head.Id);
            var tier = strength.Tier;
            outcome = strength.Kind switch
            {
                WolfmedKillKind.Ballistic => Ballistic(victim, head.Id, attacker, weapon, ref tier),
                WolfmedKillKind.Energy => Energy(victim, head.Id, ref tier),
                WolfmedKillKind.Blunt => Blunt(victim, head.Id, attacker, weapon, ref tier),
                _ => Blade(victim, head.Id, attacker, weapon, ref tier),
            };

            Announce(victim, strength.Kind, tier, machine);
        }
        else if (!killed)
        {
            return true;
        }

        var did = ending == WolfmedEnding.Suicide ? "killed themselves" : killed ? "executed" : "mutilated the corpse of";
        var measured = $"{strength.Kind} {strength.Tier}, {MathF.Round(strength.Damage, 1)} damage";
        if (ending == WolfmedEnding.Suicide)
        {
            _adminLog.Add(LogType.Damaged, LogImpact.Extreme,
                $"{subject:subject} {did} with {ToPrettyString(weapon):tool} ({measured}): {outcome}");
        }
        else
        {
            _adminLog.Add(LogType.Damaged, LogImpact.Extreme,
                $"{ToPrettyString(attacker):actor} {did} {subject:subject} with {ToPrettyString(weapon):tool} ({measured}): {outcome}");
        }

        return true;
    }

    private bool CanApply(EntityUid victim) => !TerminatingOrDeleted(victim) && _life.OwnsDeath(victim);

    // Each of the four returns what it did for the admin log, and steps the tier down when a head will not come off.
    private string Ballistic(EntityUid body, EntityUid head, EntityUid? attacker, EntityUid weapon, ref WolfmedKillTier tier)
    {
        var direction = _gore.GetHitDirection(body, attacker, weapon);
        if (tier == WolfmedKillTier.Heavy)
        {
            if (DestroyHead(body, head, false))
            {
                Splatter(body, direction, 30);
                return "head destroyed";
            }

            tier = WolfmedKillTier.Medium;
        }

        if (tier == WolfmedKillTier.Weak)
        {
            Wound(head, Hole, 30);
            Wound(head, Artery, 20);
            Splatter(body, direction, 10);
            return "head shot, artery open";
        }

        // "Shoots a hole in your head and your brain flies out."
        Wound(head, Hole, 80);
        Splatter(body, direction, 30);
        if (!ThrowBrain(body, head, direction))
            return "hole in the head";

        _dismemberment.Play(body, head);
        _gibs.Throw(body, WolfmedGibDecalSystem.Evisceration, 1);
        return "hole in the head, brain thrown out";
    }

    private string Energy(EntityUid body, EntityUid head, ref WolfmedKillTier tier)
    {
        if (tier == WolfmedKillTier.Heavy)
        {
            if (DestroyHead(body, head, true))
                return "head burned to ash";

            tier = WolfmedKillTier.Medium;
        }

        // A laser cauterises: a burn and dead tissue, no bleed.
        _audio.PlayPvs(BurnSound, body);
        if (tier == WolfmedKillTier.Weak)
        {
            Wound(head, Burn, 40);
            _charring.TryChar(head, Charring, 20);
            return "head burned through";
        }

        Wound(head, Burn, 60);
        _charring.TryChar(head, Charring, 80);
        return "head burned through, brain destroyed in place";
    }

    private string Blade(EntityUid body, EntityUid head, EntityUid? attacker, EntityUid weapon, ref WolfmedKillTier tier)
    {
        if (tier == WolfmedKillTier.Heavy)
        {
            if (_amputation.TryAmputate(body, head))
                return "decapitated";

            tier = WolfmedKillTier.Medium;
        }

        var direction = _gore.GetHitDirection(body, attacker, weapon);
        if (tier == WolfmedKillTier.Weak)
        {
            // A head with no artery to open (slime, plant, chassis) takes the cut instead.
            if (Wound(head, Artery, 20) == null)
                Wound(head, Cut, 25);

            Splatter(body, direction, 10);
            return "throat cut";
        }

        Wound(head, Artery, 25);
        Wound(head, Cut, 50);
        Splatter(body, direction, 30);
        return "throat cut deep";
    }

    private string Blunt(EntityUid body, EntityUid head, EntityUid? attacker, EntityUid weapon, ref WolfmedKillTier tier)
    {
        var direction = _gore.GetHitDirection(body, attacker, weapon);
        if (tier == WolfmedKillTier.Heavy)
        {
            // Crushed: what a heavy round leaves.
            if (DestroyHead(body, head, false))
            {
                Splatter(body, direction, 30);
                return "head crushed";
            }

            tier = WolfmedKillTier.Medium;
        }

        // A head with no bones (slime, plant, chassis) takes the blow and has no skull to break.
        if (tier == WolfmedKillTier.Weak)
        {
            Wound(head, Bruise, 30);
            _fractures.Break(head, FractureGrade.Simple);
            Splatter(body, direction, 10);
            return "skull cracked";
        }

        // Caved in. The brain stays where it is: it is already at 0 from the kill.
        Wound(head, Bruise, 80);
        _fractures.Break(head, FractureGrade.Comminuted);
        Splatter(body, direction, 30);
        return "skull caved in";
    }

    private EntityUid? Wound(EntityUid part, ProtoId<WoundPrototype>[] candidates, int severity)
    {
        foreach (var wound in candidates)
        {
            if (_wounds.CanCreateWound(part, wound))
                return _wounds.CreateOrMergeWound(part, wound, FixedPoint2.New(severity));
        }

        return null;
    }

    private void Splatter(EntityUid body, Vector2? direction, int severity)
    {
        if (_sfx.Profile is { } profile)
            _gore.TrySpawnSplatter(body, profile.HitSplatter, direction, FixedPoint2.New(severity));
    }

    private void Announce(EntityUid victim, WolfmedKillKind kind, WolfmedKillTier tier, bool machine)
    {
        var key = $"wolfmed-execution-{kind.ToString().ToLowerInvariant()}-{tier.ToString().ToLowerInvariant()}";
        if (machine && _loc.HasString(key + "-machine"))
            key += "-machine";

        _popup.PopupEntity(Loc.GetString(key, ("victim", Identity.Entity(victim, EntityManager))), victim, PopupType.LargeCaution);
    }

    /// <summary>
    /// Tears the brain out of the head and throws it. Nothing leaves a head that holds none (a chassis and a slime keep
    /// theirs in the torso), and a diona's is left in: out of the body it becomes a living nymph.
    /// </summary>
    private bool ThrowBrain(EntityUid body, EntityUid head, Vector2? direction)
    {
        foreach (var (organ, comp) in _body.GetPartOrgans(head).ToArray())
        {
            if (!HasComp<BrainComponent>(organ) || HasComp<NymphComponent>(organ) || !_body.RemoveOrgan(organ, comp))
                continue;

            _transform.DropNextTo(organ, body);
            var away = direction is { } line && line.LengthSquared() > 0.01f
                ? Vector2.Normalize(line)
                : _random.NextAngle().ToVec();
            _throwing.TryThrow(organ, away * BrainThrowSpeed, baseThrowSpeed: BrainThrowSpeed, pushbackRatio: 0f,
                playSound: false, doSpin: true);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Takes the head off through the amputation chain (stump, sound, spill, decals, death bookkeeping), then destroys
    /// the loose head: its organs stay on the deck, the rest is gibs or ash. A burned neck is cauterised.
    /// </summary>
    private bool DestroyHead(EntityUid body, EntityUid head, bool burned)
    {
        var parent = _body.GetParentPartOrNull(head);
        var organic = _traits.IsOrganic(head);
        if (!_amputation.TryAmputate(body, head))
            return false;

        foreach (var (organ, comp) in _body.GetPartOrgans(head).ToArray())
        {
            if (TerminatingOrDeleted(organ) || EntityManager.IsQueuedForDeletion(organ) || !_body.RemoveOrgan(organ, comp))
                continue;

            _transform.DropNextTo(organ, body);
            if (!burned)
            {
                _throwing.TryThrow(organ, _random.NextAngle().ToVec() * OrganScatterSpeed, baseThrowSpeed: OrganScatterSpeed,
                    pushbackRatio: 0f, playSound: false, doSpin: true);
            }
        }

        if (burned)
        {
            if (parent is { } neck)
                CauteriseStump(neck);

            if (organic)
                Spawn(Ash, _transform.GetMoverCoordinates(body));

            _audio.PlayPvs(BurnSound, body);
        }
        else
        {
            _gibs.Throw(body, WolfmedGibDecalSystem.Gib, _cfg.GetCVar(WolfmedCVars.GibsGib));
            _audio.PlayPvs(GibSound, body);
        }

        QueueDel(head);
        return true;
    }

    private void CauteriseStump(EntityUid parent)
    {
        foreach (var wound in _wounds.GetWounds(parent).ToArray())
        {
            if (TryComp(wound, out WolfmedStumpComponent? stump) && stump.PartType == BodyPartType.Head)
                _bleeding.SetTreatment(wound.Owner, BleedingTreatment.Cauterized);
        }
    }

    #endregion
}

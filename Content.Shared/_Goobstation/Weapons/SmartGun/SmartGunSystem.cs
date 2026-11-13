using Content.Shared._Goobstation.Wizard.Projectiles;
using Content.Shared.CombatMode; // Forge-Change
using Content.Shared.DoAfter; // Forge-Change
using Content.Shared.Examine; // Forge-Change
using Content.Shared.Humanoid; // Forge-Change
using Content.Shared.Interaction; // Forge-Change
using Content.Shared._Mono.Company; // Forge-Change
using Content.Shared.Mobs.Components; // Forge-Change
using Content.Shared.Popups; // Forge-Change
using Content.Shared.Projectiles; // Forge-Change
using Content.Shared.Verbs; // Forge-Change
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Utility;

namespace Content.Shared._Goobstation.Weapons.SmartGun;

public sealed class SmartGunSystem : EntitySystem
{
    // Forge-Change-Start
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    // Forge-Change-End

    public override void Initialize()
    {
        base.Initialize();

        // Forge-Change-Start
        SubscribeLocalEvent<SmartGunComponent, AmmoShotEvent>(OnShot);
        SubscribeLocalEvent<SmartGunComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<SmartGunComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<SmartGunComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<SmartGunComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<SmartGunComponent, SmartGunTargetSelectionDoAfterEvent>(OnTargetSelectionDoAfter);
        // Forge-Change-End
    }

    // Forge-Change-Start
    private void OnExamine(Entity<SmartGunComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(
            "smartgun-mode-examine",
            ("mode", Loc.GetString(GetModeLoc(ent.Comp.Mode)))));
    }

    private void OnAttemptShoot(Entity<SmartGunComponent> ent, ref AttemptShootEvent args)
    {
        if (!TryComp(ent.Owner, out GunComponent? gun) ||
            gun.Target is not { } target ||
            ent.Comp.Mode != SmartGunMode.Disabled &&
            !ShouldPassThroughTarget(ent.Comp, args.User, target))
            return;

        gun.Target = null;
        Dirty(ent.Owner, gun);
    }
    // Forge-Change-End

    // Forge-Change-Start
    private void OnGetVerbs(Entity<SmartGunComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var nextMode = (SmartGunMode)(((int) ent.Comp.Mode + 1) % Enum.GetValues<SmartGunMode>().Length);
        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("smartgun-mode-verb", ("mode", Loc.GetString(GetModeLoc(ent.Comp.Mode)))),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/refresh.svg.192dpi.png")),
            Priority = 1,
            Act = () => SetMode(ent, nextMode, user),
        });
    }

    private void SetMode(Entity<SmartGunComponent> ent, SmartGunMode mode, EntityUid user)
    {
        if (ent.Comp.Mode == mode)
            return;

        ent.Comp.Mode = mode;
        Dirty(ent);
        _popup.PopupClient(Loc.GetString("smartgun-mode-switched", ("mode", Loc.GetString(GetModeLoc(mode)))), ent, user);
    }

    private static string GetModeLoc(SmartGunMode mode)
    {
        return mode switch
        {
            SmartGunMode.Disabled => "smartgun-mode-disabled",
            SmartGunMode.Company => "smartgun-mode-company",
            SmartGunMode.Humanoids => "smartgun-mode-humanoids",
            SmartGunMode.Selected => "smartgun-mode-selected",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    private void OnAfterInteract(Entity<SmartGunComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled ||
            args.Used != ent.Owner ||
            !args.CanReach ||
            args.Target is not { } target ||
            ent.Comp.Mode != SmartGunMode.Selected ||
            _combatMode.IsInCombatMode(args.User) ||
            target == args.User ||
            !HasComp<MobStateComponent>(target))
            return;

        var doAfter = new DoAfterArgs(
            EntityManager,
            args.User,
            TimeSpan.FromSeconds(ent.Comp.TargetSelectionDelay),
            new SmartGunTargetSelectionDoAfterEvent { Target = GetNetEntity(target) },
            ent.Owner,
            target,
            ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            DistanceThreshold = 2,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            args.Handled = true;
    }

    private void OnTargetSelectionDoAfter(Entity<SmartGunComponent> ent, ref SmartGunTargetSelectionDoAfterEvent args)
    {
        if (args.Cancelled ||
            ent.Comp.Mode != SmartGunMode.Selected ||
            _combatMode.IsInCombatMode(args.User))
            return;

        var target = GetEntity(args.Target);
        if (target == args.User || !HasComp<MobStateComponent>(target))
            return;

        var added = ent.Comp.SelectedTargets.Add(target);
        if (!added)
            ent.Comp.SelectedTargets.Remove(target);

        Dirty(ent);
        _popup.PopupClient(
            Loc.GetString(added ? "smartgun-target-added" : "smartgun-target-removed", ("target", ToPrettyString(target))),
            ent,
            args.User);
    }

    private void OnShot(Entity<SmartGunComponent> ent, ref AmmoShotEvent args)
    {
        var (uid, comp) = ent;

        if (!TryComp(uid, out GunComponent? gun))
            return;

        if (comp.RequiresWield && !(TryComp(uid, out WieldableComponent? wieldable) && wieldable.Wielded))
            return;

        foreach (var projectile in args.FiredProjectiles)
        {
            if (!TryComp(projectile, out ProjectileComponent? projectileComponent))
                continue;

            projectileComponent.IgnoreSameCompany = comp.Mode == SmartGunMode.Company;
            projectileComponent.IgnoreHumanoids = comp.Mode == SmartGunMode.Humanoids;
            projectileComponent.IgnoreEntities.Clear();
            if (comp.Mode == SmartGunMode.Selected)
                projectileComponent.IgnoreEntities.UnionWith(comp.SelectedTargets);
            Dirty(projectile, projectileComponent);

            if (!TryComp(projectile, out HomingProjectileComponent? homing))
                continue;

            homing.Target = null;
            if (comp.Mode != SmartGunMode.Disabled &&
                gun.Target is { } target &&
                target != Transform(uid).ParentUid &&
                (projectileComponent.Shooter is not { } shooter || !ShouldPassThroughTarget(comp, shooter, target)))
            {
                homing.Target = target;
            }

            Dirty(projectile, homing);
        }
    }

    private bool ShouldPassThroughTarget(SmartGunComponent smartGun, EntityUid shooter, EntityUid target)
    {
        return smartGun.Mode switch
        {
            SmartGunMode.Company =>
                TryComp<CompanyComponent>(shooter, out var shooterCompany) &&
                shooterCompany.CompanyName != "None" &&
                TryComp<CompanyComponent>(target, out var targetCompany) &&
                shooterCompany.CompanyName == targetCompany.CompanyName,
            SmartGunMode.Humanoids =>
                HasComp<MobStateComponent>(target) && HasComp<HumanoidAppearanceComponent>(target),
            SmartGunMode.Selected => smartGun.SelectedTargets.Contains(target),
            _ => false,
        };
    }
    // Forge-Change-End
}

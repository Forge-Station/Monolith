using Content.Shared._Forge.Genetics.Mutations;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Prototypes;
// Drop is blocked by Unremoveable via ContainerGettingRemovedAttemptEvent.

namespace Content.Server._Forge.Genetics.Mutations;

public sealed partial class GeneticBoneWeaponSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticBoneWeaponComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GeneticBoneWeaponComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GeneticBoneWeaponComponent, GeneticBoneWeaponActionEvent>(OnGrow);
        SubscribeLocalEvent<GeneticBoneBladeComponent, ThrowItemAttemptEvent>(OnBladeThrowItem);
        SubscribeLocalEvent<GeneticBoneBladeComponent, GotUnequippedHandEvent>(OnBladeUnequipped);
    }

    private void OnStartup(Entity<GeneticBoneWeaponComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<GeneticBoneWeaponComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);

        if (ent.Comp.SpawnedWeapon is { } weapon && Exists(weapon))
        {
            RemComp<UnremoveableComponent>(weapon);
            QueueDel(weapon);
        }
    }

    private void OnGrow(Entity<GeneticBoneWeaponComponent> ent, ref GeneticBoneWeaponActionEvent args)
    {
        if (args.Handled)
            return;

        // Toggle: second press retracts the blade (no slash cost).
        if (ent.Comp.SpawnedWeapon is { } existing && Exists(existing))
        {
            args.Handled = true;
            RemComp<UnremoveableComponent>(existing);
            QueueDel(existing);
            ent.Comp.SpawnedWeapon = null;
            Dirty(ent);
            _popup.PopupEntity(Loc.GetString("gene-bone-weapon-retracted"), ent.Owner, ent.Owner);
            return;
        }

        if (!_prototypes.HasIndex(ent.Comp.WeaponPrototype))
            return;

        args.Handled = true;

        var damage = new DamageSpecifier();
        damage.DamageDict["Slash"] = ent.Comp.GrowSlash;
        _damageable.TryChangeDamage(ent.Owner, damage, ignoreResistances: true, interruptsDoAfters: false);

        var weapon = Spawn(ent.Comp.WeaponPrototype, Transform(ent.Owner).Coordinates);
        EnsureComp<UnremoveableComponent>(weapon);
        ent.Comp.SpawnedWeapon = weapon;
        Dirty(ent);

        if (!_hands.TryForcePickupAnyHand(ent.Owner, weapon, checkActionBlocker: false))
        {
            QueueDel(weapon);
            ent.Comp.SpawnedWeapon = null;
            Dirty(ent);
            _popup.PopupEntity(Loc.GetString("gene-bone-weapon-drop"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        _popup.PopupEntity(Loc.GetString("gene-bone-weapon-grown"), ent.Owner, ent.Owner);
    }

    private void OnBladeThrowItem(Entity<GeneticBoneBladeComponent> ent, ref ThrowItemAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnBladeUnequipped(Entity<GeneticBoneBladeComponent> ent, ref GotUnequippedHandEvent args)
    {
        // Unremoveable may delete on drop; clear owner ref if this was their blade.
        var query = EntityQueryEnumerator<GeneticBoneWeaponComponent>();
        while (query.MoveNext(out var owner, out var gene))
        {
            if (gene.SpawnedWeapon != ent.Owner)
                continue;

            gene.SpawnedWeapon = null;
            Dirty(owner, gene);
        }
    }
}

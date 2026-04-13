using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared._Forge.Demon.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;

namespace Content.Server._Forge.Demon.Systems;

public sealed class DamageOnActionSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageOnActionComponent, ComponentInit>(OnCompInit);
        SubscribeLocalEvent<DamageOnActionComponent, DamageOnActionEvent>(OnAction);
    }

    private void OnCompInit(Entity<DamageOnActionComponent> ent, ref ComponentInit args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ActionEntity, ent.Comp.Action, ent.Owner);
        _actions.SetUseDelay(ent.Comp.ActionEntity, ent.Comp.Delay);
    }

    private void OnAction(Entity<DamageOnActionComponent> ent, ref DamageOnActionEvent args)
    {
        if (!TryComp<HungerComponent>(ent.Owner, out var hunger))
            return;

        if (!TryComp<DamageableComponent>(ent.Owner, out var damageable))
            return;

        if (_hunger.GetHunger(hunger) < ent.Comp.HungerPerUse)
        {
            _popup.PopupEntity(Loc.GetString("damage-action-too-hungry"), ent.Owner, ent.Owner);
            args.Handled = true;
            return;
        }

        _hunger.ModifyHunger(ent.Owner, -ent.Comp.HungerPerUse, hunger);
        _damageable.TryChangeDamage(ent.Owner, ent.Comp.Damage, true, false, damageable);
        args.Handled = true;
    }
}

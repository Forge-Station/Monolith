using System.Linq;
using Content.Server.Polymorph.Components;
using Content.Server.Polymorph.Systems;
using Content.Shared._Forge.Genetics.Mutations;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Popups;

namespace Content.Server._Forge.Genetics.Mutations;

public sealed partial class GeneticSlimeFormSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private PolymorphSystem _polymorph = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticSlimeFormComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GeneticSlimeFormComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GeneticSlimeFormComponent, GeneticSlimeTransformActionEvent>(OnTransform);
    }

    private void OnStartup(Entity<GeneticSlimeFormComponent> ent, ref ComponentStartup args)
    {
        EnsureAction(ent);

        if (ent.Comp.StartupDamageApplied)
            return;

        ApplyTransformDamage(ent.Owner, ent.Comp.TransformSlash);
        ent.Comp.StartupDamageApplied = true;
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("gene-slime-form-activate"), ent.Owner, ent.Owner);
    }

    private void OnShutdown(Entity<GeneticSlimeFormComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnTransform(Entity<GeneticSlimeFormComponent> ent, ref GeneticSlimeTransformActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (TryComp<PolymorphedEntityComponent>(ent.Owner, out var poly))
        {
            var parent = poly.Parent;
            var reverted = _polymorph.Revert((ent.Owner, poly));
            if (reverted == null || !Exists(parent))
            {
                _popup.PopupEntity(Loc.GetString("gene-slime-form-fail"), ent.Owner, ent.Owner, PopupType.SmallCaution);
                args.Handled = false;
                return;
            }

            // Parent kept its original action while paused — do not AddAction again.
            if (TryComp<GeneticSlimeFormComponent>(parent, out var parentForm))
                EnsureAction((parent, parentForm));

            ApplyTransformDamage(parent, ent.Comp.TransformSlash);
            _popup.PopupEntity(Loc.GetString("gene-slime-form-humanoid"), parent, parent);
            return;
        }

        var child = _polymorph.PolymorphEntity(ent.Owner, ent.Comp.BlobPolymorph);
        if (child == null)
        {
            _popup.PopupEntity(Loc.GetString("gene-slime-form-fail"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            args.Handled = false;
            return;
        }

        // OnStartup already bound the action on the copied component; only mark damage handled.
        if (TryComp<GeneticSlimeFormComponent>(child.Value, out var childForm))
        {
            childForm.StartupDamageApplied = true;
            EnsureAction((child.Value, childForm));
            Dirty(child.Value, childForm);
        }

        ApplyTransformDamage(child.Value, ent.Comp.TransformSlash);
        _popup.PopupEntity(Loc.GetString("gene-slime-form-blob"), child.Value, child.Value);
    }

    /// <summary>
    /// Keeps exactly one transform action on the entity. Copied ActionEntity refs from polymorph
    /// often still point at the parent's action (Exists == true), so we also purge orphans by proto.
    /// </summary>
    private void EnsureAction(Entity<GeneticSlimeFormComponent> ent)
    {
        if (ent.Comp.ActionEntity != null)
        {
            var actionId = ent.Comp.ActionEntity.Value;
            var valid = Exists(actionId)
                        && !TerminatingOrDeleted(actionId)
                        && _actions.TryGetActionData(actionId, out var action)
                        && action.AttachedEntity == ent.Owner;

            if (!valid)
            {
                if (Exists(actionId)
                    && _actions.TryGetActionData(actionId, out var stale)
                    && stale.AttachedEntity == ent.Owner)
                {
                    _actions.RemoveAction(ent.Owner, actionId);
                }

                ent.Comp.ActionEntity = null;
            }
        }

        foreach (var (actionId, _) in _actions.GetActions(ent.Owner).ToArray())
        {
            if (MetaData(actionId).EntityPrototype?.ID != ent.Comp.Action.Id)
                continue;

            if (ent.Comp.ActionEntity == actionId)
                continue;

            _actions.RemoveAction(ent.Owner, actionId);
        }

        if (ent.Comp.ActionEntity != null && Exists(ent.Comp.ActionEntity.Value))
        {
            Dirty(ent);
            return;
        }

        ent.Comp.ActionEntity = null;
        _actions.AddAction(ent.Owner, ref ent.Comp.ActionEntity, ent.Comp.Action);
        Dirty(ent);
    }

    private void ApplyTransformDamage(EntityUid uid, float amount)
    {
        if (amount <= 0)
            return;

        var damage = new DamageSpecifier();
        damage.DamageDict["Slash"] = amount;
        _damageable.TryChangeDamage(uid, damage, ignoreResistances: true, interruptsDoAfters: false);
    }
}

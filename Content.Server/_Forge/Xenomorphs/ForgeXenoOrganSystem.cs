using Content.Shared._Forge.Xenomorphs;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Shared.Containers;

namespace Content.Server._Forge.Xenomorphs;

/// <summary>
/// Glands carry the caste abilities. A xenomorph hosts its own set.
/// A harvested gland works in the hands or grafted into another species, and that body takes two at most.
/// </summary>
public sealed partial class ForgeXenoOrganSystem : EntitySystem
{
    private const float GraftDelay = 4f;

    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForgeXenoOrganHostComponent, MapInitEvent>(OnHostInit);
        SubscribeLocalEvent<ForgeXenoOrganComponent, MapInitEvent>(OnOrganInit);
        SubscribeLocalEvent<ForgeXenoOrganComponent, ComponentStartup>(OnOrganStartup);
        SubscribeLocalEvent<ForgeXenoOrganComponent, ComponentShutdown>(OnOrganShutdown);
        SubscribeLocalEvent<ForgeXenoOrganComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<ForgeXenoOrganComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<ForgeXenoOrganComponent, GetItemActionsEvent>(OnGetItemActions);
        SubscribeLocalEvent<ForgeXenoOrganComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ForgeXenoOrganComponent, XenoGraftDoAfterEvent>(OnGraftDoAfter);
        SubscribeLocalEvent<ForgeXenoOrganComponent, GetVerbsEvent<AlternativeVerb>>(OnOrganVerbs);
        SubscribeLocalEvent<ForgeXenoGraftHostComponent, GetVerbsEvent<AlternativeVerb>>(OnGraftVerbs);
        SubscribeLocalEvent<HandsComponent, DidEquipHandEvent>(OnHandEquipped, after: [typeof(SharedActionsSystem)]);
        SubscribeLocalEvent<HandsComponent, DidUnequipHandEvent>(OnHandUnequipped, after: [typeof(SharedActionsSystem)]);
    }

    private void OnHostInit(Entity<ForgeXenoOrganHostComponent> ent, ref MapInitEvent args)
    {
        _containers.EnsureContainer<Container>(ent, ForgeXenoOrganHostComponent.ContainerId);

        foreach (var organ in ent.Comp.Organs)
            SpawnInContainerOrDrop(organ, ent, ForgeXenoOrganHostComponent.ContainerId);

        if (!TryComp<ButcherableComponent>(ent, out var butcher))
            return;

        // Copy so the prototype list is not mutated for every later spawn.
        butcher.SpawnedEntities = new List<EntitySpawnEntry>(butcher.SpawnedEntities);
        foreach (var organ in ent.Comp.Organs)
        {
            butcher.SpawnedEntities.Add(new EntitySpawnEntry
            {
                PrototypeId = organ,
                SpawnProbability = ForgeXenoOrganHostComponent.ButcherChance,
                Amount = 1,
                MaxAmount = 1,
            });
        }
    }

    private void OnOrganInit(Entity<ForgeXenoOrganComponent> ent, ref MapInitEvent args)
    {
        AttachIfHosted(ent);
    }

    private void OnOrganStartup(Entity<ForgeXenoOrganComponent> ent, ref ComponentStartup args)
    {
        AttachIfHosted(ent);
    }

    private void OnOrganShutdown(Entity<ForgeXenoOrganComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Host is { } host)
            Revoke(ent, host);
    }

    private void OnInserted(Entity<ForgeXenoOrganComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != ForgeXenoOrganHostComponent.ContainerId
            && args.Container.ID != ForgeXenoGraftHostComponent.ContainerId)
            return;

        AttachIfHosted(ent);
    }

    private void OnRemoved(Entity<ForgeXenoOrganComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ForgeXenoOrganHostComponent.ContainerId
            && args.Container.ID != ForgeXenoGraftHostComponent.ContainerId)
            return;

        if (ent.Comp.Host is { } host)
            Revoke(ent, host);
    }

    private void OnGetItemActions(Entity<ForgeXenoOrganComponent> ent, ref GetItemActionsEvent args)
    {
        // Still inside a xenomorph or a grafted body. Those hosts already have the actions.
        if (ent.Comp.Host != null
            && (HasComp<ForgeXenoOrganHostComponent>(ent.Comp.Host.Value)
                || HasComp<ForgeXenoGraftHostComponent>(ent.Comp.Host.Value)))
            return;

        if (!HeldWithinCap(ent, args.User))
            return;

        EnsureActions(ent);
        foreach (var action in ent.Comp.ActionEntities)
            args.AddAction(action);
    }

    private void OnAfterInteract(Entity<ForgeXenoOrganComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (HasComp<ForgeXenoOrganHostComponent>(target))
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("xeno-graft-xeno"), target, args.User);
            return;
        }

        if (!HasComp<HumanoidAppearanceComponent>(target))
            return;

        args.Handled = true;
        TryStartGraft(ent, args.User, target);
    }

    private void OnGraftDoAfter(Entity<ForgeXenoOrganComponent> ent, ref XenoGraftDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;

        if (UsedSlots(target, ent.Owner) >= ForgeXenoGraftHostComponent.MaxGrafts)
        {
            _popup.PopupEntity(Loc.GetString("xeno-graft-full"), target, args.User);
            return;
        }

        EnsureComp<ForgeXenoGraftHostComponent>(target);
        var container = _containers.EnsureContainer<Container>(target, ForgeXenoGraftHostComponent.ContainerId);
        if (!_containers.Insert(ent.Owner, container))
            return;

        RefreshHeld(target);
        _popup.PopupEntity(Loc.GetString("xeno-graft-done"), target);
    }

    private void OnOrganVerbs(Entity<ForgeXenoOrganComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (ent.Comp.Host != null && HasComp<ForgeXenoGraftHostComponent>(ent.Comp.Host.Value))
            return;

        if (!HasComp<HumanoidAppearanceComponent>(args.User) || HasComp<ForgeXenoOrganHostComponent>(args.User))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("xeno-graft-implant-self"),
            Act = () => TryStartGraft(ent, user, user),
        });
    }

    private void OnGraftVerbs(Entity<ForgeXenoGraftHostComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (!_containers.TryGetContainer(ent, ForgeXenoGraftHostComponent.ContainerId, out var container))
            return;

        var user = args.User;
        var organs = new List<EntityUid>();
        foreach (var contained in container.ContainedEntities)
            organs.Add(contained);

        foreach (var organ in organs)
        {
            if (!HasComp<ForgeXenoOrganComponent>(organ))
                continue;

            var organUid = organ;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("xeno-graft-extract", ("organ", Name(organUid))),
                Act = () => Extract(organUid, user),
            });
        }
    }

    private void TryStartGraft(Entity<ForgeXenoOrganComponent> ent, EntityUid user, EntityUid target)
    {
        if (HasComp<ForgeXenoOrganHostComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("xeno-graft-xeno"), target, user);
            return;
        }

        if (!HasComp<HumanoidAppearanceComponent>(target))
            return;

        if (ent.Comp.Host != null && HasComp<ForgeXenoGraftHostComponent>(ent.Comp.Host.Value))
            return;

        if (UsedSlots(target, ent.Owner) >= ForgeXenoGraftHostComponent.MaxGrafts)
        {
            _popup.PopupEntity(Loc.GetString("xeno-graft-full"), target, user);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, GraftDelay, new XenoGraftDoAfterEvent(), ent.Owner, target: target, used: ent.Owner)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnDropItem = true,
        });
    }

    private void Extract(EntityUid organ, EntityUid user)
    {
        if (!TryComp(organ, out TransformComponent? xform)
            || !_containers.TryGetContainingContainer(xform.ParentUid, organ, out var container)
            || container.ID != ForgeXenoGraftHostComponent.ContainerId)
            return;

        var body = container.Owner;
        _containers.Remove(organ, container);
        _hands.TryPickupAnyHand(user, organ);
        RefreshHeld(body);
        if (user != body)
            RefreshHeld(user);
    }

    private void AttachIfHosted(Entity<ForgeXenoOrganComponent> ent)
    {
        EnsureActions(ent);
        if (ContainingHost(ent) is not { } host)
            return;

        Grant(ent, host);
    }

    private void EnsureActions(Entity<ForgeXenoOrganComponent> ent)
    {
        if (ent.Comp.ActionEntities.Count > 0)
            return;

        foreach (var proto in ent.Comp.Actions)
        {
            EntityUid? action = null;
            if (_actionContainer.EnsureAction(ent.Owner, ref action, proto) && action != null)
                ent.Comp.ActionEntities.Add(action.Value);
        }
    }

    private EntityUid? ContainingHost(Entity<ForgeXenoOrganComponent> ent)
    {
        if (!TryComp(ent, out TransformComponent? xform) ||
            !_containers.TryGetContainingContainer(xform.ParentUid, ent.Owner, out var container))
            return null;

        if (container.ID == ForgeXenoOrganHostComponent.ContainerId
            && HasComp<ForgeXenoOrganHostComponent>(container.Owner))
            return container.Owner;

        if (container.ID == ForgeXenoGraftHostComponent.ContainerId)
            return container.Owner;

        return null;
    }

    private void Grant(Entity<ForgeXenoOrganComponent> ent, EntityUid host)
    {
        if (ent.Comp.Host == host)
            return;

        if (ent.Comp.Host is { } previous)
            Revoke(ent, previous);

        if (ent.Comp.ActionEntities.Count == 0)
            return;

        _actions.GrantActions(host, ent.Comp.ActionEntities, ent);
        ent.Comp.Host = host;
    }

    private void Revoke(Entity<ForgeXenoOrganComponent> ent, EntityUid host)
    {
        foreach (var action in ent.Comp.ActionEntities)
            _actions.RemoveAction(host, action);

        if (ent.Comp.Host == host)
            ent.Comp.Host = null;
    }

    private int GraftCount(EntityUid body)
    {
        return _containers.TryGetContainer(body, ForgeXenoGraftHostComponent.ContainerId, out var container)
            ? container.Count
            : 0;
    }

    /// <summary>
    /// Grafted glands plus glands in the hands. The one about to be grafted is left out.
    /// A xenomorph's internal set is a different container and is not counted.
    /// </summary>
    private int UsedSlots(EntityUid body, EntityUid except)
    {
        var count = GraftCount(body);
        if (!TryComp(body, out HandsComponent? hands))
            return count;

        foreach (var held in _hands.EnumerateHeld(body, hands))
        {
            if (held == except)
                continue;

            if (HasComp<ForgeXenoOrganComponent>(held))
                count++;
        }

        return count;
    }

    private void OnHandEquipped(Entity<HandsComponent> ent, ref DidEquipHandEvent args)
    {
        if (HasComp<ForgeXenoOrganComponent>(args.Equipped))
            RefreshHeld(args.User);
    }

    private void OnHandUnequipped(Entity<HandsComponent> ent, ref DidUnequipHandEvent args)
    {
        if (HasComp<ForgeXenoOrganComponent>(args.Unequipped))
            RefreshHeld(args.User);
    }

    /// <summary>
    /// Held glands are queried once, when they enter the hand. After a graft or an extraction
    /// the remaining handful has to be granted or taken back so the body never keeps more than two.
    /// </summary>
    private void RefreshHeld(EntityUid user)
    {
        if (!TryComp(user, out HandsComponent? hands))
            return;

        HashSet<EntityUid>? owned = null;
        if (TryComp(user, out ActionsComponent? actions))
            owned = actions.Actions;

        foreach (var held in _hands.EnumerateHeld(user, hands))
        {
            if (!TryComp(held, out ForgeXenoOrganComponent? organ))
                continue;

            if (organ.Host != null)
                continue;

            var ent = new Entity<ForgeXenoOrganComponent>(held, organ);
            EnsureActions(ent);

            if (owned != null)
            {
                foreach (var action in organ.ActionEntities)
                {
                    if (owned.Contains(action))
                        _actions.RemoveAction(user, action);
                }
            }

            if (!HeldWithinCap(ent, user) || organ.ActionEntities.Count == 0)
                continue;

            _actions.GrantActions(user, organ.ActionEntities, held);
        }
    }

    private bool HeldWithinCap(Entity<ForgeXenoOrganComponent> ent, EntityUid user)
    {
        var grafts = GraftCount(user);
        if (grafts >= ForgeXenoGraftHostComponent.MaxGrafts)
            return false;

        if (!TryComp(user, out HandsComponent? hands))
            return true;

        var index = 0;
        foreach (var held in _hands.EnumerateHeld(user, hands))
        {
            if (!HasComp<ForgeXenoOrganComponent>(held))
                continue;

            if (held == ent.Owner)
                return grafts + index < ForgeXenoGraftHostComponent.MaxGrafts;

            index++;
        }

        return grafts < ForgeXenoGraftHostComponent.MaxGrafts;
    }
}

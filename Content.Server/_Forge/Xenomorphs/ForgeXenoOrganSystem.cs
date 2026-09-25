using Content.Shared._Forge.Xenomorphs;
using Content.Shared.Actions;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Examine;
using Content.Shared.Nutrition.Components;
using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Server._Forge.Xenomorphs;

/// <summary>
/// Glands carry the caste abilities. A xenomorph hosts its own set.
/// A harvested gland is silent until a surgeon grafts it into a torso. That body takes two.
/// </summary>
public sealed partial class ForgeXenoOrganSystem : EntitySystem
{
    /// <summary>
    /// A grafted host has no plasma pool. Grafted abilities wait this long instead.
    /// Rest, fortify, zoom and pheromones keep the delay from their prototype so toggles stay usable.
    /// </summary>
    private static readonly TimeSpan GraftAbilityCooldown = TimeSpan.FromMinutes(1);

    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForgeXenoOrganHostComponent, MapInitEvent>(OnHostInit);
        SubscribeLocalEvent<ForgeXenoOrganComponent, MapInitEvent>(OnOrganInit);
        SubscribeLocalEvent<ForgeXenoOrganComponent, ComponentStartup>(OnOrganStartup);
        SubscribeLocalEvent<ForgeXenoOrganComponent, ComponentShutdown>(OnOrganShutdown);
        SubscribeLocalEvent<ForgeXenoOrganComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<ForgeXenoOrganComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<ForgeXenoOrganComponent, OrganAddedToBodyEvent>(OnAddedToBody);
        SubscribeLocalEvent<ForgeXenoOrganComponent, OrganRemovedFromBodyEvent>(OnRemovedFromBody);
        SubscribeLocalEvent<ForgeXenoOrganComponent, ExaminedEvent>(OnExamined);
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
        if (args.Container.ID != ForgeXenoOrganHostComponent.ContainerId)
            return;

        AttachIfHosted(ent);
    }

    private void OnRemoved(Entity<ForgeXenoOrganComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ForgeXenoOrganHostComponent.ContainerId)
            return;

        if (ent.Comp.Host is { } host)
            Revoke(ent, host);
    }

    private void OnAddedToBody(Entity<ForgeXenoOrganComponent> ent, ref OrganAddedToBodyEvent args)
    {
        EnsureActions(ent);
        Grant(ent, args.Body);
    }

    private void OnRemovedFromBody(Entity<ForgeXenoOrganComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        Revoke(ent, args.OldBody);
    }

    private void OnExamined(Entity<ForgeXenoOrganComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("xeno-graft-examine"));
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
        if (TryComp(ent, out OrganComponent? organ) && organ.Body is { } body)
            return body;

        if (!TryComp(ent, out TransformComponent? xform) ||
            !_containers.TryGetContainingContainer(xform.ParentUid, ent.Owner, out var container))
            return null;

        if (container.ID == ForgeXenoOrganHostComponent.ContainerId
            && HasComp<ForgeXenoOrganHostComponent>(container.Owner))
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

        if (!HasComp<ForgeXenoOrganHostComponent>(host))
            ApplyGraftCooldown(ent);

        _actions.GrantActions(host, ent.Comp.ActionEntities, ent);
        ent.Comp.Host = host;
    }

    /// <summary>
    /// Rest / fortify / zoom / pheromones keep a short toggle delay. Hide and combat abilities wait a minute.
    /// </summary>
    private void ApplyGraftCooldown(Entity<ForgeXenoOrganComponent> ent)
    {
        foreach (var action in ent.Comp.ActionEntities)
        {
            if (!_actions.TryGetActionData(action, out var actionComp))
                continue;

            // Events must hit the body, not the buried gland.
            actionComp.RaiseOnUser = true;
            Dirty(action, actionComp);

            if (KeepsOwnDelay(actionComp.BaseEvent))
                continue;

            _actions.SetUseDelay(action, GraftAbilityCooldown);
        }
    }

    private static bool KeepsOwnDelay(BaseActionEvent? ev)
    {
        return ev is ForgeXenoRestActionEvent
            or ForgeXenoFortifyActionEvent
            or ForgeXenoZoomActionEvent
            or ForgeXenoPheromonesActionEvent;
    }

    private void Revoke(Entity<ForgeXenoOrganComponent> ent, EntityUid host)
    {
        foreach (var action in ent.Comp.ActionEntities)
            _actions.RemoveAction(host, action);

        if (ent.Comp.Host == host)
            ent.Comp.Host = null;
    }
}

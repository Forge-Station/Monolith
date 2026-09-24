using Content.Shared._Forge.Xenomorphs;
using Content.Shared.Actions;
using Content.Shared.Nutrition.Components;
using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Server._Forge.Xenomorphs;

/// <summary>
/// Glands carry the caste abilities. The xenomorph hosts them; a butchered gland
/// grants the same actions to whoever holds it.
/// </summary>
public sealed partial class ForgeXenoOrganSystem : EntitySystem
{
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForgeXenoOrganHostComponent, MapInitEvent>(OnHostInit);
        SubscribeLocalEvent<ForgeXenoOrganComponent, MapInitEvent>(OnOrganInit);
        SubscribeLocalEvent<ForgeXenoOrganComponent, ComponentShutdown>(OnOrganShutdown);
        SubscribeLocalEvent<ForgeXenoOrganComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<ForgeXenoOrganComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<ForgeXenoOrganComponent, GetItemActionsEvent>(OnGetItemActions);
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
        EnsureActions(ent);
        if (HostOf(ent) is { } host)
            Grant(ent, host);
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

        EnsureActions(ent);
        if (HasComp<ForgeXenoOrganHostComponent>(args.Container.Owner))
            Grant(ent, args.Container.Owner);
    }

    private void OnRemoved(Entity<ForgeXenoOrganComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ForgeXenoOrganHostComponent.ContainerId)
            return;

        if (ent.Comp.Host is { } host)
            Revoke(ent, host);
    }

    private void OnGetItemActions(Entity<ForgeXenoOrganComponent> ent, ref GetItemActionsEvent args)
    {
        // Still inside a xenomorph. The caste is the one using these.
        if (ent.Comp.Host != null && HasComp<ForgeXenoOrganHostComponent>(ent.Comp.Host.Value))
            return;

        EnsureActions(ent);
        foreach (var action in ent.Comp.ActionEntities)
            args.AddAction(action);
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

    private EntityUid? HostOf(Entity<ForgeXenoOrganComponent> ent)
    {
        if (!TryComp(ent, out TransformComponent? xform) ||
            !_containers.TryGetContainingContainer(xform.ParentUid, ent.Owner, out var container))
            return null;

        if (container.ID != ForgeXenoOrganHostComponent.ContainerId)
            return null;

        return HasComp<ForgeXenoOrganHostComponent>(container.Owner) ? container.Owner : null;
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
}

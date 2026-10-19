using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Actions;

public sealed partial class SwappableActionSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedRMCActionsSystem _rmcActions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SwappableActionComponent, AfterAutoHandleStateEvent>(OnSwappableHandleState);
    }

    private void OnSwappableHandleState(Entity<SwappableActionComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (ent.Comp.IsSwapped)
        {
            if (!HasComp<WorldTargetActionComponent>(ent) || ent.Comp.SwappedEventTemplate == null)
                return;

            _rmcActions.SetEvent(ent, ent.Comp.SwappedEventTemplate);
        }
        else
        {
            if (!HasComp<InstantActionComponent>(ent) || ent.Comp.OriginalEventTemplate == null)
                return;

            _rmcActions.SetEvent(ent, ent.Comp.OriginalEventTemplate);
        }
    }

    // WOLFGATE: Wolfgate's actions keep their state on the per-type component, so swapping copies it across with CopyBaseActionData.
    public bool SwapInstantToWorldTarget<TOriginalEvent>(
        EntityUid owner,
        string swappedName,
        string swappedDescription) where TOriginalEvent : InstantActionEvent
    {
        foreach (var (actionId, _) in _actions.GetActions(owner))
        {
            if (!TryComp<InstantActionComponent>(actionId, out var instant) ||
                instant.Event is not TOriginalEvent)
            {
                continue;
            }

            if (!TryComp<SwappableActionComponent>(actionId, out var swappable) ||
                swappable.SwappedEventTemplate == null)
            {
                continue;
            }

            swappable.OriginalName = MetaData(actionId).EntityName;
            swappable.OriginalDescription = MetaData(actionId).EntityDescription;
            swappable.IsSwapped = true;
            Dirty(actionId, swappable);

            var world = new WorldTargetActionComponent();
            _rmcActions.CopyBaseActionData(instant, world);
            world.CheckCanAccess = false;
            world.Range = -1;
            world.DeselectOnMiss = false;
            world.Repeat = false;

            RemComp<InstantActionComponent>(actionId);
            AddComp(actionId, world, true);
            _rmcActions.SetEvent(actionId, swappable.SwappedEventTemplate);

            _metaData.SetEntityName(actionId, swappedName);
            _metaData.SetEntityDescription(actionId, swappedDescription);

            return true;
        }

        return false;
    }

    public void SwapAllToInstant(EntityUid owner)
    {
        foreach (var (actionId, _) in _actions.GetActions(owner))
        {
            if (!TryComp<SwappableActionComponent>(actionId, out var swappable))
                continue;

            if (TerminatingOrDeleted(actionId))
            {
                RemCompDeferred<SwappableActionComponent>(actionId);
                continue;
            }

            if (swappable.OriginalEventTemplate == null ||
                !TryComp<WorldTargetActionComponent>(actionId, out var world))
            {
                continue;
            }

            var instant = new InstantActionComponent();
            _rmcActions.CopyBaseActionData(world, instant);

            RemComp<WorldTargetActionComponent>(actionId);
            AddComp(actionId, instant, true);
            _rmcActions.SetEvent(actionId, swappable.OriginalEventTemplate);

            _metaData.SetEntityName(actionId, swappable.OriginalName);
            _metaData.SetEntityDescription(actionId, swappable.OriginalDescription);

            swappable.IsSwapped = false;
            Dirty(actionId, swappable);
        }
    }
}

using Content.Server.Actions;
using Content.Server.Humanoid;
using Content.Shared.Actions; // Forge-Change
using Content.Shared._Shitmed.Humanoid.Events; // Forge-Change
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Mobs;
using Content.Shared.Toggleable;
using Content.Shared.Wagging;
using Robust.Shared.Prototypes;

namespace Content.Server.Wagging;

/// <summary>
/// Adds an action to toggle wagging animation for tails markings that supporting this
/// </summary>
public sealed class WaggingSystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoidAppearance = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WaggingComponent, MapInitEvent>(OnWaggingMapInit);
        SubscribeLocalEvent<WaggingComponent, ComponentShutdown>(OnWaggingShutdown);
        SubscribeLocalEvent<WaggingComponent, ToggleActionEvent>(OnWaggingToggle);
        SubscribeLocalEvent<WaggingComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WaggingComponent, ProfileLoadFinishedEvent>(OnProfileLoadFinished); // Forge-Change
    }

    private void OnWaggingMapInit(EntityUid uid, WaggingComponent component, MapInitEvent args)
    {
        UpdateWaggingAction(uid, component);
    }

    private void OnProfileLoadFinished(EntityUid uid, WaggingComponent component, ProfileLoadFinishedEvent args)
    {
        UpdateWaggingAction(uid, component);
    }

    private void OnWaggingShutdown(EntityUid uid, WaggingComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.ActionEntity);
    }

    private void OnWaggingToggle(EntityUid uid, WaggingComponent component, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        TryToggleWagging(uid, wagging: component);
    }

    private void OnMobStateChanged(EntityUid uid, WaggingComponent component, MobStateChangedEvent args)
    {
        if (component.Wagging)
            TryToggleWagging(uid, wagging: component);
    }

    // Forge-Change-start: only provide wagging action when the entity has a tail marking.
    private void UpdateWaggingAction(EntityUid uid, WaggingComponent component, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(uid, ref humanoid, false))
        {
            _actions.RemoveAction(uid, component.ActionEntity);
            component.ActionEntity = null;
            component.Wagging = false;
            return;
        }

        var hasTail = humanoid.MarkingSet.Markings.TryGetValue(MarkingCategories.Tail, out var markings) &&
                      markings.Count > 0;

        if (!hasTail)
        {
            _actions.RemoveAction(uid, component.ActionEntity);
            component.ActionEntity = null;
            component.Wagging = false;
            return;
        }

        // The action container can still be uninitialized during profile load (e.g. integration map init spawn paths).
        if (!TryComp<ActionsContainerComponent>(uid, out var actionContainer) || !actionContainer.Initialized)
            return;

        _actions.AddAction(uid, ref component.ActionEntity, component.Action, uid);
    }
    // Forge-Change-end

    public bool TryToggleWagging(EntityUid uid, WaggingComponent? wagging = null, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(uid, ref wagging, ref humanoid))
            return false;

        if (!humanoid.MarkingSet.Markings.TryGetValue(MarkingCategories.Tail, out var markings))
            return false;

        if (markings.Count == 0)
            return false;

        wagging.Wagging = !wagging.Wagging;

        for (var idx = 0; idx < markings.Count; idx++) // Animate all possible tails
        {
            var currentMarkingId = markings[idx].MarkingId;
            string newMarkingId;

            if (wagging.Wagging)
            {
                newMarkingId = $"{currentMarkingId}{wagging.Suffix}";
            }
            else
            {
                if (currentMarkingId.EndsWith(wagging.Suffix))
                {
                    newMarkingId = currentMarkingId[..^wagging.Suffix.Length];
                }
                else
                {
                    newMarkingId = currentMarkingId;
                    Log.Warning($"Unable to revert wagging for {currentMarkingId}");
                }
            }

            if (!_prototype.HasIndex<MarkingPrototype>(newMarkingId))
            {
                Log.Warning($"{ToPrettyString(uid)} tried toggling wagging but {newMarkingId} marking doesn't exist");
                continue;
            }

            _humanoidAppearance.SetMarkingId(uid, MarkingCategories.Tail, idx, newMarkingId,
                humanoid: humanoid);
        }

        return true;
    }
}

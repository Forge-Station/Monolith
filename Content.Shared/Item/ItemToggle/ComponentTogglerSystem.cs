using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Timing;

namespace Content.Shared.Item.ItemToggle;

/// <summary>
/// Handles <see cref="ComponentTogglerComponent"/> component manipulation.
/// </summary>
public sealed partial class ComponentTogglerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!; // Forge-change: predict err fix

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ComponentTogglerComponent, ItemToggledEvent>(OnToggled);
    }

    private void OnToggled(Entity<ComponentTogglerComponent> ent, ref ItemToggledEvent args)
    {
        ToggleComponent(ent, args.Activated); // Forge-change: idk. Just move logic to ToggleComponent (for why?)
    }

    // Goobstation - Make this system more flexible
    // Forge-change - no, return to our legacy. No F-C comment cauz' return from goobs to the original wizden-logic.
    public void ToggleComponent(EntityUid uid, bool activate)
    {
        if (!_timing.IsFirstTimePredicted) // Forge-change: predict err fix
            return;

        if (!TryComp<ComponentTogglerComponent>(uid, out var component))
            return;

        if (activate)
        {
            var target = component.Parent ? Transform(uid).ParentUid : uid;
            if (TerminatingOrDeleted(target))
                return;

            component.Target = target;
            EntityManager.AddComponents(target, component.Components);
        }
        else
        {
            if (component.Target == null)
                return;

            if (TerminatingOrDeleted(component.Target.Value))
                return;

            EntityManager.RemoveComponents(component.Target.Value, component.RemoveComponents ?? component.Components);
        }
    }
}

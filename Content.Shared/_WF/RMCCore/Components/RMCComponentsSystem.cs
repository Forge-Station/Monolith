namespace Content.Shared._WF.RMCCore.Components;

public sealed class RMCComponentsSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<RemoveComponentsComponent, ComponentInit>(OnRemoveComponentsInit);
    }

    private void OnRemoveComponentsInit(Entity<RemoveComponentsComponent> ent, ref ComponentInit args)
    {
        EntityManager.RemoveComponents(ent, ent.Comp.Components);
    }
}

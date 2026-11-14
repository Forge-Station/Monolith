using Content.Shared._DV.Abilities;
using Content.Shared.Actions;

namespace Content.Server._DV.Abilities;

/// <summary>
/// Server half of sneaking: hands out the toggle action. Everything else lives in the shared system.
/// </summary>
public sealed partial class CrawlUnderObjectsSystem : SharedCrawlUnderObjectsSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrawlUnderObjectsComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<CrawlUnderObjectsComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.ToggleHideAction != null)
            return;

        _actions.AddAction(ent, ref ent.Comp.ToggleHideAction, ent.Comp.ActionProto);
    }
}

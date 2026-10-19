using Content.Shared._WF.Xenonids.Fruit;
using Content.Shared._WF.Xenonids.Fruit.Components;
using Robust.Shared.Timing;

namespace Content.Client._WF.Xenonids.Fruit;

public sealed partial class XenoFruitChooseSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoFruitPlanterComponent, AfterAutoHandleStateEvent>(OnXenoFruitAfterState);
    }

    private void OnXenoFruitAfterState(Entity<XenoFruitPlanterComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        try
        {
            if (!_timing.IsFirstTimePredicted)
                return;

            if (!TryComp(ent, out UserInterfaceComponent? ui))
                return;

            foreach (var bui in ui.ClientOpenInterfaces.Values)
            {
                if (bui is XenoFruitChooseBui chooseUi)
                    chooseUi.Refresh();
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error refreshing {nameof(XenoFruitChooseBui)}\n{e}");
        }
    }
}

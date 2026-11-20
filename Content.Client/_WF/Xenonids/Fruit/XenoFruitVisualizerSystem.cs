using Content.Shared._WF.Xenonids.Fruit.Components;
using Content.Shared._WF.Xenonids.Fruit.Events;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Xenonids.Fruit;

// System for updating the appearance of the resin fruits
public sealed class XenoFruitVisualizerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoFruitComponent, ComponentStartup>(SetVisuals);
        SubscribeLocalEvent<XenoFruitComponent, XenoFruitStateChangedEvent>(SetVisuals);
    }

    private void SetVisuals<T>(Entity<XenoFruitComponent> ent, ref T args)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        var state = ent.Comp.State switch
        {
            XenoFruitState.Item => ent.Comp.ItemState,
            XenoFruitState.Growing => ent.Comp.GrowingState,
            XenoFruitState.Grown => ent.Comp.GrownState,
            XenoFruitState.Eaten => ent.Comp.EatenState,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(state))
            return;

        sprite.LayerSetState(XenoFruitLayers.Base, state);
    }
}

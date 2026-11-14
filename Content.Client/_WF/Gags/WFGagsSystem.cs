using Content.Client.Clothing;
using Content.Client.Items.Systems;
using Content.Shared._WF.Gags;
using Content.Shared.Clothing;
using Content.Shared.Hands;
using Content.Shared.Item;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Gags;

/// <summary>Tints a recolourable item's layers with its colours: on the item, on the wearer and in hand.</summary>
public sealed partial class WFGagsSystem : SharedWFGagsSystem
{
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFGagsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFGagsComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<WFGagsComponent, GetEquipmentVisualsEvent>(OnGetEquipmentVisuals,
            after: [typeof(ClientClothingSystem)]);
        SubscribeLocalEvent<WFGagsComponent, GetInhandVisualsEvent>(OnGetInhandVisuals,
            after: [typeof(ItemSystem)]);
    }

    private void OnStartup(Entity<WFGagsComponent> ent, ref ComponentStartup args)
    {
        TintSprite(ent);
    }

    private void OnHandleState(Entity<WFGagsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Refresh(ent);
    }

    protected override void OnColorsChanged(Entity<WFGagsComponent> ent)
    {
        Refresh(ent);
    }

    private void Refresh(Entity<WFGagsComponent> ent)
    {
        TintSprite(ent);
        _item.VisualsChanged(ent);
    }

    private void TintSprite(Entity<WFGagsComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var index = 0;
        foreach (var _ in sprite.AllLayers)
        {
            var state = _sprite.LayerGetRsiState((ent, sprite), index).Name;
            if (ColorFor(ent.Comp, state) is { } color)
                _sprite.LayerSetColor((ent, sprite), index, color);

            index++;
        }
    }

    private void OnGetEquipmentVisuals(EntityUid uid, WFGagsComponent comp, GetEquipmentVisualsEvent args)
    {
        Tint(comp, args.Layers);
    }

    private void OnGetInhandVisuals(EntityUid uid, WFGagsComponent comp, GetInhandVisualsEvent args)
    {
        Tint(comp, args.Layers);
    }

    /// <summary>Replaces each slotted layer with a tinted copy; the originals belong to the prototype.</summary>
    private static void Tint(WFGagsComponent comp, List<(string, PrototypeLayerData)> layers)
    {
        for (var i = 0; i < layers.Count; i++)
        {
            var (key, layer) = layers[i];
            if (ColorFor(comp, layer.State) is not { } color)
                continue;

            layers[i] = (key, new PrototypeLayerData
            {
                Shader = layer.Shader,
                TexturePath = layer.TexturePath,
                RsiPath = layer.RsiPath,
                State = layer.State,
                Scale = layer.Scale,
                Rotation = layer.Rotation,
                Offset = layer.Offset,
                Visible = layer.Visible,
                Color = color,
                MapKeys = layer.MapKeys,
                RenderingStrategy = layer.RenderingStrategy,
                CopyToShaderParameters = layer.CopyToShaderParameters,
                Cycle = layer.Cycle,
                Loop = layer.Loop,
            });
        }
    }

    private static Color? ColorFor(WFGagsComponent comp, string? state)
    {
        return WFGagsColors.SlotOf(state) is { } slot && slot < comp.Colors.Count ? comp.Colors[slot] : null;
    }
}

using System.Numerics;
using Content.Shared._WF.Planets.Parachute;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._WF.Planets.Parachute;

/// <summary>Draws a parachute on its wearer: the pack strapped to them, and the open canopy over them in a fall.</summary>
public sealed class WFParachuteVisualsSystem : EntitySystem
{
    private static readonly SpriteSpecifier Canopy =
        new SpriteSpecifier.Rsi(new ResPath("_WF/Planets/Effects/parachute_canopy.rsi"), "canopy");

    private static readonly SpriteSpecifier Pack =
        new SpriteSpecifier.Rsi(new ResPath("_WF/Planets/Objects/parachute.rsi"), "icon");

    /// <summary>The pack is drawn small and low, so it reads as worn rather than as an item lying on the wearer.</summary>
    private static readonly Vector2 PackScale = new(0.6f, 0.6f);
    private static readonly Vector2 PackOffset = new(0f, -0.1f);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFParachutedComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WFParachutedComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnState(Entity<WFParachutedComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (!sprite.LayerMapTryGet(CanopyKey.Pack, out _))
        {
            var pack = sprite.AddLayer(Pack);
            sprite.LayerMapSet(CanopyKey.Pack, pack);
            sprite.LayerSetScale(pack, PackScale);
            sprite.LayerSetOffset(pack, PackOffset);
        }

        var has = sprite.LayerMapTryGet(CanopyKey.Key, out var layer);

        if (!ent.Comp.Deployed)
        {
            if (has)
                sprite.RemoveLayer(layer);

            return;
        }

        if (has)
            return;

        layer = sprite.AddLayer(Canopy);
        sprite.LayerMapSet(CanopyKey.Key, layer);

        // The canopy art has its harness at the bottom edge; sit that on the wearer's top.
        sprite.LayerSetOffset(layer, new Vector2(0f, sprite.Bounds.Height / 2f + 0.5f));
    }

    private void OnShutdown(Entity<WFParachutedComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (sprite.LayerMapTryGet(CanopyKey.Key, out var layer))
            sprite.RemoveLayer(layer);

        // Looked up afresh: removing the canopy may have shifted the pack's index.
        if (sprite.LayerMapTryGet(CanopyKey.Pack, out var pack))
            sprite.RemoveLayer(pack);
    }

    private enum CanopyKey
    {
        Key,
        Pack,
    }
}

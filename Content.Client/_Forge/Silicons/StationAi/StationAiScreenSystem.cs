using Content.Shared._Forge.Silicons.StationAi;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;
using System.Linq;

namespace Content.Client._Forge.Silicons.StationAi;

public sealed class StationAiScreenSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IResourceCache _resources = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;

    private static readonly ResPath DefaultRsi = new("Mobs/Silicon/station_ai.rsi");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationAiScreenComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<StationAiScreenComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnStartup(Entity<StationAiScreenComponent> ent, ref ComponentStartup args)
    {
        UpdateScreen(ent);
    }

    private void OnState(Entity<StationAiScreenComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateScreen(ent);
    }

    private void UpdateScreen(Entity<StationAiScreenComponent> ent)
    {
        if (!TryComp(ent.Owner, out SpriteComponent? sprite))
            return;

        var layer = sprite.LayerMapTryGet("unshaded", out var mapped)
            ? mapped
            : sprite.AllLayers.Count() > 1 ? 1 : -1;
        if (layer < 0)
            return;

        var path = DefaultRsi;
        var state = "ai_empty";
        if (ent.Comp.Occupied && _prototypes.TryIndex(ent.Comp.Screen, out var prototype))
        {
            path = prototype.Sprite;
            state = prototype.State;
        }

        if (!_resources.TryGetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / path, out var resource))
            return;

        _sprites.LayerSetRsi((ent.Owner, sprite), layer, resource.RSI, state);
    }
}

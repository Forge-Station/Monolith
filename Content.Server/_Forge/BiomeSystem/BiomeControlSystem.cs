using System.Numerics;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.BiomeSystem;

public readonly record struct BiomeRect(int MinX, int MaxX, int MinY, int MaxY)
{
    public override string ToString()
    {
        return $"[{MinX}..{MaxX}]x[{MinY}..{MaxY}]";
    }
}

public sealed partial class BiomeControlSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public static BiomeRect? GetYamlRect(BiomePrototype biome)
    {
        if (biome.MinX is not { } minX || biome.MaxX is not { } maxX || biome.MinY is not { } minY || biome.MaxY is not { } maxY)
            return null;

        return new BiomeRect(minX, maxX, minY, maxY);
    }

    public static Vector2? GetYamlDistance(BiomePrototype biome)
    {
        if (biome.DistanceRange is { } range)
            return range;

        return null;
    }

    public List<string> GetBiomes(EntityUid mapId)
    {
        if (!mapId.IsValid() || !TryComp<BiomeSelectionComponent>(mapId, out var biomeSelectionComponent))
            return [];

        return biomeSelectionComponent.Biomes;
    }

    public bool TryOffset(ProtoId<BiomePrototype> biomeId, Vector2i offset, out string result)
    {
        result = string.Empty;

        if (!_proto.TryIndex(biomeId, out var biome))
        {
            result = $"Неизвестный биом '{biomeId}' (см. biomelist).";
            return false;
        }

        var rect = GetYamlRect(biome);

        if (rect is not { } oldRect)
        {
            result = "Для данного биома не задан прямоугольник.";
            return false;
        }


        BiomeRect newRect = new(oldRect.MinX + offset.X, oldRect.MaxX + offset.X, oldRect.MinY + offset.Y, oldRect.MaxY + offset.Y);
        SetYmlRect(biome, newRect);
        result = $"{biomeId}: rect {oldRect} -> {newRect}";
        return true;
    }

    public bool TryMove(ProtoId<BiomePrototype> biomeId, Vector2i point, Vector2i size, out string result)
    {
        result = string.Empty;

        if (!_proto.TryIndex(biomeId, out var biome))
        {
            result = $"Неизвестный биом '{biomeId}' (см. biomelist).";
            return false;
        }

        var rect = GetYamlRect(biome);

        if (rect is not { } oldRect)
        {
            result = "Для данного биома не задан прямоугольник.";
            return false;
        }

        BiomeRect newRect = new(point.X, size.X, point.Y, size.Y);
        SetYmlRect(biome, newRect);
        result = $"{biomeId}: rect {oldRect} -> {newRect}";
        return true;
    }

    public void RefreshChunks(EntityUid mapId)
    {
        if (mapId.IsValid() && Exists(mapId))
            RaiseLocalEvent(mapId, new BiomeControlChangedEvent(), true);
    }

    private static void SetYmlRect(BiomePrototype biome, BiomeRect rect)
    {
        biome.MinX = rect.MinX;
        biome.MaxX = rect.MaxX;
        biome.MinY = rect.MinY;
        biome.MaxY = rect.MaxY;
    }
}

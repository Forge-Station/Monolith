using Content.Client.Decals;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Client.Placement;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.Mapping;

public static class MappingPalettePlacement
{
    public static void Begin(
        IPlacementManager placement,
        DecalPlacementSystem decals,
        IPrototype prototype,
        string? entityPlacementMode = null,
        Decal? sampledDecal = null)
    {
        switch (prototype)
        {
            case EntityPrototype entity:
            {
                decals.SetActive(false);
                placement.BeginPlacing(new PlacementInformation
                {
                    PlacementOption = string.IsNullOrEmpty(entityPlacementMode)
                        ? entity.PlacementMode
                        : entityPlacementMode,
                    EntityType = entity.ID,
                    IsTile = false
                });
                break;
            }
            case ContentTileDefinition tile:
            {
                decals.SetActive(false);
                placement.BeginPlacing(new PlacementInformation
                {
                    PlacementOption = "AlignTileAny",
                    TileType = tile.TileId,
                    IsTile = true
                });
                break;
            }
            case DecalPrototype decal:
            {
                var last = decals.GetActiveDecal();
                placement.Clear();
                decals.SetActive(true);

                Color color;
                float rotation;
                int zIndex;
                bool cleanable;
                bool snap;

                if (sampledDecal != null)
                {
                    color = sampledDecal.Color ?? Color.White;
                    rotation = (float) sampledDecal.Angle.Degrees;
                    zIndex = sampledDecal.ZIndex;
                    cleanable = sampledDecal.Cleanable;
                    snap = true;
                }
                else if (last.Decal != null)
                {
                    color = last.Color;
                    rotation = (float) last.Angle.Degrees;
                    zIndex = 0;
                    cleanable = false;
                    snap = last.Snap;
                }
                else
                {
                    color = Color.White;
                    rotation = 0f;
                    zIndex = 0;
                    cleanable = false;
                    snap = true;
                }

                decals.UpdateDecalInfo(decal.ID, color, rotation, snap, zIndex, cleanable);
                break;
            }
        }
    }

    public static string? ModeName(int selectedId)
    {
        if (selectedId <= 0)
            return null;

        var names = EntitySpawnWindow.InitOpts;
        return selectedId < names.Length ? names[selectedId] : null;
    }
}

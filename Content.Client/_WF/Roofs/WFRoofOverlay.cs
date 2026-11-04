using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.Roofs;

/// <summary>
/// Draws a solid roof over the floor of roofed tiles on the levels below the local player, seen from above: a vessel's
/// enclosed rooms as hull plating, a building's roofed floor as roofing, shaded at the eaves and lit by the level's
/// daylight. Walls, windows and doors are left uncovered, so they draw and light as normal. Nothing is drawn on the
/// player's own level (<see cref="WFRoofSystem.BelowViewer"/>). The z-level renderer draws each lower level through an
/// eye of its own, offset and scaled for its depth, and this overlay with it, so the roof sits on that level's tiles.
/// Drawn over the world after lighting, so it isn't darkened as the sunless indoors below it is.
/// </summary>
public sealed class WFRoofOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    private readonly SharedMapSystem _maps;
    private readonly SharedTransformSystem _transform;
    private readonly WFRoofSystem _roofs;
    private readonly Texture _hull;
    private readonly Texture _roofing;
    private List<Entity<MapGridComponent>> _grids = new();

    /// <summary>Light on a roof with no daylight to go by, such as out in space.</summary>
    private static readonly Color Unlit = new(0.55f, 0.55f, 0.6f);

    /// <summary>The least light a roof gets, so it still reads at night.</summary>
    private const float MinLight = 0.22f;

    /// <summary>Width of the shadow along a roof's edge, in tiles.</summary>
    private const float Eaves = 0.12f;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WFRoofOverlay()
    {
        IoCManager.InjectDependencies(this);
        _maps = _entities.System<SharedMapSystem>();
        _transform = _entities.System<SharedTransformSystem>();
        _roofs = _entities.System<WFRoofSystem>();
        _hull = _resources.GetResource<TextureResource>("/Textures/Tiles/hull.png").Texture;
        _roofing = _resources.GetResource<TextureResource>("/Textures/Tiles/plating.png").Texture;
        ZIndex = -10;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // The ship previewer draws maps the client made itself, to be seen inside.
        return args.MapUid.IsValid() && !_entities.IsClientSide(args.MapUid) && _roofs.BelowViewer(args.MapUid);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var light = Daylight(args.MapUid);
        _grids.Clear();
        _maps.FindGridsIntersecting(args.MapId, args.WorldAABB, ref _grids, approx: true, includeMap: true);

        foreach (var grid in _grids)
        {
            var implicitRoof = _entities.HasComponent<ImplicitRoofComponent>(grid);
            _entities.TryGetComponent(grid.Owner, out RoofComponent? roof);
            if (!implicitRoof && (roof == null || _roofs.Underground(grid)))
                continue;

            var enclosed = implicitRoof ? _roofs.Enclosed(grid) : null;
            bool Covered(Vector2i index) => _roofs.Covers(grid, roof, enclosed, index);

            handle.SetTransform(_transform.GetWorldMatrix(grid));
            var texture = implicitRoof ? _hull : _roofing;
            var shade = light.WithAlpha(1f);
            var eaves = new Color(light.R * 0.45f, light.G * 0.45f, light.B * 0.45f);
            var size = grid.Comp.TileSize;
            var tiles = _maps.GetTilesEnumerator(grid, grid.Comp, args.WorldAABB, ignoreEmpty: implicitRoof);
            while (tiles.MoveNext(out var tileRef))
            {
                var index = tileRef.GridIndices;
                if (!Covered(index))
                    continue;

                var box = new Box2(index * size, (index + Vector2i.One) * size);
                handle.DrawTextureRect(texture, box, shade);
                if (!Covered(index + new Vector2i(-1, 0)))
                    handle.DrawRect(new Box2(box.Left, box.Bottom, box.Left + Eaves, box.Top), eaves);
                if (!Covered(index + new Vector2i(1, 0)))
                    handle.DrawRect(new Box2(box.Right - Eaves, box.Bottom, box.Right, box.Top), eaves);
                if (!Covered(index + new Vector2i(0, -1)))
                    handle.DrawRect(new Box2(box.Left, box.Bottom, box.Right, box.Bottom + Eaves), eaves);
                if (!Covered(index + new Vector2i(0, 1)))
                    handle.DrawRect(new Box2(box.Left, box.Top - Eaves, box.Right, box.Top), eaves);
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>The map's ambient light, kept bright enough to see a roof by.</summary>
    private Color Daylight(EntityUid map)
    {
        if (!_entities.TryGetComponent(map, out MapLightComponent? mapLight))
            return Unlit;
        var ambient = mapLight.AmbientLightColor;
        return new Color(MathF.Max(ambient.R, MinLight), MathF.Max(ambient.G, MinLight), MathF.Max(ambient.B, MinLight));
    }
}

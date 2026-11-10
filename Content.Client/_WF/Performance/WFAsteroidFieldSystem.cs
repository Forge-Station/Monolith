using System.Numerics;
using Content.Shared._WF.AsteroidMining;
using Content.Shared._WF.Performance;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Performance;

/// <summary>
/// Reads the asteroid field lists for rocks whose grids this client hasn't been sent, and the radar shapes to draw
/// them with.
/// </summary>
public sealed partial class WFAsteroidFieldSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    private const int CircleSides = 16;

    /// <summary>Fill triangles and outline segments around each rock prototype and variant's centre.</summary>
    private readonly Dictionary<(string Proto, int Variant), WFRockShape?> _shapes = new();

    /// <summary>Adds the rocks in an area of a map whose grids this client doesn't have there.</summary>
    public void GetFarRocks(MapId map, Box2 area, List<WFFarRock> results)
    {
        var fields = EntityQueryEnumerator<WFAsteroidFieldComponent, TransformComponent>();
        while (fields.MoveNext(out var field, out var xform))
        {
            if (xform.MapID != map)
                continue;

            foreach (var rock in field.Rocks)
            {
                if (!area.Contains(rock.Position) || rock.Proto >= field.Protos.Count)
                    continue;

                if (TryGetEntity(rock.Grid, out var grid) && TryComp(grid, out TransformComponent? gridXform) &&
                    gridXform.MapID == map)
                {
                    continue;
                }

                var look = rock.Look < field.Looks.Count ? field.Looks[rock.Look] : null;
                results.Add(new WFFarRock(rock, field.Protos[rock.Proto], look));
            }
        }
    }

    /// <summary>The radar shape of a rock prototype's variant: its outline if it has one, else a circle.</summary>
    public WFRockShape? GetShape(string proto, int variant)
    {
        if (_shapes.TryGetValue((proto, variant), out var cached))
            return cached;

        WFRockShape? shape = null;
        if (_prototypes.TryIndex<EntityPrototype>(proto, out var entity) &&
            entity.TryComp<WFAsteroidComponent>(out var rock, EntityManager.ComponentFactory))
        {
            var outline = new List<Vector2>();
            if (rock.Shapes.TryGetValue(variant, out var irregular) && irregular.Outline.Count >= 3)
            {
                outline.AddRange(irregular.Outline);
            }
            else
            {
                for (var i = 0; i < CircleSides; i++)
                {
                    outline.Add(new Angle(MathF.Tau * i / CircleSides).ToVec() * rock.Radius);
                }
            }

            shape = new WFRockShape(outline);
        }

        _shapes[(proto, variant)] = shape;
        return shape;
    }
}

/// <summary>A rock read off a field list, with its prototype and look resolved.</summary>
public readonly record struct WFFarRock(WFAsteroidFieldRock Rock, string Proto, string? Look);

/// <summary>A rock's radar shape around its centre, as a triangle fan's triangles and as outline segments.</summary>
public sealed class WFRockShape
{
    public readonly List<Vector2> Fill = new();
    public readonly List<Vector2> Lines = new();

    public WFRockShape(List<Vector2> outline)
    {
        for (var i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            Fill.Add(Vector2.Zero);
            Fill.Add(a);
            Fill.Add(b);
            Lines.Add(a);
            Lines.Add(b);
        }
    }
}

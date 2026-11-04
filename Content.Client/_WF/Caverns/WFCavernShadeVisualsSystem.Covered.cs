using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._WF.Caverns;

/// <summary>
/// Hides a pit under a hull on the same layer. Every grid's floor draws beneath every entity, so its lip would otherwise
/// lie across the deck.
/// </summary>
public sealed partial class WFCavernShadeVisualsSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How often hulls are checked for pits under them.</summary>
    private static readonly TimeSpan CoveredInterval = TimeSpan.FromSeconds(0.2);

    /// <summary>How far past its tile a pit's lip reaches, in tiles.</summary>
    private const float LipReach = 0.5f;

    private TimeSpan _nextCoveredCheck;
    private HashSet<EntityUid> _covered = new();
    private HashSet<EntityUid> _coveredBefore = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCoveredCheck)
            return;
        _nextCoveredCheck = _timing.CurTime + CoveredInterval;

        (_covered, _coveredBefore) = (_coveredBefore, _covered);
        _covered.Clear();

        if (_pits.Count > 0)
            FindCovered();

        foreach (var uid in _coveredBefore)
        {
            if (!_covered.Contains(uid) && TryComp<SpriteComponent>(uid, out var sprite))
                _sprite.SetVisible((uid, sprite), true);
        }

        foreach (var uid in _covered)
        {
            if (!_coveredBefore.Contains(uid) && TryComp<SpriteComponent>(uid, out var sprite))
                _sprite.SetVisible((uid, sprite), false);
        }
    }

    private void FindCovered()
    {
        var hulls = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (hulls.MoveNext(out var hull, out var grid, out var xform))
        {
            if (xform.MapUid is not { } map || map == hull || !_pits.TryGetValue(map, out var pits))
                continue;

            var box = _transform.GetWorldMatrix(hull).TransformBox(grid.LocalAABB).Enlarged(LipReach);
            var min = box.BottomLeft.Floored();
            var max = box.TopRight.Floored();

            for (var x = min.X; x <= max.X; x++)
            {
                for (var y = min.Y; y <= max.Y; y++)
                {
                    var tile = new Vector2i(x, y);
                    if (!pits.TryGetValue(tile, out var pit) || _covered.Contains(pit))
                        continue;

                    var near = new Box2(new Vector2(x - LipReach, y - LipReach), new Vector2(x + 1f + LipReach, y + 1f + LipReach));
                    foreach (var under in _map.GetTilesIntersecting(hull, grid, near))
                    {
                        if (under.Tile.IsEmpty)
                            continue;

                        _covered.Add(pit);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>Whether a shade is hidden under a hull, for tests and tools.</summary>
    public bool IsCovered(EntityUid uid)
    {
        return _covered.Contains(uid);
    }
}

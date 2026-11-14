using Robust.Shared.Timing;

namespace Content.Server._CE.ZLevels.Core;

/// <summary>
/// Transit collision checks work out each falling grid's docked set once per tick, instead of once for every pair
/// it is checked against.
/// </summary>
public sealed partial class CEZLevelsSystem
{
    private readonly Dictionary<EntityUid, (bool Found, HashSet<EntityUid> Set, Box2 Aabb)> _wfGridSetCache = new();
    private GameTick _wfGridSetCacheTick;

    /// <summary><see cref="TryGetGridSetAabb"/>, kept for the rest of the tick.</summary>
    private bool WFTryGetCachedGridSetAabb(EntityUid primary, out HashSet<EntityUid> set, out Box2 aabb)
    {
        if (_wfGridSetCacheTick != _timing.CurTick)
        {
            _wfGridSetCache.Clear();
            _wfGridSetCacheTick = _timing.CurTick;
        }

        if (!_wfGridSetCache.TryGetValue(primary, out var cached))
        {
            var found = TryGetGridSetAabb(primary, out var fresh, out var freshAabb);
            cached = (found, fresh, freshAabb);
            _wfGridSetCache[primary] = cached;
        }

        set = cached.Set;
        aabb = cached.Aabb;
        // A set crushed earlier this tick is gone.
        return cached.Found && !TerminatingOrDeleted(primary) && !EntityManager.IsQueuedForDeletion(primary);
    }
}

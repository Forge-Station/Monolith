using Content.Shared._WF.GroundCover;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.GroundCover;

public sealed partial class WFGroundCoverSystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    /// <summary>Solution temperature from which a liquid counts as warm and melts snow.</summary>
    public const float WarmLiquid = 303f;

    /// <summary>Snow depth units melted by each unit of a body-warm liquid, up to <see cref="MeltVolume"/>.</summary>
    public const float MeltPerUnit = 1.5f;

    /// <summary>Units of a puddle that melt snow.</summary>
    public const float MeltVolume = 20f;

    private const string PuddleSolution = "puddle";

    private static readonly string[] BloodReagents = { "Blood", "CopperBlood", "InsectBlood", "AmmoniaBlood", "ZombieBlood" };

    private static readonly TimeSpan LiquidInterval = TimeSpan.FromSeconds(2);

    private readonly List<EntityUid> _puddles = new();
    private TimeSpan _nextLiquid;

    /// <summary>Melts the snow under each new warm puddle, once.</summary>
    private void UpdateLiquids(TimeSpan now)
    {
        if (now < _nextLiquid)
            return;

        _nextLiquid = now + LiquidInterval;
        _puddles.Clear();
        var query = EntityQueryEnumerator<PuddleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && HasComp<WFSnowCoverComponent>(grid) && !HasComp<WFPuddleMeltComponent>(uid))
                _puddles.Add(uid);
        }

        foreach (var uid in _puddles)
        {
            MeltUnder(uid);
        }
    }

    /// <summary>Melts the snow on a puddle's tile by how much warm liquid it holds. Public for tests.</summary>
    public void MeltUnder(EntityUid puddle)
    {
        if (!TryComp(puddle, out TransformComponent? xform) || xform.GridUid is not { } grid
            || !TryComp<MapGridComponent>(grid, out var gridComp)
            || !_solutions.TryGetSolution(puddle, PuddleSolution, out _, out var solution))
            return;

        EnsureComp<WFPuddleMeltComponent>(puddle);
        var warmth = solution.Temperature;
        foreach (var blood in BloodReagents)
        {
            if (solution.ContainsPrototype(blood))
                warmth = MathF.Max(warmth, 310f);
        }

        if (warmth < WarmLiquid)
            return;

        var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var depth = GetDepth(grid, tile);
        if (depth == 0)
            return;

        var volume = MathF.Min(solution.Volume.Float(), MeltVolume);
        var melt = (int) MathF.Ceiling(volume * MeltPerUnit * Math.Clamp((warmth - Freezing) / 37f, 0f, 1.2f));
        SetDepth(grid, tile, depth - melt);
    }
}

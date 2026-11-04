using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using Content.Shared._WF.PlanetCities;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetPois;

/// <summary>What a fixed city has come to this round: overrun by something, taken over by crazies, or left alone.</summary>
public sealed partial class WFPlanetPoiSystem
{
    /// <summary>Tiles a fate's groups and a city's residents keep from its spawn points, so nobody joins into a fight.</summary>
    private const float FateSpawnClearance = 16f;

    private async Task RollCityFateAsync(WFFixedCityPrototype city, List<CityFloor> floors, HashSet<EntityUid> created)
    {
        if (city.Fates.Count == 0 || floors.FirstOrDefault(floor => floor.Depth == 0) is not { } ground)
            return;

        var roll = _random.NextFloat(city.Fates.Sum(fate => fate.Weight));
        var picked = city.Fates[^1];
        foreach (var fate in city.Fates)
        {
            roll -= fate.Weight;
            if (roll >= 0)
                continue;
            picked = fate;
            break;
        }
        Log.Info($"{city.ID} on {ToPrettyString(ground.Grid)} this round: {picked.Name}");
        if (picked.Spawns is not { } spawns)
            return;

        // Indoor groups settle on roofed floor, one on every wanted level first; the rest are in the open streets.
        var indoors = picked.IndoorShare > 0
            ? CityPools(floors, floor => floor.Depth == 0 || (picked.UpperLevels && floor.Depth > 0), floor => floor.Indoor)
            : new List<CityPool>();
        var streets = CityPools(floors, floor => floor == ground, floor => floor.Tiles.Except(floor.Indoor)).FirstOrDefault();
        if (indoors.Count == 0 && streets == null)
            return;

        var firsts = indoors.ToList();
        _random.Shuffle(firsts);
        var weight = indoors.Sum(pool => pool.Tiles.Count);
        var visited = 0;
        var groups = _random.Next(picked.MinGroups, picked.MaxGroups + 1);
        for (var i = 0; i < groups; i++)
        {
            var inside = indoors.Count > 0 && (streets == null || _random.Prob(picked.IndoorShare));
            var pool = inside ? (visited < firsts.Count ? firsts[visited++] : PickPool(indoors, weight)) : streets!;
            var centre = _random.Pick(pool.Tiles);
            if (!inside)
            {
                foreach (var id in picked.Gathering)
                {
                    await _preload.Breathe();
                    if (TerminatingOrDeleted(pool.Floor.Grid.Owner))
                        return;
                    created.Add(Spawn(id, new EntityCoordinates(pool.Floor.Grid.Owner, (Vector2) centre + new Vector2(0.5f))));
                }
            }

            var size = _random.Next(picked.MinGroupSize, picked.MaxGroupSize + 1);
            for (var member = 0; member < size; member++)
            {
                var near = Enumerable.Range(0, 12)
                    .Select(_ => centre + new Vector2i(_random.Next(-3, 4), _random.Next(-3, 4)))
                    .FirstOrDefault(tile => tile != centre && pool.Set.Contains(tile), centre);
                foreach (var id in _entityTables.GetSpawns(spawns))
                {
                    // A person is a body, organs and kit: one at a time, within the generation budget.
                    await _preload.Breathe();
                    if (TerminatingOrDeleted(pool.Floor.Grid.Owner))
                        return;
                    created.Add(Spawn(id, new EntityCoordinates(pool.Floor.Grid.Owner, (Vector2) near + new Vector2(0.5f))));
                }
            }
        }
    }
}

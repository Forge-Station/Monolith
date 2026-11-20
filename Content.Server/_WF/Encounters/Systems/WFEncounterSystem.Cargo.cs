namespace Content.Server._WF.Encounters.Systems;

/// <summary>Lets extorters pick the richest hold among the nearest ships and set the toll by its value.</summary>
public sealed partial class WFEncounterSystem
{
    /// <summary>How many of the nearest crewed ships an extorter weighs before picking a mark.</summary>
    private const int MarkCandidates = 4;

    /// <summary>Share of a hold's value an extorter asks when that beats its usual toll.</summary>
    private const float CargoTollShare = 0.12f;

    /// <summary>The ship with the most valuable cargo among the nearest few in reach, or the nearest if none carries any.</summary>
    private EntityUid? RichestNearbyShip(EntityUid hunter, float reach, ICollection<EntityUid>? except, List<string>? neutral)
    {
        var skip = new HashSet<EntityUid>();
        if (except != null)
            skip.UnionWith(except);

        EntityUid? nearest = null;
        EntityUid? richest = null;
        var best = 0;
        for (var i = 0; i < MarkCandidates; i++)
        {
            if (NearestCrewedShip(hunter, reach, skip, neutral) is not { } ship)
                break;

            nearest ??= ship;
            skip.Add(ship);
            var value = CargoValue(ship);
            if (value <= best)
                continue;

            best = value;
            richest = ship;
        }

        return richest ?? nearest;
    }

    /// <summary>The base value of the trade cargo aboard a ship, as the market reckons it.</summary>
    private int CargoValue(EntityUid ship)
    {
        var ev = new WFCargoValueQueryEvent(ship);
        RaiseLocalEvent(ref ev);
        return ev.Value;
    }

    /// <summary>An extorter's toll for a mark: its usual toll, or a share of the mark's cargo if that is more.</summary>
    private int CargoToll(int toll, EntityUid? mark)
    {
        return mark is { } ship ? Math.Max(toll, (int) (CargoValue(ship) * CargoTollShare)) : toll;
    }
}

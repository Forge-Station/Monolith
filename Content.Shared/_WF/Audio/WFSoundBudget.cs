namespace Content.Shared._WF.Audio;

/// <summary>
/// Caps how many sounds of one kind start within a window, counted per place: the grid a sound comes from, or its map
/// where it is on no grid. A hull grinding out a landing then spends its own budget, not the next ship's.
/// </summary>
public sealed class WFSoundBudget
{
    /// <summary>How many places are remembered before those whose window is over are dropped.</summary>
    private const int PruneAt = 64;

    private readonly int _perWindow;
    private readonly TimeSpan _window;
    private readonly Dictionary<EntityUid, (TimeSpan End, int Count)> _places = new();
    private readonly List<EntityUid> _expired = new();

    public WFSoundBudget(int perWindow, TimeSpan window)
    {
        _perWindow = perWindow;
        _window = window;
    }

    /// <summary>How many places have a window on record.</summary>
    public int Places => _places.Count;

    /// <summary>The place an entity's sounds are counted against: its grid, else its map.</summary>
    public static EntityUid PlaceOf(TransformComponent xform)
    {
        return xform.GridUid ?? xform.MapUid ?? EntityUid.Invalid;
    }

    /// <summary>Whether one more sound may start at a place now, counting it if so.</summary>
    public bool Allow(EntityUid place, TimeSpan now)
    {
        if (!_places.TryGetValue(place, out var spent) || now >= spent.End)
        {
            // Places go with their grids, so the stale ones are dropped as new ones arrive.
            if (_places.Count >= PruneAt)
                Prune(now);

            _places[place] = (now + _window, 1);
            return true;
        }

        if (spent.Count >= _perWindow)
            return false;

        _places[place] = (spent.End, spent.Count + 1);
        return true;
    }

    private void Prune(TimeSpan now)
    {
        _expired.Clear();

        foreach (var (place, spent) in _places)
        {
            if (now >= spent.End)
                _expired.Add(place);
        }

        foreach (var place in _expired)
        {
            _places.Remove(place);
        }
    }
}

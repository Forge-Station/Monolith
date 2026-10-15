using System.Linq;

namespace Content.Server._Forge.KIAS;

public sealed class KiasEmissionGate
{
    private readonly Dictionary<(EntityUid Grid, string Key), (TimeSpan After, int Severity, int Suppressed)> _entries = new();
    public bool Allow(EntityUid grid, string key, TimeSpan now, float cooldown, int severity, out int suppressed)
    {
        var id = (grid, key);
        suppressed = 0;
        if (_entries.TryGetValue(id, out var previous))
        {
            if (now < previous.After && severity <= previous.Severity)
            {
                _entries[id] = (previous.After, previous.Severity, Math.Min(previous.Suppressed + 1, 1000000));
                return false;
            }
            suppressed = previous.Suppressed;
        }
        if (_entries.Count >= 4096 && !_entries.ContainsKey(id))
        {
            foreach (var entry in _entries.Where(entry => entry.Value.After <= now).Select(entry => entry.Key).ToArray())
                _entries.Remove(entry);
            if (_entries.Count >= 4096) return false;
        }
        _entries[id] = (now + TimeSpan.FromSeconds(Math.Clamp(cooldown, 0.1f, 600)), severity, 0);
        return true;
    }

    public void Remove(EntityUid grid)
    {
        foreach (var key in _entries.Keys.Where(key => key.Grid == grid).ToArray()) _entries.Remove(key);
    }
}

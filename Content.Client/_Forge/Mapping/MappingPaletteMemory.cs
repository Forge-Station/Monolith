using System.Linq;
using Content.Shared._Forge.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client._Forge.Mapping;

public sealed class MappingPaletteMemory
{
    public const int MaxRecents = 16;

    private readonly IConfigurationManager _cfg;
    private readonly List<MappingPaletteRef> _recents = new();
    private readonly HashSet<MappingPaletteRef> _favorites = new();

    public IReadOnlyList<MappingPaletteRef> Recents => _recents;
    public IReadOnlySet<MappingPaletteRef> Favorites => _favorites;

    public MappingPaletteMemory(IConfigurationManager cfg)
    {
        _cfg = cfg;
        LoadSet(_favorites, ForgeCVars.MappingPaletteFavorites);
        LoadList(_recents, ForgeCVars.MappingPaletteRecents);
    }

    public bool IsFavorite(MappingPaletteRef? id)
    {
        return id != null && _favorites.Contains(id.Value);
    }

    public bool ToggleFavorite(MappingPaletteRef id)
    {
        if (!_favorites.Add(id))
            _favorites.Remove(id);

        Save(_favorites, ForgeCVars.MappingPaletteFavorites);
        return _favorites.Contains(id);
    }

    public void PushRecent(MappingPaletteRef id)
    {
        _recents.Remove(id);
        _recents.Insert(0, id);

        while (_recents.Count > MaxRecents)
            _recents.RemoveAt(_recents.Count - 1);

        Save(_recents, ForgeCVars.MappingPaletteRecents);
    }

    private void LoadSet(HashSet<MappingPaletteRef> target, CVarDef<string> cvar)
    {
        target.Clear();
        foreach (var part in Split(_cfg.GetCVar(cvar)))
        {
            if (MappingPaletteRef.TryParse(part, out var parsed))
                target.Add(parsed);
        }
    }

    private void LoadList(List<MappingPaletteRef> target, CVarDef<string> cvar)
    {
        target.Clear();
        foreach (var part in Split(_cfg.GetCVar(cvar)))
        {
            if (MappingPaletteRef.TryParse(part, out var parsed) && !target.Contains(parsed))
                target.Add(parsed);
        }
    }

    private void Save(IEnumerable<MappingPaletteRef> values, CVarDef<string> cvar)
    {
        _cfg.SetCVar(cvar, string.Join(',', values.Select(v => v.ToString())));
    }

    private static IEnumerable<string> Split(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return part;
    }
}

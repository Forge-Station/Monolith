namespace Content.Client._Forge.Mapping;

public enum MappingPaletteTab : byte
{
    Entities = 0,
    Tiles = 1,
    Decals = 2,
    Favorites = 3,
    Recents = 4,
    Hierarchy = 5
}

public enum MappingPaletteKind : byte
{
    Entity,
    Tile,
    Decal
}

public enum MappingEyedropperMode : byte
{
    Auto,
    Entity,
    Tile,
    Decal
}

public readonly record struct MappingPaletteRef(MappingPaletteKind Kind, string Id)
{
    public override string ToString()
    {
        var prefix = Kind switch
        {
            MappingPaletteKind.Entity => "e",
            MappingPaletteKind.Tile => "t",
            MappingPaletteKind.Decal => "d",
            _ => "?"
        };

        return string.Concat(prefix, ":", Id);
    }

    public static bool TryParse(string value, out MappingPaletteRef result)
    {
        result = default;
        var split = value.Split(':', 2);
        if (split.Length != 2 || string.IsNullOrWhiteSpace(split[1]))
            return false;

        var kind = split[0] switch
        {
            "e" => MappingPaletteKind.Entity,
            "t" => MappingPaletteKind.Tile,
            "d" => MappingPaletteKind.Decal,
            _ => (MappingPaletteKind) 255
        };

        if ((byte) kind == 255)
            return false;

        result = new MappingPaletteRef(kind, split[1]);
        return true;
    }
}

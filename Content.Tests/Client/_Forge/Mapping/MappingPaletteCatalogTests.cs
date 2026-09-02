using Content.Client._Forge.Mapping;
using NUnit.Framework;

namespace Content.Tests.Client._Forge.Mapping;

[TestFixture]
[TestOf(typeof(MappingPaletteCatalog))]
public sealed class MappingPaletteCatalogTests
{
    [Test]
    public void GroupsTilesByKind()
    {
        Assert.That(MappingPaletteCatalog.GetTileGroup("Space"), Is.EqualTo("space"));
        Assert.That(MappingPaletteCatalog.GetTileGroup("Lattice"), Is.EqualTo("lattice"));
        Assert.That(MappingPaletteCatalog.GetTileGroup("FloorSteel"), Is.EqualTo("station"));
        Assert.That(MappingPaletteCatalog.GetTileGroup("FloorWood"), Is.EqualTo("wood"));
        Assert.That(MappingPaletteCatalog.GetTileGroup("FloorCarpetRed"), Is.EqualTo("carpet"));
        Assert.That(MappingPaletteCatalog.GetTileGroup("FloorAsteroidSand"), Is.EqualTo("planet"));
    }

    [Test]
    public void ParsesPaletteRefs()
    {
        Assert.That(MappingPaletteRef.TryParse("e:WallSolid", out var entity), Is.True);
        Assert.That(entity.Kind, Is.EqualTo(MappingPaletteKind.Entity));
        Assert.That(entity.Id, Is.EqualTo("WallSolid"));
        Assert.That(entity.ToString(), Is.EqualTo("e:WallSolid"));

        Assert.That(MappingPaletteRef.TryParse("t:FloorSteel", out var tile), Is.True);
        Assert.That(tile.Kind, Is.EqualTo(MappingPaletteKind.Tile));

        Assert.That(MappingPaletteRef.TryParse("d:dirty1", out var decal), Is.True);
        Assert.That(decal.Kind, Is.EqualTo(MappingPaletteKind.Decal));

        Assert.That(MappingPaletteRef.TryParse("nope", out _), Is.False);
    }
}

using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using Robust.Shared.Map;

namespace Content.Shared._WF.Barricades;

/// <summary>Construction condition: no barricade on the tile already faces the build direction.</summary>
[DataDefinition]
public sealed partial class TileBarricadeClear : IConstructionCondition
{
    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry
        {
            Localization = "rmc-construction-not-barricade-clear",
        };
    }

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var barricades = IoCManager.Resolve<IEntityManager>().System<BarricadeSystem>();
        return !barricades.HasBarricadeFacing(location, direction);
    }
}

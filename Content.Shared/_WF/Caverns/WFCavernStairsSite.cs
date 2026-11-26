using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Shared._WF.Caverns;

/// <summary>Construction condition for cavern stairs: a cavern floor, under ground the stairs can open.</summary>
[UsedImplicitly, DataDefinition]
public sealed partial class WFCavernStairsSite : IConstructionCondition
{
    /// <inheritdoc/>
    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        return IoCManager.Resolve<IEntityManager>().System<SharedWFCavernStairsSystem>().CanBuild(user, location, direction);
    }

    /// <inheritdoc/>
    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry { Localization = "wf-cavern-stairs-condition" };
    }
}

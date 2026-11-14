using Robust.Shared.Map;

namespace Content.Shared._WF.Caverns;

/// <summary>Where cavern stairs may be built: on a cavern floor, under ground the server finds it can open.</summary>
public abstract partial class SharedWFCavernStairsSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _xform = default!;

    /// <summary>Whether stairs facing a direction may be built at a location; a client knows only that it is a cavern.</summary>
    public virtual bool CanBuild(EntityUid user, EntityCoordinates location, Direction direction)
    {
        return HasComp<WFCavernLayerComponent>(_xform.GetMap(location));
    }

    /// <summary>The ground tile at the top of stairs facing a direction: the one beside the hole they come up through.</summary>
    public static Vector2i ExitOf(Vector2i index, Direction direction)
    {
        return index - direction.ToIntVec();
    }
}

using Content.Shared.FixedPoint; // Forge-Change

namespace Content.Server.Nutrition;

/// <summary>
/// Raised on a food being sliced.
/// Used by deep frier to apply friedness to slices (e.g. deep fried pizza)
/// </summary>
/// <remarks>
/// Not to be confused with upstream SliceFoodEvent which doesn't pass the slice entities, and is only raised once.
/// </remarks>
[ByRefEvent]
public sealed class FoodSlicedEvent : EntityEventArgs
{
    /// <summary>
    /// Who did the slicing?
    /// <summary>
    public EntityUid User;

    /// <summary>
    /// What has been sliced?
    /// <summary>
    /// <remarks>
    /// This could soon be deleted if there was not enough food left to
    /// continue slicing.
    /// </remarks>
    public EntityUid Food;

    /// <summary>
    /// What is the slice?
    /// <summary>
    public EntityUid Slice;

    public FoodSlicedEvent(EntityUid user, EntityUid food, EntityUid slice)
    {
        User = user;
        Food = food;
        Slice = slice;
    }
}
// Forge-Change-start
public enum IngestionTrackedType : byte
{
    Food = 0,
    Drink = 1
}

/// <summary>
/// Raised when an entity successfully consumes food or drink.
/// Amount is the ingested solution volume.
/// </summary>
[ByRefEvent]
public readonly record struct IngestionTrackedEvent(EntityUid Consumer, FixedPoint2 Amount, IngestionTrackedType Type);
// Forge-Change-end

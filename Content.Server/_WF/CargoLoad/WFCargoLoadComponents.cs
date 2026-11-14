namespace Content.Server._WF.CargoLoad;

/// <summary>A grid's cargo: its own load and the thrust factor its whole docked set flies with in space.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFCargoLoadComponent : Component
{
    /// <summary>Tonnes of cargo aboard this grid alone.</summary>
    [ViewVariables]
    public float OwnLoad;

    /// <summary>Tonnes of <see cref="OwnLoad"/> held as materials in silos, lathes and other material storage.</summary>
    [ViewVariables]
    public float OwnStored;

    /// <summary>Tonnes aboard the grid's cargo set.</summary>
    [ViewVariables]
    public float SetLoad;

    /// <summary>Share of thrust the set keeps in space.</summary>
    [ViewVariables]
    public float SpaceFactor = 1f;

    /// <summary>Tonnes of <see cref="SetLoad"/> held as stored materials.</summary>
    [ViewVariables]
    public float SetStored;

    /// <summary>Hull mass of the grid's cargo set, in the same units as the load.</summary>
    [ViewVariables]
    public float SetHull;
}

/// <summary>A helm on a loaded ship: its examine line, and which load warnings its pilots have had.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFCargoHelmComponent : Component
{
    /// <summary>Bands whose warning has gone out and not yet re-armed.</summary>
    [ViewVariables]
    public readonly HashSet<int> Told = new();

    /// <summary>Pilots already told the current load.</summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> Pilots = new();
}

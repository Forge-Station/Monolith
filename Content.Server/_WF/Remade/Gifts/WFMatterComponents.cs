namespace Content.Server._WF.Remade.Gifts;

/// <summary>A material silo holding stock a Monolith made. That stock is worth nothing and won't come out as sheets.</summary>
[RegisterComponent]
public sealed partial class WFMatterStockComponent : Component
{
    /// <summary>Units of each material, by id, that the Monolith made and nothing has used yet.</summary>
    [ViewVariables]
    public Dictionary<string, int> Tainted = new();

    /// <summary>Monolith-made units a machine has just drawn, waiting for the lathe that asked to claim them.</summary>
    [ViewVariables]
    public int Pending;

    /// <summary>An ejection of clean material is under way, so the draw it makes isn't a print.</summary>
    [ViewVariables]
    public bool Ejecting;
}

/// <summary>A lathe whose current print came from Monolith-made stock: what it makes can't be sold.</summary>
[RegisterComponent]
public sealed partial class WFMatterPrintComponent : Component;

/// <summary>Worth nothing to any buyer.</summary>
[RegisterComponent]
public sealed partial class WFNoResaleComponent : Component;

/// <summary>Raised by the material storage's eject handler before it takes material out, so Monolith-made stock can refuse.</summary>
[ByRefEvent]
public record struct WFMatterEjectAttemptEvent(EntityUid User, EntityUid Storage, string Material, int Volume)
{
    public bool Cancelled;
}

/// <summary>Raised on a lathe for each item it prints.</summary>
[ByRefEvent]
public readonly record struct WFLatheProducedEvent(EntityUid Product);

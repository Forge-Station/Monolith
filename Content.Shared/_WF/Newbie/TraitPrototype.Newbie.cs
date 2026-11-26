namespace Content.Shared.Traits;

public sealed partial class TraitPrototype
{
    /// <summary>Players with the newbie tag can't take this trait, such as one that makes them a kill target.</summary>
    [DataField]
    public bool NewbieBlocked { get; private set; }
}

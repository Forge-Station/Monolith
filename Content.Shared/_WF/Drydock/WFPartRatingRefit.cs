namespace Content.Shared._WF.Drydock;

/// <summary>Which ship machines a part rating refit covers.</summary>
public enum WFPartRefitTarget : byte
{
    LinearThruster,
    Gyroscope,
    Power,
}

/// <summary>
/// A refit that keeps a category of machines' parts at a rating per tier. Its units are machine parts.
/// </summary>
public sealed partial class WFPartRatingRefit : WFShipRefitEffect<WFPartRatingRefit>
{
    /// <summary>Machines covered.</summary>
    [DataField(required: true)]
    public WFPartRefitTarget Target;

    /// <summary>Part rating each tier installs, one per tier of the refit.</summary>
    [DataField(required: true)]
    public List<int> Ratings = new();
}

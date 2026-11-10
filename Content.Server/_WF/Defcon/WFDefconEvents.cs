using Content.Shared._WF.Defcon;

namespace Content.Server._WF.Defcon;

/// <summary>One source's share of the sector threat, with the reason shown in banners.</summary>
public readonly record struct WFDefconContribution(float Score, string Reason);

/// <summary>Raised (broadcast) each time the DEFCON is scored, for threat sources to add to.</summary>
public sealed class WFDefconGatherEvent(WFDefconProfilePrototype profile, TimeSpan now) : EntityEventArgs
{
    public readonly WFDefconProfilePrototype Profile = profile;
    public readonly TimeSpan Now = now;
    public readonly List<WFDefconContribution> Contributions = new();

    /// <summary>The calmest level any source allows.</summary>
    public int Floor = WFDefconTracker.Calmest;

    public void Add(float score, string reason)
    {
        if (score > 0f)
            Contributions.Add(new WFDefconContribution(score, reason));
    }
}

/// <summary>Raised (broadcast) after the DEFCON level changes, or DEFCON is turned on or off.</summary>
public sealed class WFDefconChangedEvent(int old, int level) : EntityEventArgs
{
    public readonly int Old = old;
    public readonly int Level = level;
}

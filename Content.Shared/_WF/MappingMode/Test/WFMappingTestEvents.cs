using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode.Test;

/// <summary>Starts the session's live test, or joins the one running. Sent by a present member.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTestStartRequest : EntityEventArgs;

/// <summary>
/// Leaves the live test and goes back to the observer. With <see cref="All"/> the owner ends it for everyone.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingTestStopRequest(bool all) : EntityEventArgs
{
    public readonly bool All = all;
}

/// <summary>A session's running live test, as its members see it.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTestInfo(TimeSpan since, TimeSpan ends, List<string> testers)
{
    /// <summary>Game time the test started.</summary>
    public readonly TimeSpan Since = since;

    /// <summary>Game time the test ends on its own.</summary>
    public readonly TimeSpan Ends = ends;

    /// <summary>Names of the members in the test right now.</summary>
    public readonly List<string> Testers = testers;
}

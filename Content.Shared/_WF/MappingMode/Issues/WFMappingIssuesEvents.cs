using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode.Issues;

/// <summary>
/// What a check is for: a shipyard vessel (one level, one grid), any map (every level) or a bare grid file, which the
/// map lint takes for a point of interest.
/// </summary>
[Serializable, NetSerializable]
public enum WFMappingIssueScope : byte
{
    Ship,
    Map,
    Grid,
}

/// <summary>How bad an issue is; a ship export is refused while an <see cref="Error"/> stands, any export over a map lint one.</summary>
[Serializable, NetSerializable]
public enum WFMappingIssueSeverity : byte
{
    Error,
    Warning,
    Note,
}

/// <summary>One finding: its rule, the Fluent title and arguments, and where it is when it has a place.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMappingIssue(
    WFMappingIssueSeverity Severity,
    string LocKey,
    List<WFMappingLocArg> LocArgs,
    NetEntity? Entity,
    NetCoordinates? Where,
    int? Depth,
    string RuleId)
{
    /// <summary>The arguments in the form <c>Loc.GetString</c> takes.</summary>
    public (string, object)[] Args()
    {
        var result = new (string, object)[LocArgs.Count];
        for (var i = 0; i < LocArgs.Count; i++)
        {
            var arg = LocArgs[i];
            result[i] = (arg.Name, arg.Text ?? (object) arg.Number);
        }

        return result;
    }
}

/// <summary>What the check looked over: counts across the session's levels and the appraisal of its grids.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMappingIssuesSummary(int Levels, int Grids, int Tiles, int Entities, double Appraisal);

/// <summary>A present member asks for the issues of their session.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingIssuesRequest(WFMappingIssueScope scope) : EntityEventArgs
{
    public readonly WFMappingIssueScope Scope = scope;
}

/// <summary>Server to client: the issues found, errors first, and the scan summary.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingIssuesEvent(
    WFMappingIssueScope scope,
    List<WFMappingIssue> issues,
    WFMappingIssuesSummary summary) : EntityEventArgs
{
    public readonly WFMappingIssueScope Scope = scope;
    public readonly List<WFMappingIssue> Issues = issues;
    public readonly WFMappingIssuesSummary Summary = summary;
}

/// <summary>
/// Server to client: a request was refused by the one-check-per-three-seconds throttle, so the client knows to try
/// again rather than wait for a reply that isn't coming.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingIssuesRefusedEvent(WFMappingIssueScope scope) : EntityEventArgs
{
    public readonly WFMappingIssueScope Scope = scope;
}

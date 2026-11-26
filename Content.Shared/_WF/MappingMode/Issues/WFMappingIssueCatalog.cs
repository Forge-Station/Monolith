using System.Diagnostics.CodeAnalysis;

namespace Content.Shared._WF.MappingMode.Issues;

/// <summary>A rule the issues checker applies: its id, severity, and the Fluent ids of its title and fix hint.</summary>
public readonly record struct WFMappingIssueRule(
    string Id,
    WFMappingIssueSeverity Severity,
    WFMappingIssueScope Scope)
{
    /// <summary>The Fluent id of the one-line title; it takes the issue's arguments.</summary>
    public string TitleKey => $"{WFMappingIssueCatalog.KeyPrefix}{Id}";

    /// <summary>The Fluent id of the hint on how to fix it.</summary>
    public string HintKey => $"{WFMappingIssueCatalog.KeyPrefix}{Id}-hint";
}

/// <summary>
/// Every rule the checker knows, so a client can render a report without knowing the rules. Pure: the server
/// decides when a rule fires. Rules with the <see cref="WFMappingIssueScope.Map"/> scope run for every scope;
/// <see cref="WFMappingIssueScope.Ship"/> ones only for a ship.
/// </summary>
public static class WFMappingIssueCatalog
{
    public const string KeyPrefix = "wf-mapping-issue-";

    /// <summary>Most issues one reply carries; a <see cref="More"/> note closes a longer list.</summary>
    public const int MaxIssues = 200;

    /// <summary>The closing note of a capped list; its <c>count</c> is how many were left out.</summary>
    public const string More = "more";

    public const string ShipGridCount = "ship-grid-count";
    public const string ShipNoName = "ship-no-name";
    public const string ShipNoFloor = "ship-no-floor";
    public const string ShipNoConsole = "ship-no-console";
    public const string ShipNoDock = "ship-no-dock";
    public const string ShipNoThruster = "ship-no-thruster";
    public const string ShipNoGyroscope = "ship-no-gyroscope";
    public const string ShipNoPowerSource = "ship-no-power-source";
    public const string ShipNoApc = "ship-no-apc";
    public const string ShipNoBattery = "ship-no-battery";
    public const string ShipNoAtmos = "ship-no-atmos";
    public const string ShipNoSpawnPoint = "ship-no-spawn-point";
    public const string ShipGunOffHardpoint = "ship-gun-off-hardpoint";
    public const string ShipAppraisalZero = "ship-appraisal-zero";
    public const string ShipPriceFloor = "ship-price-floor";

    public const string MapLevelEmpty = "map-level-empty";
    public const string MapDoubled = "map-doubled";
    public const string MapAnchoredNoTile = "map-anchored-no-tile";
    public const string MapLooseEntity = "map-loose-entity";
    public const string MapUnsaved = "map-unsaved";
    public const string MapDenied = "map-denied";
    public const string MapLint = "map-lint";

    private static readonly WFMappingIssueRule[] RuleList =
    {
        new(ShipGridCount, WFMappingIssueSeverity.Error, WFMappingIssueScope.Ship),
        new(ShipNoName, WFMappingIssueSeverity.Warning, WFMappingIssueScope.Ship),
        new(ShipNoFloor, WFMappingIssueSeverity.Error, WFMappingIssueScope.Ship),
        new(ShipNoConsole, WFMappingIssueSeverity.Error, WFMappingIssueScope.Ship),
        new(ShipNoDock, WFMappingIssueSeverity.Error, WFMappingIssueScope.Ship),
        new(ShipNoThruster, WFMappingIssueSeverity.Error, WFMappingIssueScope.Ship),
        new(ShipNoGyroscope, WFMappingIssueSeverity.Error, WFMappingIssueScope.Ship),
        new(ShipNoPowerSource, WFMappingIssueSeverity.Error, WFMappingIssueScope.Ship),
        new(ShipNoApc, WFMappingIssueSeverity.Error, WFMappingIssueScope.Ship),
        new(ShipNoBattery, WFMappingIssueSeverity.Error, WFMappingIssueScope.Ship),
        new(ShipNoAtmos, WFMappingIssueSeverity.Note, WFMappingIssueScope.Ship),
        new(ShipNoSpawnPoint, WFMappingIssueSeverity.Warning, WFMappingIssueScope.Ship),
        new(ShipGunOffHardpoint, WFMappingIssueSeverity.Error, WFMappingIssueScope.Ship),
        new(ShipAppraisalZero, WFMappingIssueSeverity.Error, WFMappingIssueScope.Ship),
        new(ShipPriceFloor, WFMappingIssueSeverity.Error, WFMappingIssueScope.Ship),
        new(MapLevelEmpty, WFMappingIssueSeverity.Warning, WFMappingIssueScope.Map),
        new(MapDoubled, WFMappingIssueSeverity.Warning, WFMappingIssueScope.Map),
        new(MapAnchoredNoTile, WFMappingIssueSeverity.Error, WFMappingIssueScope.Map),
        new(MapLooseEntity, WFMappingIssueSeverity.Warning, WFMappingIssueScope.Map),
        new(MapUnsaved, WFMappingIssueSeverity.Error, WFMappingIssueScope.Map),
        new(MapDenied, WFMappingIssueSeverity.Error, WFMappingIssueScope.Map),
        new(MapLint, WFMappingIssueSeverity.Error, WFMappingIssueScope.Map),
        new(More, WFMappingIssueSeverity.Note, WFMappingIssueScope.Map),
    };

    private static readonly Dictionary<string, WFMappingIssueRule> ById = BuildIndex();

    /// <summary>Every rule, in the order they are checked.</summary>
    public static IReadOnlyList<WFMappingIssueRule> Rules => RuleList;

    private static Dictionary<string, WFMappingIssueRule> BuildIndex()
    {
        var index = new Dictionary<string, WFMappingIssueRule>();
        foreach (var rule in RuleList)
        {
            index[rule.Id] = rule;
        }

        return index;
    }

    /// <summary>The rule with an id, if the catalog has it.</summary>
    public static bool TryGet(string id, out WFMappingIssueRule rule)
    {
        return ById.TryGetValue(id, out rule);
    }

    /// <summary>The rule with an id; throws on an unknown one.</summary>
    public static WFMappingIssueRule Get(string id)
    {
        return ById[id];
    }

    /// <summary>
    /// Puts issues in their reported order: errors, warnings, notes; within one severity by rule id, then by depth
    /// and grid position, placeless issues first.
    /// </summary>
    public static int Compare(WFMappingIssue a, WFMappingIssue b)
    {
        var order = a.Severity.CompareTo(b.Severity);
        if (order != 0)
            return order;

        order = string.CompareOrdinal(a.RuleId, b.RuleId);
        if (order != 0)
            return order;

        order = (a.Depth ?? int.MinValue).CompareTo(b.Depth ?? int.MinValue);
        if (order != 0)
            return order;

        if (a.Where is not { } wa)
            return b.Where is null ? 0 : -1;

        if (b.Where is not { } wb)
            return 1;

        order = wa.Position.Y.CompareTo(wb.Position.Y);
        return order != 0 ? order : wa.Position.X.CompareTo(wb.Position.X);
    }
}

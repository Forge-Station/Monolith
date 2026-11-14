using System.Linq;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.RoundSetup;

/// <summary>A crew member's condition as shown in public crew listings.</summary>
[Serializable, NetSerializable]
public enum WFCrewMemberStatus : byte { Waiting, Alive, Critical, Dead, Missing }

/// <summary>How much of a launched crew's ship is left.</summary>
[Serializable, NetSerializable]
public enum WFCrewShipCondition : byte { None, Intact, Damaged, Wrecked, Gone }

/// <summary>Pure rules for crew status summaries and join warnings.</summary>
public static class WFCrewStatusPolicy
{
    /// <summary>The share of the original deck above which a ship counts as intact.</summary>
    public const float IntactFraction = 0.9f;
    /// <summary>The share of the original deck below which a ship counts as wrecked.</summary>
    public const float WreckedFraction = 0.5f;

    /// <summary>Rates the largest surviving deck against the largest deck seen since launch.</summary>
    public static WFCrewShipCondition Condition(int tiles, int baseline)
    {
        if (tiles <= 0)
            return WFCrewShipCondition.Gone;
        if (baseline <= 0)
            return WFCrewShipCondition.Intact;
        var fraction = (float) tiles / baseline;
        return fraction >= IntactFraction ? WFCrewShipCondition.Intact
            : fraction >= WreckedFraction ? WFCrewShipCondition.Damaged
            : WFCrewShipCondition.Wrecked;
    }

    /// <summary>Spawned members who are alive or in critical condition.</summary>
    public static int Living(IEnumerable<WFCrewMemberStatus> statuses) =>
        statuses.Count(status => status is WFCrewMemberStatus.Alive or WFCrewMemberStatus.Critical);

    /// <summary>Members who have spawned into the round.</summary>
    public static int Deployed(IEnumerable<WFCrewMemberStatus> statuses) =>
        statuses.Count(status => status != WFCrewMemberStatus.Waiting);

    /// <summary>A started crew warrants a warning when every deployed member is down or its ship is lost.</summary>
    public static bool NeedsJoinWarning(bool started, IReadOnlyCollection<WFCrewMemberStatus> statuses, WFCrewShipCondition ship) =>
        started && (Deployed(statuses) > 0 && Living(statuses) == 0 ||
                    ship is WFCrewShipCondition.Wrecked or WFCrewShipCondition.Gone);
}

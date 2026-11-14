using Robust.Shared.Serialization;

namespace Content.Shared._WF.NpcCrew;

/// <summary>Scenario-specific flight speeds, handling limits and safe navigation distances.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class WFCrewNavigationSettings
{
    /// <summary>Travel speed limit in metres per second; 0 sets none, and the pilot flies as fast as the hull allows.</summary>
    [DataField] public float CruiseSpeed = 10f;
    /// <summary>Follow and escort speed allowance above the target's world speed, in metres per second; 0 sets no limit.</summary>
    [DataField] public float FollowSpeed = 10f;
    /// <summary>Loiter speed allowance above the target's world speed, in metres per second.</summary>
    [DataField] public float LoiterSpeed = 6f;
    /// <summary>Circle speed allowance above the target's world speed, in metres per second.</summary>
    [DataField] public float CircleSpeed = 4f;
    /// <summary>Attack speed allowance above the target's world speed, in metres per second; 0 sets no limit.</summary>
    [DataField] public float AttackSpeed = 6f;
    /// <summary>Position recovery speed while holding, in metres per second.</summary>
    [DataField] public float HoldCorrectionSpeed = 2f;
    /// <summary>Dock approach speed in metres per second.</summary>
    [DataField] public float DockApproachSpeed = 3f;
    /// <summary>Final dock approach speed in metres per second.</summary>
    [DataField] public float DockCreepSpeed = 1.5f;
    /// <summary>Departure speed and arrival threshold for clearing docks, in metres per second.</summary>
    [DataField] public float UndockSpeed = 3f;
    /// <summary>Approach speed near a navigation target, in metres per second; 0 slows nothing near a target.</summary>
    [DataField] public float NearTargetSpeed = 3f;
    /// <summary>Maximum rotation rate in radians per second.</summary>
    [DataField] public float MaximumTurnRate = 0.15f;
    /// <summary>Orbit waypoint look-ahead in degrees; smaller angles track the radius more closely.</summary>
    [DataField] public float OrbitLookaheadAngle = 10f;
    /// <summary>Available linear thrust fraction.</summary>
    [DataField] public float ThrustMultiplier = 0.45f;
    /// <summary>Available angular thrust fraction.</summary>
    [DataField] public float AngularThrustMultiplier = 0.5f;
    /// <summary>Additional clearance between hulls in metres.</summary>
    [DataField] public float NavigationClearance = 60f;
    /// <summary>Additional braking prediction time in seconds.</summary>
    [DataField] public float BrakingLookahead = 2f;
    /// <summary>Allowed speed deviation in metres per second.</summary>
    [DataField] public float SpeedTolerance = 0.25f;
    /// <summary>Maximum arrival speed in metres per second.</summary>
    [DataField] public float ArrivalSpeed = 0.5f;
    /// <summary>Drift in metres before returning to a held position.</summary>
    [DataField] public float HoldRange = 5f;
    /// <summary>Position tolerance in metres when returning to a held position.</summary>
    [DataField] public float HoldReturnRange = 1f;
    /// <summary>Allowed rotation while holding in radians per second.</summary>
    [DataField] public float HoldAngularSpeed = 0.02f;
    /// <summary>Formation slot tolerance in metres.</summary>
    [DataField] public float EscortRange = 8f;
    /// <summary>Distance in metres at which an escort matches the leader's heading.</summary>
    [DataField] public float EscortHeadingRange = 50f;
    /// <summary>Minimum attack orbit radius in metres, before hull clearance.</summary>
    [DataField] public float AttackRange = 350f;
    /// <summary>Additional hull radius used by ordinary collision avoidance, in metres.</summary>
    [DataField] public float EvasionBuffer = 20f;
    /// <summary>Projectile avoidance prediction in seconds.</summary>
    [DataField] public float EvasionLookahead = 8f;
    /// <summary>Projectile avoidance buffer near docks in metres.</summary>
    [DataField] public float DockEvasionBuffer = 3f;
    /// <summary>Projectile avoidance prediction near docks in seconds.</summary>
    [DataField] public float DockEvasionLookahead = 4f;
    /// <summary>Distance from the docking port before final approach, in metres.</summary>
    [DataField] public float DockStandoff = 40f;
    /// <summary>Dock alignment starts within this distance in metres.</summary>
    [DataField] public float DockAlignmentRange = 80f;
    /// <summary>Standoff position tolerance in metres.</summary>
    [DataField] public float DockStandoffRange = 2f;
    /// <summary>Final docking position tolerance in metres.</summary>
    [DataField] public float DockCreepRange = 0.3f;
    /// <summary>Allowed drift before final docking in metres per second.</summary>
    [DataField] public float DockSettleSpeed = 0.3f;
    /// <summary>Allowed rotation before final docking in radians per second.</summary>
    [DataField] public float DockSettleTurnRate = 0.05f;
    /// <summary>Time for a docking approach attempt in seconds, on top of the flight to the standoff at cruise speed.</summary>
    [DataField] public float DockApproachTimeout = 120f;
    /// <summary>Maximum time spent settling before final docking in seconds.</summary>
    [DataField] public float DockSettleTimeout = 30f;
    /// <summary>Maximum time for the final docking approach in seconds.</summary>
    [DataField] public float DockCreepTimeout = 60f;

    /// <summary>Shared editor metadata and bounds for every flight setting.</summary>
    public static readonly IReadOnlyList<WFCrewNavigationField> Fields = new WFCrewNavigationField[]
    {
        new("cruise-speed", "speeds", s => s.CruiseSpeed, (s, v) => s.CruiseSpeed = v, 0f, 500f),
        new("follow-speed", "speeds", s => s.FollowSpeed, (s, v) => s.FollowSpeed = v, 0f, 500f),
        new("loiter-speed", "speeds", s => s.LoiterSpeed, (s, v) => s.LoiterSpeed = v, 0.1f, 500f),
        new("circle-speed", "speeds", s => s.CircleSpeed, (s, v) => s.CircleSpeed = v, 0.1f, 500f),
        new("attack-speed", "speeds", s => s.AttackSpeed, (s, v) => s.AttackSpeed = v, 0f, 500f),
        new("hold-correction-speed", "speeds", s => s.HoldCorrectionSpeed, (s, v) => s.HoldCorrectionSpeed = v, 0.1f, 500f),
        new("near-target-speed", "speeds", s => s.NearTargetSpeed, (s, v) => s.NearTargetSpeed = v, 0f, 500f),
        new("maximum-turn-rate", "handling", s => s.MaximumTurnRate, (s, v) => s.MaximumTurnRate = v, 0.001f, 10f),
        new("orbit-lookahead-angle", "handling", s => s.OrbitLookaheadAngle, (s, v) => s.OrbitLookaheadAngle = v, 1f, 45f),
        new("thrust-multiplier", "handling", s => s.ThrustMultiplier, (s, v) => s.ThrustMultiplier = v, 0.01f, 10f),
        new("angular-thrust-multiplier", "handling", s => s.AngularThrustMultiplier, (s, v) => s.AngularThrustMultiplier = v, 0.01f, 10f),
        new("braking-lookahead", "handling", s => s.BrakingLookahead, (s, v) => s.BrakingLookahead = v, 0f, 120f),
        new("speed-tolerance", "handling", s => s.SpeedTolerance, (s, v) => s.SpeedTolerance = v, 0f, 500f),
        new("arrival-speed", "handling", s => s.ArrivalSpeed, (s, v) => s.ArrivalSpeed = v, 0.01f, 500f),
        new("hold-angular-speed", "handling", s => s.HoldAngularSpeed, (s, v) => s.HoldAngularSpeed = v, 0f, 10f),
        new("navigation-clearance", "clearances", s => s.NavigationClearance, (s, v) => s.NavigationClearance = v, 0f, 5000f),
        new("hold-range", "clearances", s => s.HoldRange, (s, v) => s.HoldRange = v, 0f, 5000f),
        new("hold-return-range", "clearances", s => s.HoldReturnRange, (s, v) => s.HoldReturnRange = v, 0f, 5000f),
        new("escort-range", "clearances", s => s.EscortRange, (s, v) => s.EscortRange = v, 0f, 5000f),
        new("escort-heading-range", "clearances", s => s.EscortHeadingRange, (s, v) => s.EscortHeadingRange = v, 0f, 5000f),
        new("attack-range", "clearances", s => s.AttackRange, (s, v) => s.AttackRange = v, 0f, 5000f),
        new("evasion-buffer", "clearances", s => s.EvasionBuffer, (s, v) => s.EvasionBuffer = v, 0f, 5000f),
        new("evasion-lookahead", "clearances", s => s.EvasionLookahead, (s, v) => s.EvasionLookahead = v, 0f, 120f),
        new("dock-approach-speed", "docking", s => s.DockApproachSpeed, (s, v) => s.DockApproachSpeed = v, 0.1f, 500f),
        new("dock-creep-speed", "docking", s => s.DockCreepSpeed, (s, v) => s.DockCreepSpeed = v, 0.1f, 500f),
        new("undock-speed", "docking", s => s.UndockSpeed, (s, v) => s.UndockSpeed = v, 0.1f, 500f),
        new("dock-evasion-buffer", "docking", s => s.DockEvasionBuffer, (s, v) => s.DockEvasionBuffer = v, 0f, 5000f),
        new("dock-evasion-lookahead", "docking", s => s.DockEvasionLookahead, (s, v) => s.DockEvasionLookahead = v, 0f, 120f),
        new("dock-standoff", "docking", s => s.DockStandoff, (s, v) => s.DockStandoff = v, 0f, 5000f),
        new("dock-alignment-range", "docking", s => s.DockAlignmentRange, (s, v) => s.DockAlignmentRange = v, 0f, 5000f),
        new("dock-standoff-range", "docking", s => s.DockStandoffRange, (s, v) => s.DockStandoffRange = v, 0f, 5000f),
        new("dock-creep-range", "docking", s => s.DockCreepRange, (s, v) => s.DockCreepRange = v, 0f, 5000f),
        new("dock-settle-speed", "docking", s => s.DockSettleSpeed, (s, v) => s.DockSettleSpeed = v, 0.01f, 500f),
        new("dock-settle-turn-rate", "docking", s => s.DockSettleTurnRate, (s, v) => s.DockSettleTurnRate = v, 0.001f, 10f),
        new("dock-approach-timeout", "docking", s => s.DockApproachTimeout, (s, v) => s.DockApproachTimeout = v, 1f, 86400f),
        new("dock-settle-timeout", "docking", s => s.DockSettleTimeout, (s, v) => s.DockSettleTimeout = v, 1f, 86400f),
        new("dock-creep-timeout", "docking", s => s.DockCreepTimeout, (s, v) => s.DockCreepTimeout = v, 1f, 86400f),
    };

    /// <summary>Copies values without sharing a mutable scenario or prototype instance.</summary>
    public WFCrewNavigationSettings Clone()
    {
        var copy = new WFCrewNavigationSettings();
        foreach (var field in Fields)
            field.Set(copy, field.Get(this));
        return copy;
    }

    /// <summary>Rejects invalid numbers, unsupported limits and contradictory hold thresholds.</summary>
    public bool IsValid()
    {
        foreach (var field in Fields)
        {
            var value = field.Get(this);
            if (!float.IsFinite(value) || value < field.Min || value > field.Max)
                return false;
        }
        return HoldReturnRange <= HoldRange;
    }
}

/// <summary>One editable setting and its allowed range; delegates are local editor metadata.</summary>
public sealed record WFCrewNavigationField(string Id, string Category,
    Func<WFCrewNavigationSettings, float> Get, Action<WFCrewNavigationSettings, float> Set, float Min, float Max);

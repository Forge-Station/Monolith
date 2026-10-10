using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ColdWar;

/// <summary>
/// What the war declaration rule keeps for the round: the declarator cooldowns and the opening announcement.
/// </summary>
[RegisterComponent]
public sealed partial class WFColdWarComponent : Component
{
    /// <summary>How long a faction waits between two uses of its declarator.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    /// <summary>When each faction may next use its declarator.</summary>
    [DataField]
    public Dictionary<ProtoId<CompanyPrototype>, TimeSpan> NextUse = new();

    /// <summary>How long into the round the standing war level is announced.</summary>
    [DataField]
    public TimeSpan AnnounceDelay = TimeSpan.FromMinutes(1);

    /// <summary>When the standing war level is announced. Null once it has been.</summary>
    [DataField]
    public TimeSpan? AnnounceAt;
}

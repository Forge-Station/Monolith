using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>
/// A chop-shop drydock console: it only breaks hulls, pays a share of the ship's value in thalers printed by the console
/// as the tiles come off, and posts a warrant on whoever breaks a ship that is neither theirs nor an outlaw's.
/// </summary>
[RegisterComponent]
public sealed partial class WFDrydockChopShopComponent : Component
{
    /// <summary>Share of the ship's value (its shipyard price, else its appraisal) paid for breaking all of it.</summary>
    [DataField]
    public float ValueShare = 0.8f;

    /// <summary>Reward on the warrant posted on whoever breaks lawful property; 0 posts none.</summary>
    [DataField]
    public int WarrantReward = 15000;

    /// <summary>NPC factions whose encounter ships anyone may break.</summary>
    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> OutlawFactions = new();
}

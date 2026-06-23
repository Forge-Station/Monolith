using Content.Shared.Humanoid;
using Content.Shared.Roles;
using Robust.Shared.Enums;


namespace Content.Shared._Forge.Trade;


[Serializable]
public sealed class ContractObjectiveConfigData
{
    public int AcceptTimeoutSeconds;
    public bool AllowStoreWorldTurnIn;
    public int? GhostRoleCharacterAge;
    public Gender? GhostRoleCharacterGender;
    public Color? GhostRoleCharacterHairColor;
    public Sex? GhostRoleCharacterSex;
    public Color? GhostRoleCharacterSkinColor;
    public NcGhostRoleCompletionMode GhostRoleCompletionMode = NcGhostRoleCompletionMode.DeadBodyTurnIn;
    public int GhostRoleSurvivalDurationSeconds;
    public int GhostRoleTakeDelaySeconds;

    public bool GivePinpointer = true;
    public int GuardCount;
    public NcHuntCompletionMode HuntCompletionMode = NcHuntCompletionMode.ConfirmedKill;

    // Spawned Hunt runtime metadata.
    public bool HuntEnabled;
    public List<NcHuntDebrisEntry> HuntDebris { get; set; } = new();
    public List<NcHuntDungeonEntry> HuntDungeons { get; set; } = new();
    public List<NcHuntDungeonExteriorTileEntry> HuntDungeonExteriorTiles { get; set; } = new();
    public List<NcHuntDungeonExteriorRockEntry> HuntDungeonExteriorRocks { get; set; } = new();
    public float HuntDebrisMinDistance;
    public float HuntDebrisMaxDistance;
    public float HuntDebrisSafetyRadius;
    public int HuntDebrisPlacementAttempts;
    public bool DroneHuntEnabled;
    public List<NcDroneHuntGridEntry> DroneHuntGrids { get; set; } = new();
    public List<string> DroneHuntCorePrototypes { get; set; } = new();
    public float DroneHuntMinDistance;
    public float DroneHuntMaxDistance;
    public float DroneHuntSafetyRadius;
    public int DroneHuntPlacementAttempts;
    public bool PreserveTargetOnComplete;
    public NcRetrievalClaimMode RetrievalClaimMode;
    public bool RetrievalConsumeCargo;
    public float RetrievalDestinationRadius;

    public NcRetrievalDestinationTargetType RetrievalDestinationType;
    public int RetrievalGuidanceMaxActivePinpointers;

    public bool RetrievalGuidancePinpointerEnabled;
    public NcRetrievalPinpointerTargetMode RetrievalGuidancePinpointerTarget;
    public bool RetrievalLockDeliveredCargo;
    public bool RetrievalProofConsumeOnRewardClaim;

    public bool RetrievalProofEnabled;
    public NcRetrievalProofOwnership RetrievalProofOwnership;
    public NcRetrievalProofReissuePolicy RetrievalProofReissue;
    public bool RetrievalRequireSpawnedEntities;
    public bool RetrievalSpaceSpawnEnabled;
    public float RetrievalSpaceSpawnMinDistance;
    public float RetrievalSpaceSpawnMaxDistance;
    public float RetrievalSpaceSpawnSafetyRadius;
    public int RetrievalSpaceSpawnPlacementAttempts;
    public bool RetrievalSpawnEnabled;
    public bool RetrievalSpawnFallbackToStore;

    // Inventory-delivery helper spawn metadata copied at contract creation time.
    public bool SpawnItems;
    public float SupplyReturnFraction;

    public ContractPointSelectorPrototype? SpawnPoint { get; set; }
    public ContractPointSelectorPrototype? DropoffPoint { get; set; }
    public string TargetPrototype { get; set; } = string.Empty;
    public string DeliverySpawnPrototype { get; set; } = string.Empty;
    public string GhostRole { get; set; } = string.Empty;
    public string ProofPrototype { get; set; } = string.Empty;
    public string GhostRolePrototype { get; set; } = string.Empty;
    public string GhostRoleName { get; set; } = string.Empty;
    public string GhostRoleDescription { get; set; } = string.Empty;
    public string GhostRoleRules { get; set; } = string.Empty;
    public List<JobRequirement> GhostRoleRequirements { get; set; } = new();
    public string GhostRoleCharacterName { get; set; } = string.Empty;
    public string GhostRoleCharacterHair { get; set; } = string.Empty;
    public List<string> GhostRolePerks { get; set; } = new();
    public string GhostRoleSurvivalBriefing { get; set; } = string.Empty;
    public string GhostRoleSurvivalObjectiveTitle { get; set; } = string.Empty;
    public string GhostRoleSurvivalObjectiveDescription { get; set; } = string.Empty;
    public string PinpointerPrototype { get; set; } = string.Empty;

    public string GuardPrototype { get; set; } = string.Empty;
    public string HuntBodyPrototype { get; set; } = string.Empty;
    public List<string> SpawnSpecific { get; set; } = new();

    // Retrieval route: copied from ncRetrievalRoutePreset at contract generation time.
    public string RetrievalRouteId { get; set; } = string.Empty;
    public ContractPointSelectorPrototype? RetrievalSpawnPoint { get; set; }
    public string RetrievalDestinationId { get; set; } = string.Empty;
    public ContractPointSelectorPrototype? RetrievalDestinationPoint { get; set; }
    public string RetrievalGuidancePinpointerPrototype { get; set; } = string.Empty;
    public string RetrievalSourceHint { get; set; } = string.Empty;
    public string RetrievalDestinationHint { get; set; } = string.Empty;
}

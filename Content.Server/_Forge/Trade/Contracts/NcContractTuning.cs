using System.Numerics;

namespace Content.Server._Forge.Trade;

internal static class NcContractTuning
{
    public const int DefaultObjectiveStageGoal = 1;

    public const string DefaultContractPinpointerPrototypeId = "PinpointerUniversal";
    public const string DefaultTrackedDeliveryDropoffSignPrototypeId = "ForgeTradeContractDropoffSign";

    public const int MaxActiveContractPinpointers = 7;
    public const float GhostRoleStoreDeliveryRange = 2.5f;
    public const float TrackedDeliveryStoreRange = 1.5f;
    public const float TrackedDeliveryDropoffRange = 1.5f;

    public const float HuntDebrisMinSpawnDistance = 10000f;
    public const float HuntDebrisMaxSpawnDistance = 15000f;
    public const float HuntDebrisSpawnSafetyRadius = 48f;
    public const int HuntDebrisSpawnPlacementAttempts = 24;
    public const int HuntDungeonExteriorPadding = 7;
    public const int HuntDungeonExteriorCoreClearance = 2;
    public const int HuntDungeonExteriorRimWidth = 2;
    public const int HuntDungeonExteriorMaxRockCount = 320;
    public const float HuntDungeonExteriorBlobDrawChance = 0.5f;
    public const float HuntDungeonExteriorInnerRockChance = 0.04f;
    public const float HuntDungeonExteriorRimRockChance = 0.65f;
    public const float RetrievalSpaceSpawnDistance = 2000f;
    public const float RetrievalSpaceSpawnSafetyRadius = 16f;
    public const int RetrievalSpaceSpawnPlacementAttempts = 24;

    public const float GuardSpawnRingScaleStep = 0.65f;
    public const float GuardSpawnJitterScale = 0.2f;
    public const float ContractNpcPlayerWakeRange = 96f;
    public static readonly TimeSpan TrackedDeliveryDropoffCheckInterval = TimeSpan.FromSeconds(0.5);
    public static readonly TimeSpan GhostRoleTimeoutCheckInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan ActiveContractDeadlineCheckInterval = TimeSpan.FromSeconds(1);

    public static readonly Vector2[] HuntGuardSpawnOffsets =
    {
        new(0.9f, 0f),
        new(-0.9f, 0f),
        new(0f, 0.9f),
        new(0f, -0.9f),
        new(0.75f, 0.75f),
        new(-0.75f, 0.75f),
        new(0.75f, -0.75f),
        new(-0.75f, -0.75f),
    };
}

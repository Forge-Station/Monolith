using Content.Shared._Forge.Trade;
using Content.Shared.FixedPoint;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private readonly record struct ClaimTakeEntry(
        EntityUid Root,
        EntityUid Entity,
        int Amount,
        bool IsStack,
        string TargetItem,
        PrototypeMatchMode MatchMode,
        string Solution = "drink",
        FixedPoint2 ReagentAmount = default);

    private enum ClaimFailureReason : byte
    {
        None = 0,
        StoreMissing,
        ContractMissing,
        NotTaken,
        NoValidTargets,
        InvalidTarget,
        NotEnoughItems,
        MissingCrate,
        MissingBody,
        MissingProof,
        ObjectiveNotCompleted,
        ObjectiveFailed,
        ExecutionFailed,
    }

    private readonly record struct ClaimAttemptResult(bool Success, ClaimFailureReason Reason, string? Details)
    {
        public static ClaimAttemptResult Ok()
        {
            return new ClaimAttemptResult(true, ClaimFailureReason.None, null);
        }

        public static ClaimAttemptResult Fail(ClaimFailureReason reason, string? details = null)
        {
            return new ClaimAttemptResult(false, reason, details);
        }
    }

    private readonly record struct PoolEntry(ContractRewardDef Def, string Key);

    private enum QuasiKeyKind : byte
    {
        Req,
        Tc,
        TReq,
        RAmount,
    }

    private enum ContractPoolCandidateKind : byte
    {
        Supply = 1,
        Retrieval = 2,
        Hunt = 3,
        GhostRole = 4,
        ArtifactStudy = 5,
        DroneHunt = 6,
    }

    private sealed class ContractPoolCandidate
    {
        public NcArtifactStudyContractPrototype? ArtifactStudy;
        public NcGhostRoleContractPrototype? GhostRole;
        public NcHuntContractPrototype? Hunt;
        public NcDroneHuntContractPrototype? DroneHunt;
        public string Id = string.Empty;
        public ContractPoolCandidateKind Kind;
        public string OfferPoolColor = string.Empty;
        public string OfferPoolId = string.Empty;
        public string OfferPoolName = string.Empty;
        public int OfferPoolOrder = int.MaxValue;
        public bool Repeatable = true;
        public NcRetrievalContractPrototype? Retrieval;
        public NcSupplyContractPrototype? Supply;
        public int Weight;
    }

    private readonly record struct QuasiKey(QuasiKeyKind Kind, EntityUid Store, string ProtoId, string? Extra);
}

[ByRefEvent]
public readonly record struct NcContractsChangedEvent;

// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Medical;

[Serializable, NetSerializable]
public enum HealthAnalyzerSolutionType : byte
{
    Bloodstream,
    Metabolites,
    Stomach,
    Lung,
}

[Serializable, NetSerializable]
public readonly record struct HealthAnalyzerReagentInfo(string Prototype, FixedPoint2 Quantity);

[Serializable, NetSerializable]
public sealed record HealthAnalyzerChemicalInfo(
    HealthAnalyzerSolutionType Type,
    List<HealthAnalyzerReagentInfo> Reagents);

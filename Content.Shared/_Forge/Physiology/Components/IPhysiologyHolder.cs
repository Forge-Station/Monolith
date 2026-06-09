using Content.Shared._Forge.Physiology.Disease;
using Content.Shared._Forge.Physiology.Tolerance;

namespace Content.Shared._Forge.Physiology;

/// <summary>
/// Interface for components storing diseases and tolerances
/// </summary>
public interface IPhysiologyHolder
{
    Dictionary<string, DiseaseData> Diseases { get; }
    Dictionary<string, ToleranceData> Tolerances { get; }
}

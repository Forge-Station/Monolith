using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Content.Shared._Forge.Body.Syndromes;

namespace Content.Shared._Forge.Body.Components;

/// <summary>
/// Interface for components storing diseases and tolerances
/// </summary>
public interface IPhysiologyHolder
{
    Dictionary<string, SyndromeData> Syndromes { get; }
    Dictionary<string, ToleranceData> Tolerances { get; }
}

// WOLFGATE (AUTODOC): one accessor onto the surgery list the server already keeps, so the pod can offer the
// same procedures the surgery menu lists. Kept out of the upstream file, which is unchanged.

using Robust.Shared.Prototypes;

namespace Content.Server._Shitmed.Medical.Surgery;

public sealed partial class SurgerySystem
{
    /// <summary>Every surgery prototype id, the same list RefreshUI walks.</summary>
    public IReadOnlyList<EntProtoId> AllSurgeries => _surgeries;
}

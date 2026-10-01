using Robust.Shared.Prototypes;

namespace Content.Shared.StatusEffectNew;

/// <summary>RT 277 has no EntitySystem.ProtoMan; this supplies it so the vendored files stay verbatim.</summary>
public sealed partial class StatusEffectsSystem
{
    [Dependency] private IPrototypeManager _protoMan = default!;

    /// <summary>Upstream's EntitySystem.ProtoMan, under the name the vendored files call.</summary>
    private IPrototypeManager ProtoMan => _protoMan;
}

using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Genetics;

/// <summary>
/// Raised on the target after a gene is activated.
/// </summary>
[ByRefEvent]
public readonly record struct GeneActivatedEvent(string GeneId);

/// <summary>
/// Raised on the target after a gene is removed or deactivated.
/// </summary>
[ByRefEvent]
public readonly record struct GeneDeactivatedEvent(string GeneId);

[Serializable, NetSerializable]
public sealed partial class DnaInjectDoAfterEvent : SimpleDoAfterEvent;

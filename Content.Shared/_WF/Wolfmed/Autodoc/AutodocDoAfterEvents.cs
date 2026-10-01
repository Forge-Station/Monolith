using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>Forcing the lid with a prying tool while the pod has it locked.</summary>
[Serializable, NetSerializable]
public sealed partial class AutodocPryDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Somebody lifting a conscious body into the pod: a few seconds it can walk away from, like a cryo pod.</summary>
[Serializable, NetSerializable]
public sealed partial class AutodocInsertDoAfterEvent : SimpleDoAfterEvent;

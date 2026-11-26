using Robust.Shared.Serialization;

namespace Content.Shared._WF.Remade;

/// <summary>Appearance data for a Remade with a shell it can open.</summary>
[Serializable, NetSerializable]
public enum WFRemadeShellVisuals : byte
{
    /// <summary>True while the shell is peeled back.</summary>
    Open,
}

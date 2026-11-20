using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetVents;

/// <summary>Appearance key for a breathing vent's phase.</summary>
[Serializable, NetSerializable]
public enum WFPlanetVentVisuals : byte
{
    Phase,
}

/// <summary>Where a breathing vent is in its cycle.</summary>
[Serializable, NetSerializable]
public enum WFPlanetVentPhase : byte
{
    Rest,
    Inhale,
    Exhale,
}

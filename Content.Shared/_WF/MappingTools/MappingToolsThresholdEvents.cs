using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingTools;

/// <summary>An atmos monitor's alarm thresholds; a null or missing one is a kind the monitor doesn't watch.</summary>
[Serializable, NetSerializable]
public sealed class MappingThresholdSet
{
    public AtmosAlarmThreshold? Temperature;
    public AtmosAlarmThreshold? Pressure;
    public Dictionary<Gas, AtmosAlarmThreshold> Gases = new();

    /// <summary>A deep copy, so edits to it don't reach the original.</summary>
    public MappingThresholdSet Clone()
    {
        var copy = new MappingThresholdSet
        {
            Temperature = Temperature == null ? null : new AtmosAlarmThreshold(Temperature),
            Pressure = Pressure == null ? null : new AtmosAlarmThreshold(Pressure),
        };

        foreach (var (gas, threshold) in Gases)
        {
            copy.Gases[gas] = new AtmosAlarmThreshold(threshold);
        }

        return copy;
    }
}

/// <summary>Asks for the thresholds of the atmos monitor or air alarm clicked, or the nearest one.</summary>
[Serializable, NetSerializable]
public sealed class MappingToolsThresholdsRequestEvent(NetCoordinates coordinates, NetEntity clicked) : EntityEventArgs
{
    public readonly NetCoordinates Coordinates = coordinates;
    public readonly NetEntity Clicked = clicked;
}

/// <summary>
/// Server to client: the thresholds a monitor holds, or an air alarm's first linked monitor, and how many monitors
/// an Apply on <see cref="Target"/> sets.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsThresholdsEvent(NetEntity target, int monitors, MappingThresholdSet thresholds)
    : EntityEventArgs
{
    public readonly NetEntity Target = target;
    public readonly int Monitors = monitors;
    public readonly MappingThresholdSet Thresholds = thresholds;
}

/// <summary>
/// Sets the thresholds of every target that is an atmos monitor, and of every monitor an air alarm target lists.
/// A monitor only takes the kinds it already watches.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsSetThresholdsEvent(List<NetEntity> targets, MappingThresholdSet thresholds)
    : EntityEventArgs
{
    public readonly List<NetEntity> Targets = targets;
    public readonly MappingThresholdSet Thresholds = thresholds;
}

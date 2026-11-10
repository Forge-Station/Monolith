using System;
using System.Collections.Generic;
using System.Text;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Hardpoint;

/// <summary>The sprite layer states a vehicle's parts publish to clients.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleHardpointVisualsComponent : Component
{
    public List<VehicleHardpointLayerState> Layers = new();
}

/// <summary>The state of one hardpoint sprite layer.</summary>
[Serializable, NetSerializable, DataDefinition]
public partial record struct VehicleHardpointLayerState
{
    [DataField(required: true)]
    public string Layer { get; set; } = string.Empty;

    [DataField]
    public string State { get; set; } = string.Empty;

    public VehicleHardpointLayerState(string layer, string state)
    {
        Layer = layer;
        State = state;
    }
}

/// <summary>The layer states as one string, because a list of them cannot go through appearance data.</summary>
public static class VehicleHardpointLayerCodec
{
    /// <summary>Packs layer states into the one string that goes through appearance data.</summary>
    public static string Encode(List<VehicleHardpointLayerState> layers)
    {
        var builder = new StringBuilder();
        foreach (var layer in layers)
        {
            builder.Append(layer.Layer).Append('\t').Append(layer.State).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Unpacks layer states from their string.</summary>
    public static List<VehicleHardpointLayerState> Decode(string encoded)
    {
        var layers = new List<VehicleHardpointLayerState>();
        foreach (var line in encoded.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length == 2)
                layers.Add(new VehicleHardpointLayerState(parts[0], parts[1]));
        }

        return layers;
    }
}

/// <summary>Appearance key of the layer states.</summary>
[Serializable, NetSerializable]
public enum VehicleHardpointVisualsVisuals : byte
{
    Layers,
}

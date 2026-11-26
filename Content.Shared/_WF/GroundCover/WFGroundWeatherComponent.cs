using Robust.Shared.GameStates;

namespace Content.Shared._WF.GroundCover;

/// <summary>How wet a weathered map's open ground is, and the snow running totals its cover is laid from.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFGroundWeatherComponent : Component
{
    /// <summary>The tint of plain water, which the ground shader's puddles are drawn in unless the rain says otherwise.</summary>
    public static readonly Color Water = new(0.42f, 0.5f, 0.62f);

    /// <summary>The colour of the map's wet floor and puddles, as sent to clients in thirty-seconds of each channel.</summary>
    [DataField, AutoNetworkedField]
    public Color Tint = Water;

    /// <summary>The tint to the last step. It eases toward the rain falling and holds while the puddles dry.</summary>
    [DataField]
    public Color TintExact = Water;

    /// <summary>Darkening and sheen of open ground, from 0 to 1, as sent to clients.</summary>
    [DataField, AutoNetworkedField]
    public float Wetness;

    /// <summary>How far puddles have spread, from 0 to 1, as sent to clients.</summary>
    [DataField, AutoNetworkedField]
    public float Puddles;

    /// <summary>Wetness to the last step; <see cref="Wetness"/> follows it in steps so the map isn't dirtied every update.</summary>
    [DataField]
    public float WetnessExact;

    [DataField]
    public float PuddlesExact;

    /// <summary>Share of full wetness gained a second while it rains harder than the ground is wet.</summary>
    [DataField]
    public float WetRate = 1f / 60f;

    /// <summary>Share of full wetness lost a second once it rains less.</summary>
    [DataField]
    public float DryRate = 1f / 300f;

    [DataField]
    public float PuddleRate = 1f / 240f;

    [DataField]
    public float PuddleDryRate = 1f / 600f;

    /// <summary>Last sampled rain, as wetness at full strength summed over the map's weathers.</summary>
    [ViewVariables]
    public float Rain;

    /// <summary>Last sampled snowfall, in cover levels a minute.</summary>
    [ViewVariables]
    public float Snow;

    /// <summary>All snow fallen on the map so far, in cover levels. Chunks catch up from their own mark of it.</summary>
    [DataField]
    public double Fallen;

    /// <summary>Seconds without snowfall so far. Chunks melt by what passed since their own mark of it.</summary>
    [DataField]
    public double MeltClock;

    /// <summary>Cover a chunk first seen now starts with, in levels: what fell and hasn't melted in the map's air.</summary>
    [DataField]
    public float BaseDepth;

    /// <summary>When the map is next stepped.</summary>
    [ViewVariables]
    public TimeSpan NextUpdate;
}

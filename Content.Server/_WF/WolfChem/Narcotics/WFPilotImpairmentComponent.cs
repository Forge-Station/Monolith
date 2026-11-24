using System.Numerics;

namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>What a pilot's high has done to their stick so far: the lagging input, the sway and the next twitch.</summary>
[RegisterComponent]
public sealed partial class WFPilotImpairmentComponent : Component
{
    /// <summary>The input the ship is getting, trailing behind the pilot's.</summary>
    public Vector2 Strafe;
    public float Rotation;
    public float Brakes;

    /// <summary>Seconds at the helm, driving the sway.</summary>
    public float Time;

    /// <summary>Offsets this pilot's sway from anyone else's.</summary>
    public float Seed;

    /// <summary>Seconds until the next twitch, how long the current one has left and which way it turns.</summary>
    public float NextTwitch;
    public float TwitchLeft;
    public float TwitchTurn;
}

using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Birds;

/// <summary>A planet bird: perches and pecks, takes off when startled, flies over anything and lands on open ground.</summary>
[RegisterComponent]
public sealed partial class WFBirdComponent : Component
{
    /// <summary>Sprite state while perched or walking.</summary>
    [DataField] public string GroundState = "sit";

    /// <summary>Sprite state while airborne.</summary>
    [DataField] public string FlyState = "fly";

    [DataField] public string DeadState = "dead";

    /// <summary>How far up the body is drawn above its shadow while flying, in metres.</summary>
    [DataField] public float FlightLift = 0.6f;

    /// <summary>How many birds a flock spawns with, this one included.</summary>
    [DataField] public int MinFlock = 1;

    [DataField] public int MaxFlock = 1;

    [DataField] public float FlySpeed = 5f;

    /// <summary>Speed of the short hops between pecks.</summary>
    [DataField] public float WalkSpeed = 1.2f;

    /// <summary>Anything alive this close startles the flock.</summary>
    [DataField] public float NearRange = 2.5f;

    /// <summary>Anything moving faster than <see cref="ScareSpeed"/> this close startles the flock.</summary>
    [DataField] public float ScareRange = 6f;

    [DataField] public float ScareSpeed = 3f;

    /// <summary>Seconds on the ground between flights (min, max).</summary>
    [DataField] public Vector2 PerchTime = new(12, 30);

    /// <summary>Seconds a flock may circle its landing spot before settling (min, max).</summary>
    [DataField] public Vector2 CircleTime = new(0, 6);

    [DataField] public float CircleRadius = 3f;

    /// <summary>How far a flight goes, at most.</summary>
    [DataField] public float RoamRange = 14f;

    [DataField] public SoundSpecifier? TakeoffSound;

    [DataField] public SoundSpecifier? CallSound;

    /// <summary>Chance per decision (four a second) that the bird calls.</summary>
    [DataField] public float CallChance = 0.01f;

    // Runtime state, server only.

    [ViewVariables] public WFBirdState State = WFBirdState.Perched;

    /// <summary>The flock's leader; null for a leader or a lone bird.</summary>
    [ViewVariables] public EntityUid? Leader;

    /// <summary>Spawned by a flock rather than the fauna system, so the bird retires itself when nobody is near.</summary>
    [ViewVariables] public bool Member;

    [ViewVariables] public bool Dead;

    /// <summary>Hurt or otherwise involved; never retired.</summary>
    [ViewVariables] public bool Protected;

    [ViewVariables] public Vector2 FlockOffset;

    /// <summary>Where the bird is going, in its parent's local frame.</summary>
    [ViewVariables] public Vector2 Goal;

    [ViewVariables] public bool Moving;

    [ViewVariables] public Vector2 CircleCentre;

    [ViewVariables] public float CircleAngle;

    [ViewVariables] public TimeSpan StateEnd;

    [ViewVariables] public TimeSpan NextThink;

    [ViewVariables] public TimeSpan NextHop;

    [ViewVariables] public TimeSpan LastSeen;

    /// <summary>Collision layer and mask of each fixture before take-off.</summary>
    [ViewVariables] public Dictionary<string, (int Layer, int Mask)> SavedFixtures = new();
}

public enum WFBirdState : byte
{
    Perched,
    Flying,
    Circling,
    Diving,
    Climbing,
}

[Serializable, NetSerializable]
public enum WFBirdVisuals : byte
{
    Flying,
}

/// <summary>Sprite layer keys: the bird and its ground shadow.</summary>
public enum WFBirdLayers : byte
{
    Body,
    Shadow,
}

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>A crewman assigned to operate a ship's gunnery console.</summary>
[RegisterComponent]
public sealed partial class WFGunnerDutyComponent : Component
{
    [DataField] public EntityUid? Console;
    [DataField] public float Range = 3000f;

    /// <summary>The farthest the ship's guns can hit, from their ammunition; unlimited until measured.</summary>
    [ViewVariables] public float Reach = float.MaxValue;
    [ViewVariables] public TimeSpan NextReach;
    [ViewVariables] public bool AtConsole;

    /// <summary>When the costlier checks that the gunner still holds his post next run.</summary>
    [ViewVariables] public TimeSpan NextPostCheck;

    /// <summary>How far off the target the guns are laid right now, by the gunner's skill.</summary>
    [ViewVariables] public System.Numerics.Vector2 AimError;
    [ViewVariables] public TimeSpan NextAimError;
}

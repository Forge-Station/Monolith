using Robust.Shared.Audio;

namespace Content.Server._WF.Remade.Elites;

/// <summary>
/// The mender. Down with its limbs off, it sends tentacles to its severed limbs and drags them back onto itself, and gets up
/// once it is whole. A limb burnt away, or carried out of reach for long enough, leaves it down for good.
/// </summary>
[RegisterComponent]
public sealed partial class WFReclaimComponent : Component
{
    /// <summary>How far its tentacles reach, in metres.</summary>
    [DataField]
    public float Reach = 6f;

    /// <summary>How fast a limb is dragged back, in metres a second.</summary>
    [DataField]
    public float PullSpeed = 1.1f;

    /// <summary>How close a limb has to come to go back on.</summary>
    [DataField]
    public float Grab = 0.6f;

    /// <summary>Seconds it lies still after falling before the tentacles come out.</summary>
    [DataField]
    public float ReachDelay = 3f;

    /// <summary>How far a limb flies when it comes off, in metres: the least and the most.</summary>
    [DataField]
    public float FlingMin = 1.5f;

    [DataField]
    public float FlingMax = 3.5f;

    /// <summary>Seconds a limb may be out of reach, or held, before it is lost for good.</summary>
    [DataField]
    public float LostAfter = 8f;

    [DataField]
    public SoundSpecifier? AttachSound = new SoundCollectionSpecifier("WFRemadeUndyingRegen");

    [DataField]
    public SoundSpecifier? DragSound = new SoundCollectionSpecifier("WFEnvSquelch");

    /// <summary>When each limb went out of reach, by limb id.</summary>
    [ViewVariables]
    public Dictionary<string, TimeSpan> OutOfReach = new();

    /// <summary>When the tentacles come out, once it has fallen. Null while it stands.</summary>
    [ViewVariables]
    public TimeSpan? ReachAt;

    /// <summary>Whether it has started reaching since it last fell.</summary>
    [ViewVariables]
    public bool Reaching;

    [ViewVariables]
    public TimeSpan NextDragSound;

    /// <summary>Limbs it has pulled back on. Tests read it.</summary>
    [ViewVariables]
    public int Reattached;
}

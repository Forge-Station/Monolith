using Robust.Shared.Audio;

namespace Content.Server._WF.Remade.Elites;

/// <summary>
/// A Remade that gets back up after it dies, unless it was killed the right way. The hunter and the Undying have it. The
/// kill rules are checked at the moment of death.
/// </summary>
[RegisterComponent]
public sealed partial class WFRisesComponent : Component
{
    /// <summary>Seconds it lies dead before it gets up.</summary>
    [DataField]
    public float RiseDelay = 15f;

    /// <summary>Seconds after it rises in which a second death is the last. 0 means it always rises.</summary>
    [DataField]
    public float Cooldown;

    /// <summary>It stays dead if this share of its death threshold is burns. 0 means burns don't matter.</summary>
    [DataField]
    public float BurnKill;

    /// <summary>It stays dead if this many limbs are off when it dies. 0 means it doesn't matter.</summary>
    [DataField]
    public int SeveredKill;

    /// <summary>It stays dead if its head is off when it dies.</summary>
    [DataField]
    public bool HeadKills;

    /// <summary>It goes down at once, without being killed, once this many limbs are off. 0 means never.</summary>
    [DataField]
    public int CollapseSevered;

    /// <summary>The share of its health it has when it gets up.</summary>
    [DataField]
    public float RiseHealth = 0.5f;

    /// <summary>The most limbs it grows back as it gets up.</summary>
    [DataField]
    public int Regrow = 3;

    [DataField]
    public SoundSpecifier? RiseSound;

    /// <summary>
    /// It stays dead for good once it has taken this much heat damage since it fell. 0 means burning doesn't end it. Heat
    /// damage here is what is left after its armour.
    /// </summary>
    [DataField]
    public float BurnDown;

    /// <summary>It can't get up while it lies this close to a powered stasis bed. 0 means a bed doesn't hold it.</summary>
    [DataField]
    public float StasisRange;

    /// <summary>The heat damage it had when it fell.</summary>
    [ViewVariables]
    public float HeatAtFall;

    /// <summary>Whether a stasis bed is holding it down now.</summary>
    [ViewVariables]
    public bool Held;

    /// <summary>Whether it lies down for the last time. Set at death.</summary>
    [ViewVariables]
    public bool Final;

    /// <summary>When it gets up. Null while it isn't waiting to.</summary>
    [ViewVariables]
    public TimeSpan? RiseAt;

    /// <summary>When it last got up.</summary>
    [ViewVariables]
    public TimeSpan? LastRose;

    /// <summary>Times it has got up.</summary>
    [ViewVariables]
    public int Rises;
}

/// <summary>Raised on a downed mob when its time to rise comes. Cancel it to keep the mob down for now.</summary>
[ByRefEvent]
public record struct WFRiseAttemptEvent(bool Cancelled = false);

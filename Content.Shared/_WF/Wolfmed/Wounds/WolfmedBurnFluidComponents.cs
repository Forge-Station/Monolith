using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// A burn weeps: it loses blood volume (plasma) at a rate per point of severity once it is deep enough (M1b,
/// plan §3.7, OD11). No puddle: burns weep, they do not bleed. Declared on the wound's prototype.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedFluidLossBehavior : WoundBehavior
{
    /// <summary>Severity from which the wound weeps at all.</summary>
    [DataField]
    public FixedPoint2 From = FixedPoint2.New(20);

    /// <summary>Units of blood a second per point of severity.</summary>
    [DataField]
    public float PerSeverity;
}

/// <summary>Marks a wound whose prototype weeps, so the fluid-loss tick walks only those.</summary>
[RegisterComponent]
public sealed partial class WolfmedFluidLossComponent : Component;

/// <summary>
/// A burn under a dressing (ointment, burn pack, gel) or a graft. A dressing cuts its fluid loss and its
/// infection; only a graft stops the fluid loss. Growing past where it was treated takes the treatment off.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedDressedComponent : Component
{
    /// <summary>Grafted rather than dressed: no fluid loss at all.</summary>
    [DataField, AutoNetworkedField]
    public bool Grafted;

    /// <summary>The wound's severity when the treatment went on.</summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 TreatedSeverity;
}

/// <summary>The body's current burn fluid loss, for the analyzer and examine. Absent while nothing weeps.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedBurnFluidLossComponent : Component
{
    /// <summary>Units a second.</summary>
    [DataField, AutoNetworkedField]
    public float Rate;

    /// <summary>Loss not yet taken: the blood solution moves in hundredths of a unit.</summary>
    [ViewVariables]
    public float Owed;
}

/// <summary>A surgery step that grafts every weeping wound on the part: their fluid loss stops.</summary>
[RegisterComponent]
public sealed partial class WolfmedSurgeryGraftBurnsEffectComponent : Component;

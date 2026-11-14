using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.RMCClothing.Ghillie;

/// <summary>
/// A ghillie suit: worn as outer clothing, it lets the wearer settle into position and turn near invisible until
/// they move, are pulled, buckled or downed, or take it off. Firing makes them a little more visible each shot.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(WFGhillieSuitSystem))]
public sealed partial class WFGhillieSuitComponent : Component
{
    /// <summary>Whether the wearer is hidden.</summary>
    [DataField, AutoNetworkedField]
    public bool Enabled;

    /// <summary>The wearer's opacity once fully hidden.</summary>
    [DataField, AutoNetworkedField]
    public float Opacity = 0.01f;

    /// <summary>Opacity added each time the wearer fires a gun.</summary>
    [DataField, AutoNetworkedField]
    public float AddedOpacityOnShoot = 0.04f;

    /// <summary>How long settling into position takes.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan UseDelay = TimeSpan.FromSeconds(4);

    /// <summary>How long the wearer takes to fade out once settled.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan InvisibilityDelay = TimeSpan.FromSeconds(4);

    /// <summary>How long the wearer takes to fade back out after a shot.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan InvisibilityBreakDelay = TimeSpan.FromSeconds(5);

    [DataField]
    public EntProtoId Action = "WFActionGhilliePreparePosition";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;
}

/// <summary>On a wearer hidden by a ghillie suit; anything that breaks the cover is raised here.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(WFGhillieSuitSystem))]
public sealed partial class WFGhillieHiddenComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Suit;

    /// <summary>Whether hiding added the turn-invisible component, so revealing removes only what it added.</summary>
    [DataField]
    public bool AddedTurnInvisible;
}

[Serializable, NetSerializable]
public sealed partial class WFGhillieSuitDoAfterEvent : SimpleDoAfterEvent;

public sealed partial class WFGhilliePreparePositionEvent : InstantActionEvent;

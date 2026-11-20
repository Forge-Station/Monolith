using Content.Shared.Actions;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Lattice;

/// <summary>Someone who answered the song: the Choir's own, at home past the Lattice. No Drift or garble touches them; only the Monolith's dread still does.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFChoirMemberComponent : Component
{
    /// <summary>The Choir's mark over their head, seen by ghosts, admins and other members.</summary>
    [DataField]
    public ProtoId<FactionIconPrototype> StatusIcon = "WFChoirFaction";

    /// <summary>When they died, while they lie dead. Server only.</summary>
    [ViewVariables]
    public TimeSpan? DiedAt;

    /// <summary>Whether their body has been set turning. Server only.</summary>
    [ViewVariables]
    public bool Turning;
}

/// <summary>Someone deep in Drift who has been offered the song, until it fades.</summary>
[RegisterComponent]
public sealed partial class WFChoirOfferComponent : Component
{
    [ViewVariables]
    public TimeSpan Expires;

    [ViewVariables]
    public EntityUid? Action;
}

/// <summary>Raised on the player who uses the offer's action: they join the Choir.</summary>
public sealed partial class WFChoirAnswerEvent : InstantActionEvent;

/// <summary>Settings for joining the Choir.</summary>
[CVarDefs]
public sealed class WFChoirCVars
{
    /// <summary>Whether players deep in Drift are offered the Choir.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.lattice.choir", true, CVar.SERVERONLY);

    /// <summary>Fewest players online before anyone is offered it.</summary>
    public static readonly CVarDef<int> MinPlayers =
        CVarDef.Create("wf.lattice.choir_min_players", 15, CVar.SERVERONLY);

    /// <summary>Most Choir players at once.</summary>
    public static readonly CVarDef<int> MaxMembers =
        CVarDef.Create("wf.lattice.choir_max", 3, CVar.SERVERONLY);

    /// <summary>Least Drift before the song is offered.</summary>
    public static readonly CVarDef<float> MinDrift =
        CVarDef.Create("wf.lattice.choir_min_drift", 80f, CVar.SERVERONLY);
}

using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>
/// M2 (OD8 (b), plan §5.4): a body held helpless but stable, Unconscious or shut down with nothing getting worse. After
/// <c>wolfmed.dormant_offer_seconds</c> it is flagged in distress on medical HUDs and its player is offered "wait as a
/// ghost", a ghost that can always return. Anything starting to get worse withdraws both at once.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedDormantComponent : Component
{
    /// <summary>Medical HUDs flag the body: stable, helpless and waiting for someone.</summary>
    [AutoNetworkedField]
    public bool Distress;

    /// <summary>Server: seconds of continuous stable helplessness so far.</summary>
    [ViewVariables]
    public float StableSeconds;

    /// <summary>Server: the "wait as a ghost" action, while it is offered.</summary>
    [ViewVariables]
    public EntityUid? Action;

    /// <summary>Server: the player took the offer and is out as a returnable ghost.</summary>
    [ViewVariables]
    public bool Waiting;

    /// <summary>Server: the routes the waiting ghost has already been told about, so it hears each once.</summary>
    [ViewVariables]
    public WolfmedRoutes Told;

    /// <summary>Server: the last line sent to the waiting ghost. Tests and admins read it.</summary>
    [ViewVariables]
    public string LastLine = string.Empty;
}

/// <summary>M2 (OD8): leave the stable body as a ghost that can come back.</summary>
public sealed partial class WolfmedWaitAsGhostActionEvent : InstantActionEvent;

using Content.Shared._WF.Hailing;

namespace Content.Server._WF.Hailing;

/// <summary>
/// Broadcast when the hail or call at an automated console stops, so whatever drives that ship can carry on.
/// It comes after the hail is torn down, so a listener can hail again at once. Not raised if the console
/// itself is deleted.
/// </summary>
/// <param name="Console">The automated console.</param>
/// <param name="Grid">Its ship, if it still has one.</param>
/// <param name="PeerGrid">The ship on the other end, if there was one.</param>
/// <param name="Reason">Why it stopped, from this console's side.</param>
/// <param name="Connected">The hail had been answered.</param>
[ByRefEvent]
public readonly record struct HailCallEndedEvent(
    EntityUid Console,
    EntityUid? Grid,
    EntityUid? PeerGrid,
    HailEndReason Reason,
    bool Connected);

/// <summary>Broadcast when a hail from an automated console is answered and the channel opens.</summary>
/// <param name="Console">The automated caller's console.</param>
/// <param name="Grid">Its ship.</param>
/// <param name="PeerGrid">The ship that answered, if it has one.</param>
[ByRefEvent]
public readonly record struct HailAnsweredEvent(EntityUid Console, EntityUid? Grid, EntityUid? PeerGrid);

/// <summary>Broadcast when the person at a console picks one of the answers put in front of them.</summary>
/// <param name="Offerer">The console that made the offer.</param>
/// <param name="OffererGrid">Its ship, if it has one.</param>
/// <param name="Chooser">The console the answer was picked at.</param>
/// <param name="ChooserGrid">Its ship.</param>
/// <param name="ChooserEntity">Who picked.</param>
/// <param name="Id">The picked option's id.</param>
[ByRefEvent]
public readonly record struct HailOptionChosenEvent(
    EntityUid Offerer,
    EntityUid? OffererGrid,
    EntityUid Chooser,
    EntityUid? ChooserGrid,
    EntityUid ChooserEntity,
    string Id);

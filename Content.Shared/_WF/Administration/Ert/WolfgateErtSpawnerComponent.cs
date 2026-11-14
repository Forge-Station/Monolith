namespace Content.Shared._WF.Administration.Ert;

/// <summary>
/// One ERT slot waiting for a ghost. Taking it builds a body from the player's character (or a random allowed
/// species), dresses it and sets up its ID. Filled in by the server when an admin spawns a team.
/// </summary>
[RegisterComponent]
public sealed partial class WolfgateErtSpawnerComponent : Component
{
    /// <summary>
    /// Which spawned team this slot belongs to, for sign-ups.
    /// </summary>
    [ViewVariables]
    public int TeamId;

    [ViewVariables]
    public bool Leader;

    /// <summary>
    /// The team's ship, if one was spawned: whoever takes this place is registered to it.
    /// </summary>
    [ViewVariables]
    public EntityUid? Ship;

    [ViewVariables]
    public string TeamName = string.Empty;

    /// <summary>
    /// Job title written on the ID.
    /// </summary>
    [ViewVariables]
    public string Title = string.Empty;

    /// <summary>
    /// Starting gear prototype id.
    /// </summary>
    [ViewVariables]
    public string Outfit = string.Empty;

    /// <summary>
    /// Allowed species ids. Empty allows any.
    /// </summary>
    [ViewVariables]
    public List<string> Species = new();

    /// <summary>
    /// Ignore the player's character and <see cref="Species"/>; spawn the default bald male human with a random name.
    /// </summary>
    [ViewVariables]
    public bool GenericHumans;

    [ViewVariables]
    public List<string> AccessGroups = new();

    [ViewVariables]
    public bool KeepOutfitAccess = true;

    /// <summary>
    /// Claimed this tick; the marker is deleted at the end of it. Stops a second ghost taking the same slot.
    /// </summary>
    [ViewVariables]
    public bool Taken;
}

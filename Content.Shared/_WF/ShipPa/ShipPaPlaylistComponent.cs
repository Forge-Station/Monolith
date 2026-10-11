using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipPa;

/// <summary>
/// A ship's PA playlist: the track its speakers are playing from the queue and the links waiting behind it.
/// Lives on the grid while anything is playing or queued. Clients get short labels only; the links stay on the server.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipPaPlaylistComponent : Component
{
    /// <summary>Most links that may wait behind the playing track.</summary>
    public const int MaxQueued = 8;

    /// <summary>The playing track: its title once known, otherwise its shortened link.</summary>
    [ViewVariables, AutoNetworkedField]
    public ShipPaPlaylistEntry? Current;

    /// <summary>The waiting links in play order.</summary>
    [ViewVariables, AutoNetworkedField]
    public List<ShipPaPlaylistEntry> Queue = new();

    /// <summary>Server only. The link behind <see cref="Current"/>.</summary>
    [ViewVariables]
    public ShipPaQueuedLink? Playing;

    /// <summary>Server only. The waiting links in play order; <see cref="Queue"/> is built from it.</summary>
    [ViewVariables]
    public List<ShipPaQueuedLink> Links = new();

    /// <summary>Server only. When the pilot may skip again, since every skip starts a fresh fetch.</summary>
    [ViewVariables]
    public TimeSpan NextSkip;
}

/// <summary>A track or waiting link as clients see it: an id to name it by and a short label.</summary>
[Serializable, NetSerializable]
public readonly record struct ShipPaPlaylistEntry(int Id, string Label);

/// <summary>Server only. A link waiting its turn, with what the server needs to start it and report back.</summary>
public sealed class ShipPaQueuedLink
{
    public int Id;
    public string Url = string.Empty;
    public string Label = string.Empty;
    public string Requester = string.Empty;
    public ICommonSession? Session;
}

using Robust.Shared.Serialization;

namespace Content.Shared._WF.Audio.InternetSound;

/// <summary>
/// Wire format for admin internet sounds. The fetched Ogg travels over the engine's bulk transfer channel
/// under <see cref="TransferKey"/> and is mounted into <see cref="InternetSoundResources"/> at both ends, so
/// the server can then play it as an ordinary resource. Everything else is a plain network event.
/// Payload: int32 header length, header (int32 id, string title, string requester, bool isPa, BinaryWriter
/// encoding), then the Ogg Vorbis file. The header comes first so clients can show the radio while the audio is still arriving.
/// </summary>
public static class InternetSoundProtocol
{
    public const string TransferKey = "WolfgateInternetSound";

    /// <summary>
    /// Clients reject longer headers as corrupt.
    /// </summary>
    public const int MaxHeaderBytes = 64 * 1024;

    /// <summary>
    /// Clients abort transfers larger than this. Well above the server's own size limit.
    /// </summary>
    public const int MaxPayloadBytes = 64 * 1024 * 1024;
}

/// <summary>
/// A client reporting that it has the track with this id mounted, or couldn't get it. Global admin sounds wait for
/// these; PA listeners independently join the shared timeline when their download arrives.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetSoundReadyEvent : EntityEventArgs
{
    public int Id;
    public bool Failed;

    public InternetSoundReadyEvent(int id, bool failed)
    {
        Id = id;
        Failed = failed;
    }
}

/// <summary>
/// The track with this id is now playing, so the radio can drop out of its loading state. Not sent for ship
/// PA tracks, which are diegetic and get no radio at all.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetSoundPlayingEvent : EntityEventArgs
{
    public int Id;

    public InternetSoundPlayingEvent(int id)
    {
        Id = id;
    }
}

/// <summary>
/// The server has stopped every stream of this track and clients may now free it. Sent after the audio
/// entities are gone; clients still wait for them to actually disappear before freeing the decoded buffer.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetSoundReleaseEvent : EntityEventArgs
{
    public int Id;

    public InternetSoundReleaseEvent(int id)
    {
        Id = id;
    }
}

/// <summary>
/// Closes the radio for this track. Id 0 closes whatever is showing.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetSoundStopEvent : EntityEventArgs
{
    public int Id;

    public InternetSoundStopEvent(int id)
    {
        Id = id;
    }
}

/// <summary>
/// Admin window asks for the current internet sound state. Ignored for non-admins.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetSoundStateRequestEvent : EntityEventArgs;

/// <summary>
/// One internet sound the server currently has in hand, for the admin window.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetSoundEntry
{
    public int Id;
    public string Title;

    /// <summary>Admin name, or the crew member who asked for it at a console.</summary>
    public string Requester;

    /// <summary>The ship it plays on, or null when it's going to everyone at once.</summary>
    public string? Ship;

    public NetEntity? Grid;

    public bool Fetching;
    public bool Sending;

    public InternetSoundEntry(int id, string title, string requester, string? ship, NetEntity? grid, bool fetching, bool sending)
    {
        Id = id;
        Title = title;
        Requester = requester;
        Ship = ship;
        Grid = grid;
        Fetching = fetching;
        Sending = sending;
    }
}

/// <summary>
/// Everything the server currently has in hand. Sent to admins whenever it changes, and on request.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetSoundStateEvent : EntityEventArgs
{
    public List<InternetSoundEntry> Tracks;

    /// <summary>A sound is already going out to everyone, so a second one would be refused.</summary>
    public bool GlobalBusy;

    public InternetSoundStateEvent(List<InternetSoundEntry> tracks, bool globalBusy)
    {
        Tracks = tracks;
        GlobalBusy = globalBusy;
    }
}

/// <summary>
/// Progress or error for the admin who requested an internet sound.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetSoundStatusEvent : EntityEventArgs
{
    public string Message;
    public bool IsError;

    public InternetSoundStatusEvent(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }
}

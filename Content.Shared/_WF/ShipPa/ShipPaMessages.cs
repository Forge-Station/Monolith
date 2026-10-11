using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipPa;

/// <summary>Pilot asked for a new situation code.</summary>
[Serializable, NetSerializable]
public sealed class ShipAlertCodeRequestMessage : BoundUserInterfaceMessage
{
    public string Code;

    public ShipAlertCodeRequestMessage(string code)
    {
        Code = code;
    }
}

/// <summary>Pilot sounded or secured from general quarters.</summary>
[Serializable, NetSerializable]
public sealed class ShipGeneralQuartersRequestMessage : BoundUserInterfaceMessage
{
    public bool Active;

    public ShipGeneralQuartersRequestMessage(bool active)
    {
        Active = active;
    }
}

/// <summary>Pilot typed a free-text announcement.</summary>
[Serializable, NetSerializable]
public sealed class ShipPaAnnounceRequestMessage : BoundUserInterfaceMessage
{
    public string Text;

    public ShipPaAnnounceRequestMessage(string text)
    {
        Text = text;
    }
}

/// <summary>Pilot queued a link to play over the ship's own speakers.</summary>
[Serializable, NetSerializable]
public sealed class ShipPaInternetSoundRequestMessage : BoundUserInterfaceMessage
{
    public string Url;

    public ShipPaInternetSoundRequestMessage(string url)
    {
        Url = url;
    }
}

/// <summary>Pilot stopped whatever the ship was playing and emptied its queue.</summary>
[Serializable, NetSerializable]
public sealed class ShipPaInternetSoundStopMessage : BoundUserInterfaceMessage;

/// <summary>What the pilot wants done to the ship's playlist.</summary>
[Serializable, NetSerializable]
public enum ShipPaPlaylistAction : byte
{
    /// <summary>End the playing track and start the next one.</summary>
    Skip,

    /// <summary>Take one waiting link out, named by <see cref="ShipPaPlaylistRequestMessage.Id"/>.</summary>
    Remove,

    /// <summary>Empty the queue and leave the playing track alone.</summary>
    Clear,
}

/// <summary>Pilot skipped the playing track, removed a waiting link or cleared the queue.</summary>
[Serializable, NetSerializable]
public sealed class ShipPaPlaylistRequestMessage : BoundUserInterfaceMessage
{
    public ShipPaPlaylistAction Action;

    /// <summary>The waiting entry to remove; ignored by the other actions.</summary>
    public int Id;

    public ShipPaPlaylistRequestMessage(ShipPaPlaylistAction action, int id = 0)
    {
        Action = action;
        Id = id;
    }
}

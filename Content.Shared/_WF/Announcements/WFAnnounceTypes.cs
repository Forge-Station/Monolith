using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Announcements;

/// <summary>Who hears an announcement.</summary>
public enum WFAnnounceTarget : byte
{
    Everyone,
    AdminMap,
}

/// <summary>Which sound plays with an announcement.</summary>
public enum WFAnnounceSoundMode : byte
{
    None,
    Default,
    Custom,
}

/// <summary>Colour scheme of a notification chip.</summary>
public enum WFAnnounceChipLevel : byte
{
    Info,
    Advisory,
    Warning,
    Custom,
}

/// <summary>Everything an admin filled in on the announcement window.</summary>
[Serializable, NetSerializable]
public sealed class WFAnnounceRequest
{
    public string Sender = string.Empty;
    public string Title = string.Empty;
    public string Body = string.Empty;
    public bool AllowMarkup;
    public Color Accent = Color.Gold;

    public WFAnnounceTarget Target;
    public bool PostToChat = true;

    public WFAnnounceSoundMode SoundMode = WFAnnounceSoundMode.Default;
    public string SoundPath = string.Empty;
    public float Volume = -2f;

    public bool ShowChip;
    public WFAnnounceChipLevel ChipLevel = WFAnnounceChipLevel.Advisory;
    public string ChipTitle = string.Empty;
    public string ChipText = string.Empty;
    public float ChipDuration = 15f;
    public bool ChipFlash;
}

/// <summary>A notification chip shown at the top of the screen.</summary>
[Serializable, NetSerializable]
public sealed class WFAnnouncementChipEvent : EntityEventArgs
{
    public WFAnnounceChipLevel Level;
    public Color Accent;
    public string Title = string.Empty;
    public string Text = string.Empty;
    public string Sender = string.Empty;

    /// <summary>Seconds on screen; zero keeps it until clicked away.</summary>
    public float Duration;

    public bool Flash;
}

/// <summary>The result of the last send, shown under the send button.</summary>
[Serializable, NetSerializable]
public sealed class WFAnnounceEuiState(string? status, bool error) : EuiStateBase
{
    public readonly string? Status = status;
    public readonly bool Error = error;
}

/// <summary>Sends the filled-in announcement.</summary>
[Serializable, NetSerializable]
public sealed class WFAnnounceSendMessage(WFAnnounceRequest request, bool closeAfter) : EuiMessageBase
{
    public readonly WFAnnounceRequest Request = request;
    public readonly bool CloseAfter = closeAfter;
}

/// <summary>Console command names.</summary>
public static class WFAnnounceCommands
{
    public const string Open = "rpannounce";
}

/// <summary>Limits shared by the window and the server's checks.</summary>
public static class WFAnnounceLimits
{
    public const int MaxSender = 64;
    public const int MaxTitle = 100;
    public const int MaxBody = 4000;
    public const int MaxChipTitle = 24;
    public const int MaxChipText = 200;
    public const float MinVolume = -20f;
    public const float MaxVolume = 10f;
    public const float MaxChipDuration = 600f;
}

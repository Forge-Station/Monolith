namespace Content.Shared.Chat;

public abstract partial class SharedChatSystem
{
    /// <summary>Shared entry point for emotes; only the server implementation does anything.</summary>
    public virtual void TryEmoteWithChat(
        EntityUid source,
        string emoteId,
        ChatTransmitRange range = ChatTransmitRange.Normal,
        bool hideLog = false,
        string? nameOverride = null,
        bool ignoreActionBlocker = false,
        bool forceEmote = false
        )
    {
    }
}

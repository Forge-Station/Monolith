using Content.Shared.Chat;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._WF.RMCCore.Chat;

// WOLFGATE: RMC's hivemind prefix rules (ChatGetPrefixEvent) and squad name colours are cut; the Xenonids module owns hivemind chat.
/// <summary>Chat helpers RMC-14 code calls from shared systems; the server sends the messages.</summary>
public abstract class SharedCMChatSystem : EntitySystem
{
    /// <summary>Applies RMC's word replacements for the speaker's faction.</summary>
    public virtual string SanitizeMessageReplaceWords(EntityUid source, string msg)
    {
        return msg;
    }

    public virtual void ChatMessageToOne(
        ChatChannel channel,
        string message,
        string wrappedMessage,
        EntityUid source,
        bool hideChat,
        INetChannel client,
        Color? colorOverride = null,
        bool recordReplay = false,
        string? audioPath = null,
        float audioVolume = 0,
        NetUserId? author = null)
    {
    }

    public void ChatMessageToOne(
        string message,
        EntityUid target,
        ChatChannel channel = ChatChannel.Local,
        bool hideChat = false,
        Color? colorOverride = null,
        bool recordReplay = false,
        string? audioPath = null,
        float audioVolume = 0,
        NetUserId? author = null)
    {
        if (!TryComp(target, out ActorComponent? actor))
            return;

        ChatMessageToOne(channel,
            message,
            message,
            default,
            hideChat,
            actor.PlayerSession.Channel,
            colorOverride,
            recordReplay,
            audioPath,
            audioVolume,
            author
        );
    }

    public virtual void ChatMessageToMany(
        string message,
        string wrappedMessage,
        Filter filter,
        ChatChannel channel,
        EntityUid source = default,
        bool hideChat = false,
        Color? colorOverride = null,
        bool recordReplay = false,
        string? audioPath = null,
        float audioVolume = 0,
        NetUserId? author = null)
    {
    }

    public virtual void Emote(
        EntityUid source,
        string message,
        string? nameOverride = null,
        bool checkRadioPrefix = true,
        bool ignoreActionBlocker = false)
    {
    }
}

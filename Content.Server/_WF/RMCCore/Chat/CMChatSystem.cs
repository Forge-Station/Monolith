using Content.Server.Chat.Managers;
using Content.Server.Speech.EntitySystems;
using Content.Server.Speech.Prototypes;
using Content.Shared._WF.RMCCore.Chat;
using Content.Shared._WF.Xenonids;
using Content.Shared.Chat;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.RMCCore.Chat;

// WOLFGATE: RMC's language, imaginary friend, radio-speaker and multi-broadcast recipient rules are cut (no upstream ChatSystem hooks).
/// <summary>Sends RMC-14's direct chat messages through the chat manager.</summary>
public sealed partial class CMChatSystem : SharedCMChatSystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ReplacementAccentSystem _wordreplacement = default!;

    private static readonly ProtoId<ReplacementAccentPrototype> ChatSanitize = "CMChatSanitize";
    private static readonly ProtoId<ReplacementAccentPrototype> MarineChatSanitize = "CMChatSanitizeMarine";
    private static readonly ProtoId<ReplacementAccentPrototype> XenoChatSanitize = "CMChatSanitizeXeno";

    public override string SanitizeMessageReplaceWords(EntityUid source, string msg)
    {
        // The sanitize accents come with the Xenonids prototypes; skip any that aren't loaded.
        if (_proto.HasIndex(ChatSanitize))
            msg = _wordreplacement.ApplyReplacements(msg, ChatSanitize);

        var factionSanitize = HasComp<XenoComponent>(source) ? XenoChatSanitize : MarineChatSanitize;
        if (_proto.HasIndex(factionSanitize))
            msg = _wordreplacement.ApplyReplacements(msg, factionSanitize);

        return msg;
    }

    public override void ChatMessageToOne(
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
        _chat.ChatMessageToOne(
            channel,
            message,
            wrappedMessage,
            source,
            hideChat,
            client,
            colorOverride,
            recordReplay,
            audioPath,
            audioVolume,
            author
        );
    }

    public override void ChatMessageToMany(
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
        _chat.ChatMessageToManyFiltered(
            filter,
            channel,
            message,
            wrappedMessage,
            source,
            hideChat,
            recordReplay,
            colorOverride,
            audioPath,
            audioVolume
        );
    }
}

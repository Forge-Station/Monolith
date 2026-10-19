using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WF.Xenonids.Hive;

/// <summary>
/// RMC's hivemind: a xeno's message with a radio prefix (<c>;</c> or <c>:</c>) goes to every member of its hive
/// instead of a radio channel. ChatSystem hands messages over through <see cref="TrySendHiveMessage"/>.
/// </summary>
public sealed partial class XenoHiveChatSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private SharedXenoHiveSystem _hive = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly Color HiveColor = Color.FromHex("#8b36a6");

    /// <summary>Sends a xeno's prefixed message to its hive. Returns false when the message is not hive chat.</summary>
    public bool TrySendHiveMessage(EntityUid source, string message)
    {
        if (!HasComp<XenoComponent>(source) || message.Length == 0)
            return false;

        string text;
        if (message[0] == ChatSystem.RadioCommonPrefix)
            text = message[1..];
        else if (message[0] == ChatSystem.RadioChannelPrefix)
            text = message.Length > 1 && !char.IsWhiteSpace(message[1]) ? message[2..] : message[1..];
        else
            return false;

        text = text.Trim();
        if (text.Length == 0)
            return true;

        text = char.ToUpperInvariant(text[0]) + text[1..];

        if (_hive.GetHive(source) is not { } hive)
        {
            _popup.PopupEntity(Loc.GetString("wf-xeno-hive-chat-no-hive"), source, source, PopupType.SmallCaution);
            return true;
        }

        var wrapped = Loc.GetString("wf-xeno-hive-chat-wrap",
            ("color", HiveColor.ToHex()),
            ("name", FormattedMessage.EscapeText(Name(source))),
            ("message", FormattedMessage.EscapeText(text)));

        var filter = Filter.Empty()
            .AddWhereAttachedEntity(e => _hive.IsMember(e, hive) || HasComp<GhostComponent>(e));

        _chat.ChatMessageToManyFiltered(filter, ChatChannel.Radio, text, wrapped, source, false, true, null);
        _adminLog.Add(LogType.Chat, LogImpact.Low, $"Hive chat from {ToPrettyString(source):user} to {ToPrettyString(hive):hive}: {text}");
        return true;
    }
}

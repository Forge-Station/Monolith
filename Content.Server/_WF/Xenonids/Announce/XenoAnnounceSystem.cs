using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared._WF.Xenonids.Announce;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server._WF.Xenonids.Announce;

public sealed partial class XenoAnnounceSystem : SharedXenoAnnounceSystem
{
    [Dependency] private IAdminLogManager _adminLogs = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Announce(EntityUid source, Filter filter, string message, string wrapped, SoundSpecifier? sound = null, PopupType? popup = null, bool needsQueen = false)
    {
        base.Announce(source, filter, message, wrapped, sound, popup, needsQueen);

        if (needsQueen)
        {
            if (Hive.GetHive(source) is { } sourceHive)
            {
                if (!Hive.HasHiveQueen(sourceHive))
                    return;
            }
            else
            {
                return;
            }
        }

        filter.AddWhereAttachedEntity(HasComp<GhostComponent>);

        if (source.IsValid())
            _adminLogs.Add(LogType.Chat, $"{ToPrettyString(source):source} xeno announced message: {message}"); // WOLFGATE: no RMC log types here

        _chat.ChatMessageToManyFiltered(filter, ChatChannel.Radio, message, wrapped, source, false, true, null);
        _audio.PlayGlobal(sound, filter, true);

        if (popup == null)
            return;

        foreach (var session in filter.Recipients)
        {
            if (session.AttachedEntity is { } recipient)
                _popup.PopupEntity(message, recipient, recipient, popup.Value);
        }
    }
}

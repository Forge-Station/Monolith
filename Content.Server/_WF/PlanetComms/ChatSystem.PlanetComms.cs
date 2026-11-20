using Content.Server._WF.PlanetComms;
using Content.Shared.Chat;
using Content.Shared.Database;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    [Dependency] private WFPlanetCommsSystem _wfPlanetComms = default!;

    /// <summary>
    /// Sends a global announcement only to the sessions it reaches, when a dark world would miss it; false, sending
    /// nothing, when everyone hears it and the upstream path should run.
    /// </summary>
    private bool WFDispatchGlobalReachable(string message, string sender, string wrappedMessage, bool playSound,
        SoundSpecifier? announcementSound, Color? colorOverride)
    {
        if (!_wfPlanetComms.TryNarrow(Filter.Broadcast(), null, out var reach))
            return false;

        _chatManager.ChatMessageToManyFiltered(reach, ChatChannel.Radio, message, wrappedMessage, default, false, true, colorOverride);
        if (playSound)
        {
            _audio.PlayGlobal(_audio.ResolveSound(announcementSound ?? new SoundPathSpecifier(DefaultAnnouncementSound)),
                reach, true, AudioParams.Default.WithVolume(-2f));
        }

        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Global station announcement from {sender}: {message}");
        return true;
    }
}

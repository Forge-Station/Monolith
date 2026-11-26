using Content.Server._WF.RoundSetup;
using Content.Shared.Radio;
using Robust.Shared.Utility;

namespace Content.Server.Radio.EntitySystems;

public sealed partial class RadioSystem
{
    /// <summary>Preserves normal channel labels while naming each isolated crew channel.</summary>
    private string WFCrewChannelName(EntityUid source, RadioChannelPrototype channel) => channel.ID == WFRoundSetupSystem.CrewChannel
        ? FormattedMessage.EscapeText(EntityManager.System<WFRoundSetupSystem>().CrewRadioName(source))
        : channel.LocalizedName;
}

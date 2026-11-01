using Content.Server._WF.Companies;
using Content.Shared.Radio;
using Robust.Shared.Utility;

namespace Content.Server.Radio.EntitySystems;

public sealed partial class RadioSystem
{
    /// <summary>Names each company's isolated channel after the speaker's company.</summary>
    private string WFCompanyChannelName(EntityUid source, RadioChannelPrototype channel) => channel.ID == WFCompanySystem.CompanyChannel
        ? FormattedMessage.EscapeText(EntityManager.System<WFCompanySystem>().CompanyRadioName(source))
        : channel.LocalizedName;
}

using Robust.Client.Graphics;

namespace Content.Client._WF.Companies;

/// <summary>Swaps a company rank's custom job icon into screens that draw job icons: the crew manifest, monitor and records.</summary>
public static class WFCompanyRankIcons
{
    /// <summary>The rank's custom icon, or null while it hasn't arrived or the rank has none; 0 is no rank.</summary>
    public static Texture? Get(int rankId) =>
        rankId == 0 ? null : IoCManager.Resolve<IEntityManager>().System<WFCompanyClientSystem>().GetRankIconLatest(rankId);

    /// <summary>The rank's custom icon if it has one, otherwise the job's own.</summary>
    public static Texture Or(int rankId, Texture fallback) => Get(rankId) ?? fallback;
}

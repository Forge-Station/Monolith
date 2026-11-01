using Content.Client.Overlays;
using Content.Shared._WF.Companies;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.PDA;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Utility;

namespace Content.Client._WF.Companies;

/// <summary>Swaps the HUD job icon for the company rank icon of the ID a body carries.</summary>
public sealed class WFCompanyRankHudSystem : EntitySystem
{
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private WFCompanyClientSystem _companies = default!;

    private readonly Dictionary<(ResPath, JobIconPrototype), StatusIconData> _icons = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCompanyRankHudComponent, GetStatusIconsEvent>(OnGetStatusIcons, after: new[] { typeof(ShowJobIconsSystem) });
    }

    private void OnGetStatusIcons(Entity<WFCompanyRankHudComponent> body, ref GetStatusIconsEvent args)
    {
        var index = args.StatusIcons.FindIndex(icon => icon is JobIconPrototype);
        if (index < 0 || RankIcon(body) is not { } rank || _companies.GetRankIconPath(rank) is not { } path)
            return;
        var job = (JobIconPrototype) args.StatusIcons[index];
        if (!_icons.TryGetValue((path, job), out var icon))
        {
            // Same placement and layer as the job icon it replaces, only the picture changes.
            icon = new StatusIconData
            {
                Icon = new SpriteSpecifier.Texture(path), Priority = job.Priority, VisibleToGhosts = job.VisibleToGhosts,
                HideInContainer = job.HideInContainer, HideOnStealth = job.HideOnStealth, ShowTo = job.ShowTo,
                LocationPreference = job.LocationPreference, Layer = job.Layer, Offset = job.Offset, IsShaded = job.IsShaded,
            };
            _icons[(path, job)] = icon;
        }
        args.StatusIcons[index] = icon;
    }

    /// <summary>The rank of the company ID the body wears, directly or in a PDA.</summary>
    private int? RankIcon(EntityUid body)
    {
        if (!_accessReader.FindAccessItemsInventory(body, out var items))
            return null;
        foreach (var item in items)
        {
            var card = HasComp<IdCardComponent>(item) ? item
                : TryComp<PdaComponent>(item, out var pda) && pda.ContainedId is { } contained ? contained : (EntityUid?) null;
            if (card == null)
                continue;
            return TryComp<WFCompanyRankIconComponent>(card, out var rank) ? rank.RankId : null;
        }
        return null;
    }
}

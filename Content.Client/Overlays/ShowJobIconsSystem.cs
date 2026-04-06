using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Overlays;
using Content.Shared.PDA;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;
using Content.Shared._Mono.Company;

namespace Content.Client.Overlays;

public sealed class ShowJobIconsSystem : EquipmentHudSystem<ShowJobIconsComponent>
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;

    [ValidatePrototypeId<JobIconPrototype>]
    private const string JobIconForNoId = "JobIconNoId";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StatusIconComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);
    }

    private void OnGetStatusIconsEvent(EntityUid uid, StatusIconComponent _, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        var iconId = JobIconForNoId;
        string? companyName = null;

        if (_accessReader.FindAccessItemsInventory(uid, out var items))
        {
            foreach (var item in items)
            {
                // ID Card
                if (TryComp<IdCardComponent>(item, out var id))
                {
                    iconId = id.JobIcon;
                    companyName = id.CompanyName; // Forge-change
                    break;
                }

                // PDA
                if (TryComp<PdaComponent>(item, out var pda)
                    && pda.ContainedId != null
                    && TryComp(pda.ContainedId, out id))
                {
                    iconId = id.JobIcon;
                    companyName = id.CompanyName; // Forge-change
                    break;
                }
            }
        }

        // Forge-change-start
        if (_prototype.TryIndex<JobIconPrototype>(iconId, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
        else
            Log.Error($"Invalid job icon prototype: {iconId}");

        TryAddCompanyIcon(companyName, ref ev);
    }

    private void TryAddCompanyIcon(string? companyName, ref GetStatusIconsEvent ev)
    {
        if (string.IsNullOrEmpty(companyName))
            return;

        if (_prototype.TryIndex<CompanyPrototype>(companyName, out var company) &&
            company.Icon != null &&
            _prototype.TryIndex<CompanyIconPrototype>(company.Icon.Value, out var companyIcon))
        {
            ev.StatusIcons.Add(companyIcon);
        }
    // Forge-change-end
    }
}

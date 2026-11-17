using System.Linq;
using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

/// <summary>
/// Syncs company Low/Medium/High access tags onto ID cards based on membership roles.
/// </summary>
public sealed partial class CompanySystem
{
    [Dependency] private SharedAccessSystem _access = default!;

    public void ApplyCompanyAccessTags(EntityUid mob, ProtoId<CompanyPrototype> company, ICommonSession? session)
    {
        if (company == "None" || session == null)
            return;

        if (!_inventorySystem.TryGetSlotEntity(mob, "id", out var idUid))
            return;

        var target = idUid.Value;
        if (TryComp<PdaComponent>(target, out var pda) && pda.ContainedId is { } containedId)
            target = containedId;

        if (!TryComp<AccessComponent>(target, out var accessComp))
            return;

        var tier = _manager.GetAccessTier(session.UserId, company);
        var companyTags = new HashSet<ProtoId<AccessLevelPrototype>>();
        foreach (var t in new[] { CompanyAccessTier.Low, CompanyAccessTier.Medium, CompanyAccessTier.High })
        {
            var id = CompanyAccessHelper.GetAccessLevelId(company, t);
            companyTags.Add(id);
        }

        var current = accessComp.Tags.ToHashSet();
        current.ExceptWith(companyTags);

        foreach (var tag in CompanyAccessHelper.GetTagsForTier(company, tier))
            current.Add(tag);

        _access.TrySetTags(target, current, accessComp);
    }
}

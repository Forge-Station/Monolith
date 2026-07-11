// Author: @lenta313. Все права не защищены / No rights reserved.
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Roles;
using Content.Shared.StatusIcon;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Roles;

/// <summary>
/// Company JobSpecial: swaps the employee's PDA for a branded one, carrying over the ID card's
/// name, job title, job icon and access tags. Configurable via DataFields, so any company can reuse
/// it through its <c>special:</c> list instead of hardcoding the swap in the company system.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class SwapPdaSpecial : JobSpecial
{
    /// <summary>Branded PDA to give. Its contained card receives the carried-over identity.</summary>
    [DataField(required: true)]
    public EntProtoId Pda;

    /// <summary>Company name written onto the new card. Empty = leave as-is.</summary>
    [DataField]
    public string Company = string.Empty;

    /// <summary>Extra access tag added to the new card (e.g. the company's area access).</summary>
    [DataField]
    public ProtoId<AccessLevelPrototype>? Access;

    public override void AfterEquip(EntityUid mob)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        var sysMan = IoCManager.Resolve<IEntitySystemManager>();
        var protoMan = IoCManager.Resolve<IPrototypeManager>();
        var inv = sysMan.GetEntitySystem<InventorySystem>();
        var idSys = sysMan.GetEntitySystem<SharedIdCardSystem>();

        if (!inv.TryGetSlotEntity(mob, "id", out var idUid))
            return;

        // Already a branded PDA (e.g. supplied by a loadout) — nothing to do.
        if (entMan.GetComponent<MetaDataComponent>(idUid.Value).EntityPrototype?.ID == Pda.Id)
            return;

        // A held PDA contains the card; a bare card is itself.
        EntityUid? oldPda = null;
        var cardId = idUid.Value;
        if (entMan.TryGetComponent<PdaComponent>(idUid, out var pda) && pda.ContainedId != null)
        {
            oldPda = idUid.Value;
            cardId = pda.ContainedId.Value;
        }

        // Carry over the old card's identity.
        string? fullName = null;
        string? jobTitle = null;
        ProtoId<JobIconPrototype> jobIcon = "JobIconUnknown";
        if (entMan.TryGetComponent<IdCardComponent>(cardId, out var oldCard))
        {
            fullName = oldCard.FullName;
            jobTitle = oldCard.LocalizedJobTitle;
            jobIcon = oldCard.JobIcon;
        }

        var tags = new HashSet<ProtoId<AccessLevelPrototype>>();
        if (entMan.TryGetComponent<AccessComponent>(cardId, out var oldAccess))
            tags = new HashSet<ProtoId<AccessLevelPrototype>>(oldAccess.Tags);

        // In with the branded one first, so the mob is never left without an ID card if something
        // goes wrong (e.g. a tickrate hiccup) between spawning and equipping.
        var coords = entMan.GetComponent<TransformComponent>(mob).Coordinates;
        var newPda = entMan.SpawnEntity(Pda, coords);

        if (entMan.TryGetComponent<PdaComponent>(newPda, out var newPdaComp) && newPdaComp.ContainedId is { } newCard)
        {
            if (entMan.TryGetComponent<IdCardComponent>(newCard, out var card))
            {
                idSys.TryChangeFullName(newCard, fullName, card);
                idSys.TryChangeJobTitle(newCard, jobTitle, card);
                if (!string.IsNullOrEmpty(Company))
                    idSys.TryChangeCompanyName(newCard, Company, card);
                if (protoMan.TryIndex(jobIcon, out var jobIconProto))
                    idSys.TryChangeJobIcon(newCard, jobIconProto, card);
            }

            if (entMan.TryGetComponent<AccessComponent>(newCard, out var newAccess))
            {
                foreach (var tag in tags)
                    newAccess.Tags.Add(tag);
                if (Access is { } extra)
                    newAccess.Tags.Add(extra);
                entMan.Dirty(newCard, newAccess);
            }
        }

        // Only now remove the old one, and equip the new one right away in its place.
        inv.TryUnequip(mob, "id", force: true);
        entMan.QueueDeleteEntity(cardId);
        if (oldPda != null)
            entMan.QueueDeleteEntity(oldPda.Value);

        inv.TryEquip(mob, newPda, "id", force: true);
    }
}
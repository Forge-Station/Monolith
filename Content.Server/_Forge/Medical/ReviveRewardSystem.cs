using Content.Server._NF.Bank;
using Content.Shared._Forge.ReviveReward;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Inventory;
using Content.Shared.Tag;
using Content.Server.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared._NF.Bank.BUI;
using Content.Shared.Implants.Components;
using Content.Shared.Implants;

namespace Content.Server._Forge.ReviveReward;

public sealed partial class ReviveRewardSystem : EntitySystem
{
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntityRevivedEvent>(OnEntityRevived);
    }

    private void OnEntityRevived(EntityRevivedEvent ev)
    {
        ReviveRewardComponent? tracker = null;

        if (TryComp<ImplantedComponent>(ev.Target, out var implanted))
        {
            foreach (var implant in implanted.ImplantContainer.ContainedEntities)
            {
                if (TryComp<ReviveRewardComponent>(implant, out var foundTracker))
                {
                    tracker = foundTracker;
                    break;
                }
            }
        }

        if (tracker == null || !tracker.Death)
            return;
        if (_gameTiming.CurTime < tracker.NextRewardAvailable)
            return;
        if (!_inventory.HasEquippedTag(ev.Reviver, tracker.RewardTag))
            return;

        var distance = tracker.DeathPos.Length();
        var totalReward = (int)(tracker.Reward + distance * tracker.PerTileBonus);

        if (totalReward <= 0)
            return;

        var doctorShare = (int)(totalReward * 0.8f);
        var budgetShare = totalReward - doctorShare;

        if (doctorShare > 0 && _bank.TryBankDeposit(ev.Reviver, doctorShare))
        {
            _popup.PopupEntity(Loc.GetString("medical-revive-reward-popup", ("cash", doctorShare)), ev.Reviver, ev.Reviver);

            tracker.NextRewardAvailable = _gameTiming.CurTime + tracker.RewardCooldown;
            tracker.Death = false;
        }

        if (budgetShare > 0)
            _bank.TrySectorDeposit(SectorBankAccount.Medical, budgetShare, LedgerEntryType.MedicalBountyTax);
    }
}

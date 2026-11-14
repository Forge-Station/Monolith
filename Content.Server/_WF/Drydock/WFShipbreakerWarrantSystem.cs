using Content.Server._NF.BountyContracts;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared._NF.BountyContracts;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Mobs;
using Robust.Server.Player;
using Robust.Shared.Player;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Warrants on shipbreakers: posted on the bounty board's warrant list, raised by each further break, and paid to the
/// players who hurt the wanted in person once they die. Withdrawn unpaid if the wanted's body is gone first.
/// </summary>
public sealed partial class WFShipbreakerWarrantSystem : EntitySystem
{
    [Dependency] private BountyContractSystem _board = default!;
    [Dependency] private WFEncounterBountySystem _bounties = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    public override void Initialize()
    {
        base.Initialize();
        // Before the damage lands, while it still names who dealt it.
        SubscribeLocalEvent<WFShipbreakerWarrantComponent, BeforeDamageChangedEvent>(OnDamaged,
            before: [typeof(Content.Shared._Onyx.Wounds.WoundDamageRoutingSystem)]);
        SubscribeLocalEvent<WFShipbreakerWarrantComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WFShipbreakerWarrantComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>Puts a warrant on a shipbreaker, or adds the reward to the one they already have.</summary>
    public void Issue(EntityUid breaker, string ship, int reward, string description, string author, LocId? paidMessage = null)
    {
        if (reward <= 0)
            return;

        if (TryComp<WFShipbreakerWarrantComponent>(breaker, out var warrant))
        {
            warrant.Reward = (int) Math.Min((long) warrant.Reward + reward, int.MaxValue);
            if (warrant.Contract is { } id)
                _board.TrySetContractReward(id, warrant.Reward);
            else
                Post(breaker, warrant, description, author);
            Log.Info($"Shipbreaking warrant on {ToPrettyString(breaker)} raised to {warrant.Reward} for {ship}.");
            return;
        }

        warrant = AddComp<WFShipbreakerWarrantComponent>(breaker);
        warrant.Reward = reward;
        if (paidMessage is { } paid)
            warrant.PaidMessage = paid;
        Post(breaker, warrant, description, author);
    }

    /// <summary>Posts a warrant on the board, or leaves it unposted to try again on the next break if the board is down.</summary>
    private void Post(EntityUid breaker, WFShipbreakerWarrantComponent warrant, string description, string author)
    {
        warrant.Contract = _board.TryPostSystemContract(BountyContractSystem.WarrantCollection, BountyContractCategory.Criminal,
            Name(breaker), warrant.Reward, description, author: author)?.ContractId;
        if (warrant.Contract == null)
            Log.Warning($"Shipbreaking warrant on {ToPrettyString(breaker)} stands unposted: the bounty board is not up.");
    }

    /// <summary>A player who hurts the wanted in person has a hand in taking them down.</summary>
    private void OnDamaged(Entity<WFShipbreakerWarrantComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive() || args.Origin is not { } source)
            return;

        // A beam names the gun as its origin; the attacker is whoever holds it.
        var origin = _crew.Wielder(source);
        if (origin != ent.Owner && TryComp<ActorComponent>(origin, out var actor))
            ent.Comp.Fighters.Add(actor.PlayerSession.UserId);
    }

    /// <summary>Pays the warrant when the wanted dies, if anyone who hurt them can take it; otherwise it stands.</summary>
    private void OnMobStateChanged(Entity<WFShipbreakerWarrantComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        var hunters = new List<ICommonSession>();
        foreach (var user in ent.Comp.Fighters)
        {
            if (_players.TryGetSessionById(user, out var session) && session.AttachedEntity != ent.Owner && _bounties.Payable(session))
                hunters.Add(session);
        }

        if (hunters.Count == 0)
            return;

        var name = Name(ent);
        var share = ent.Comp.Reward / hunters.Count;
        var carry = ent.Comp.Reward - share * hunters.Count;
        foreach (var session in hunters)
        {
            var amount = share + carry;
            if (amount <= 0 || !_bounties.Deposit(session, amount))
            {
                carry = amount;
                continue;
            }

            carry = 0;
            _chat.DispatchServerMessage(session, Loc.GetString(ent.Comp.PaidMessage, ("name", name), ("amount", amount)));
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{session.Name} was paid {amount} spesos for the shipbreaking warrant on {ToPrettyString(ent):entity}");
        }

        if (carry > 0)
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{carry} spesos of the shipbreaking warrant on {ToPrettyString(ent):entity} could not be paid to anyone");

        RemoveContract(ent.Comp);
        RemCompDeferred<WFShipbreakerWarrantComponent>(ent);
    }

    private void OnShutdown(Entity<WFShipbreakerWarrantComponent> ent, ref ComponentShutdown args)
    {
        RemoveContract(ent.Comp);
    }

    private void RemoveContract(WFShipbreakerWarrantComponent warrant)
    {
        if (warrant.Contract is { } id)
            _board.TryRemoveSystemContract(id);

        warrant.Contract = null;
    }
}

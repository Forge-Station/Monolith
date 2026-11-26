using Content.Server._WF.Traders;
using Content.Server.Administration.Logs;
using Content.Shared._NF.Bank;
using Content.Shared._WF.Minigames;
using Content.Shared._WF.Traders;
using Content.Shared.Database;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Minigames;

/// <summary>
/// Runs games a trader plays for a stake: offers them through the trader's dialogue, takes the stake, owns the game
/// and checks every move, takes the NPC's turns, and pays a win in cash or items under a cooldown and a daily cap.
/// </summary>
public sealed partial class MinigameSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private TraderSystem _trader = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFMinigameHostComponent, TraderActionEvent>(OnAction);
        SubscribeLocalEvent<WFMinigameHostComponent, TraderConfirmedEvent>(OnConfirmed);
        SubscribeLocalEvent<WFMinigameHostComponent, TraderConversationEndedEvent>(OnConversationEnded);
        SubscribeLocalEvent<WFMinigameHostComponent, BoundUIClosedEvent>(OnUiClosed);
        Subs.BuiEvents<WFMinigameHostComponent>(WFMinigameUiKey.Key, subs =>
        {
            subs.Event<MinigameMoveMessage>(OnMoveMessage);
        });
    }

    #region Offer

    private void OnAction(Entity<WFMinigameHostComponent> ent, ref TraderActionEvent args)
    {
        ent.Comp.PendingOffer = null;
        if (args.Action != TraderAction.PlayMinigame || args.Handled || !TryComp<TraderComponent>(ent, out var trader))
            return;

        args.Handled = true;
        var traderEnt = (ent.Owner, trader);
        if (!int.TryParse(args.Argument, out var index) || index < 0 || index >= ent.Comp.Offers.Count)
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-cannot-help"));
            return;
        }

        if (!CanPlay(ent, args.Customer, index, out var refusal))
        {
            _trader.SayAndShow(traderEnt, refusal);
            return;
        }

        var offer = ent.Comp.Offers[index];
        ent.Comp.PendingOffer = index;
        _trader.AskConfirmation(traderEnt, Loc.GetString(offer.Game == MinigameKind.HigherLower ? "wf-minigame-offer-ladder" : "wf-minigame-offer",
            ("game", GameName(offer.Game)),
            ("difficulty", DifficultyName(offer.Difficulty)),
            ("stake", BankSystemExtensions.ToSpesoString(offer.Stake)),
            ("prize", PrizeText(offer, MinigameRules.Payout(offer.Game, offer.Difficulty)))));
    }

    private void OnConfirmed(Entity<WFMinigameHostComponent> ent, ref TraderConfirmedEvent args)
    {
        if (ent.Comp.PendingOffer is not { } index)
            return;

        ent.Comp.PendingOffer = null;
        if (!TryComp<TraderComponent>(ent, out var trader))
            return;

        if (!args.Accepted)
        {
            _trader.SayAndShow((ent.Owner, trader), Loc.GetString("wf-minigame-declined"));
            return;
        }

        TryStartGame(ent, args.Customer, index);
    }

    /// <summary>Whether the host will play this offer with the customer now, and why not.</summary>
    public bool CanPlay(Entity<WFMinigameHostComponent> ent, EntityUid customer, int index, out string refusal)
    {
        refusal = string.Empty;
        var now = _timing.CurTime;

        if (ent.Comp.Cooldowns.TryGetValue(PlayerKey(customer), out var until) && until > now)
        {
            refusal = Loc.GetString("wf-minigame-cooldown", ("minutes", (int) Math.Ceiling((until - now).TotalMinutes)));
            return false;
        }

        if (now >= ent.Comp.CapResetAt)
        {
            ent.Comp.PaidOut = 0;
            ent.Comp.CapResetAt = now + ent.Comp.CapPeriod;
        }

        var offer = ent.Comp.Offers[index];
        if (ent.Comp.PaidOut + MaxPrize(offer) > ent.Comp.DailyCap)
        {
            refusal = Loc.GetString("wf-minigame-house-broke");
            return false;
        }

        return true;
    }

    /// <summary>Takes the stake and deals the game. False, with nothing taken, if the host won't play or isn't paid.</summary>
    public bool TryStartGame(Entity<WFMinigameHostComponent> ent, EntityUid customer, int index)
    {
        if (!TryComp<TraderComponent>(ent, out var trader) || index < 0 || index >= ent.Comp.Offers.Count)
            return false;

        var traderEnt = (ent.Owner, trader);
        if (!CanPlay(ent, customer, index, out var refusal))
        {
            _trader.SayAndShow(traderEnt, refusal);
            return false;
        }

        var offer = ent.Comp.Offers[index];
        if (!_trader.TryTakePayment(traderEnt, customer, offer.Stake))
            return false;

        ent.Comp.Game = MinigameRules.Create(offer.Game, offer.Difficulty, new System.Random(_random.Next()));
        ent.Comp.Player = customer;
        ent.Comp.Offer = index;
        ent.Comp.Settled = false;
        ent.Comp.NpcMoveAt = null;

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(customer):actor} staked {offer.Stake} on {offer.Game} ({offer.Difficulty}) with {ToPrettyString(ent)}");
        _trader.SayAndShow(traderEnt, Loc.GetString("wf-minigame-begin"));
        _ui.TryOpenUi(ent.Owner, WFMinigameUiKey.Key, customer);
        _trader.HideDialogue(traderEnt, customer);
        if (offer.Game == MinigameKind.LiarsDice)
            _audio.PlayPvs(ent.Comp.DiceSound, ent);

        UpdateState(ent);
        return true;
    }

    #endregion

    #region Play

    private void OnMoveMessage(Entity<WFMinigameHostComponent> ent, ref MinigameMoveMessage args)
    {
        TryMove(ent, args.Actor, args.Move);
    }

    /// <summary>
    /// Applies a player's move. Refused when it isn't their game, the game is over, the NPC is still taking its turn,
    /// or the move breaks the rules.
    /// </summary>
    public bool TryMove(Entity<WFMinigameHostComponent> ent, EntityUid actor, MinigameMove move)
    {
        if (ent.Comp.Game is not { } game || ent.Comp.Player != actor
            || !TryComp<TraderComponent>(ent, out var trader) || trader.Customer != actor)
            return false;

        _trader.NoteInput((ent.Owner, trader));
        var roundBefore = game is LiarsDiceGame dice ? dice.RoundsWon : 0;
        if (!game.TryPlayerMove(move))
        {
            UpdateState(ent);
            return false;
        }

        _audio.PlayPvs(ent.Comp.MoveSound, ent);
        if (game is LiarsDiceGame rolled && rolled.RoundsWon != roundBefore && !game.Finished)
            _audio.PlayPvs(ent.Comp.DiceSound, ent);

        AfterMove(ent);
        return true;
    }

    private void AfterMove(Entity<WFMinigameHostComponent> ent)
    {
        if (ent.Comp.Game is not { } game)
            return;

        ent.Comp.NpcMoveAt = game.Status == MinigameStatus.NpcTurn ? _timing.CurTime + ent.Comp.NpcDelay : null;
        if (game.Finished)
            Settle(ent);

        UpdateState(ent);
    }

    /// <summary>Pays a win, or takes a loss, once.</summary>
    private void Settle(Entity<WFMinigameHostComponent> ent)
    {
        if (ent.Comp.Settled || ent.Comp.Game is not { } game || ent.Comp.Player is not { } player)
            return;

        ent.Comp.Settled = true;
        var offer = ent.Comp.Offers[ent.Comp.Offer];

        if (game.Status != MinigameStatus.Won)
        {
            _audio.PlayPvs(ent.Comp.LoseSound, ent);
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(player):actor} lost {offer.Stake} at {offer.Game} to {ToPrettyString(ent)}");
            return;
        }

        var prize = Prize(offer, game.PayoutMultiplier);
        ent.Comp.PaidOut += prize;
        ent.Comp.Cooldowns[PlayerKey(player)] = _timing.CurTime + ent.Comp.Cooldown;
        _audio.PlayPvs(ent.Comp.WinSound, ent);

        if (TryComp<TraderComponent>(ent, out var trader))
        {
            var traderEnt = (ent.Owner, trader);
            if (offer.Rewards.Count > 0)
            {
                var coords = _trader.GetOutputCoordinates(traderEnt);
                foreach (var reward in offer.Rewards)
                    Spawn(reward, coords);
            }
            else
            {
                _trader.GiveChange(traderEnt, prize);
            }
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):actor} won {prize} ({(offer.Rewards.Count > 0 ? "items" : "cash")}) at {offer.Game} from {ToPrettyString(ent)}");
    }

    /// <summary>A game walked away from before it ended is lost.</summary>
    private void Forfeit(Entity<WFMinigameHostComponent> ent)
    {
        if (ent.Comp.Game is { Finished: false } && !ent.Comp.Settled && ent.Comp.Player is { } player)
        {
            var offer = ent.Comp.Offers[ent.Comp.Offer];
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(player):actor} walked away from {offer.Game} with {ToPrettyString(ent)} and lost {offer.Stake}");
        }

        ent.Comp.Game = null;
        ent.Comp.Player = null;
        ent.Comp.NpcMoveAt = null;
        ent.Comp.Settled = false;
    }

    private void OnUiClosed(Entity<WFMinigameHostComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is not WFMinigameUiKey.Key || args.Actor != ent.Comp.Player)
            return;

        Forfeit(ent);

        // Back to the conversation the game took over.
        if (TryComp<TraderComponent>(ent, out var trader) && trader.Customer == args.Actor)
            _trader.TryStartConversation((ent.Owner, trader), args.Actor);
    }

    private void OnConversationEnded(Entity<WFMinigameHostComponent> ent, ref TraderConversationEndedEvent args)
    {
        ent.Comp.PendingOffer = null;
        if (ent.Comp.Player != args.Customer)
            return;

        Forfeit(ent);
        _ui.CloseUi(ent.Owner, WFMinigameUiKey.Key, args.Customer);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFMinigameHostComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NpcMoveAt is not { } at || now < at || comp.Game is not { } game)
                continue;

            comp.NpcMoveAt = null;
            game.NpcMove();
            if (game is LiarsDiceGame && game.Status == MinigameStatus.Playing)
                _audio.PlayPvs(comp.MoveSound, uid);

            AfterMove((uid, comp));
        }
    }

    #endregion

    #region State

    private void UpdateState(Entity<WFMinigameHostComponent> ent)
    {
        if (ent.Comp.Game is not { } game)
            return;

        var offer = ent.Comp.Offers[ent.Comp.Offer];
        var view = game.GetView();
        if (view is RiddleView riddle)
        {
            // Localized here so the answers' ids, which name the right one, never reach the client.
            riddle.Question = Loc.GetString(riddle.Question);
            for (var i = 0; i < riddle.Answers.Count; i++)
                riddle.Answers[i] = Loc.GetString(riddle.Answers[i]);
        }

        var multiplier = game is HigherLowerGame && game.Status != MinigameStatus.Won && game.PayoutMultiplier <= 0
            ? MinigameRules.HigherLowerLadder(offer.Difficulty)[0]
            : game.PayoutMultiplier;

        var state = new MinigameState(game.Kind, game.Difficulty, game.Status, offer.Stake,
            PrizeText(offer, multiplier), Loc.GetString(game.Line, game.LineArgs), view);
        _ui.SetUiState(ent.Owner, WFMinigameUiKey.Key, state);
    }

    #endregion

    #region Helpers

    /// <summary>Cash a win pays at this multiple.</summary>
    public static int Prize(MinigameOffer offer, float multiplier) => (int) Math.Round(offer.Stake * multiplier);

    /// <summary>Largest prize an offer can pay, for the daily cap.</summary>
    public static int MaxPrize(MinigameOffer offer) => Prize(offer, MinigameRules.Payout(offer.Game, offer.Difficulty));

    private string PrizeText(MinigameOffer offer, float multiplier)
    {
        if (offer.Rewards.Count == 0)
            return BankSystemExtensions.ToSpesoString(Prize(offer, multiplier));

        var counts = new List<(EntProtoId Item, int Count)>();
        foreach (var reward in offer.Rewards)
        {
            var at = counts.FindIndex(entry => entry.Item == reward);
            if (at >= 0)
                counts[at] = (reward, counts[at].Count + 1);
            else
                counts.Add((reward, 1));
        }

        var names = new List<string>();
        foreach (var (item, count) in counts)
            names.Add(Loc.GetString("wf-minigame-reward-item", ("item", RewardName(item)), ("count", count)));

        return string.Join(", ", names);
    }

    private string RewardName(EntProtoId proto)
    {
        return _proto.TryIndex(proto, out var entity) ? entity.Name : proto.Id;
    }

    public string GameName(MinigameKind kind) => Loc.GetString($"wf-minigame-name-{kind.ToString().ToLowerInvariant()}");

    public string DifficultyName(MinigameDifficulty difficulty) =>
        Loc.GetString($"wf-minigame-difficulty-{difficulty.ToString().ToLowerInvariant()}");

    /// <summary>Cooldowns follow the player's account, so a new body doesn't reset them.</summary>
    private string PlayerKey(EntityUid player)
    {
        return TryComp<ActorComponent>(player, out var actor) ? actor.PlayerSession.UserId.ToString() : player.ToString();
    }

    #endregion
}

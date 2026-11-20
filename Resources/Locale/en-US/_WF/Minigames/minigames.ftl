## Games NPCs play for a stake (Content.Server/_WF/Minigames).

wf-minigame-name-orepiles = Ore Piles
wf-minigame-name-liarsdice = Liar's Dice
wf-minigame-name-lockbox = Lockbox
wf-minigame-name-higherlower = Higher or Lower
wf-minigame-name-memory = Memory
wf-minigame-name-riddles = Riddles

wf-minigame-difficulty-easy = easy table
wf-minigame-difficulty-normal = normal table
wf-minigame-difficulty-hard = high table

## Offer

wf-minigame-offer = {$game}, {$difficulty}. Stake {$stake}. A win pays {$prize}. Play?
wf-minigame-offer-ladder = {$game}, {$difficulty}. Stake {$stake}. A full run pays {$prize}; cash out sooner for less. Play?
wf-minigame-declined = Suit yourself.
wf-minigame-cooldown = You've had your win off me. Come back in {$minutes ->
    [one] a minute
   *[other] {$minutes} minutes
}.
wf-minigame-house-broke = I've paid out all I can for today. Try me tomorrow.
wf-minigame-begin = Let's play.
wf-minigame-reward-item = {$count ->
    [one] {$item}
   *[other] {$count}x {$item}
}

## Window

wf-minigame-window-title = Game
wf-minigame-window-header = {$game}, {$difficulty}
wf-minigame-window-stakes = Stake {$stake}. Prize {$prize}.
wf-minigame-window-won = You win.
wf-minigame-window-lost = You lose.
wf-minigame-window-thinking = They're thinking...
wf-minigame-line-start = Your move.

## Ore Piles

wf-minigame-ore-rules = Take 1 to 3 ore in turn. Whoever takes the last one loses.
wf-minigame-ore-take = Take {$count}
wf-minigame-ore-pile = {$pile} ore left
wf-minigame-ore-start = {$pile} ore on the table. You go first.
wf-minigame-ore-player-took = You took {$count}. {$pile} left.
wf-minigame-ore-npc-took = They took {$count}. {$pile} left.
wf-minigame-ore-player-last = You took the last ore.
wf-minigame-ore-npc-last = They had to take the last ore.

## Liar's Dice

wf-minigame-dice-rules = Bid how many dice on the table show a face, or call the last bid a lie.
wf-minigame-dice-bid = Bid
wf-minigame-dice-call = Liar!
wf-minigame-dice-pick = {$count} x {$face}
wf-minigame-dice-round = Round {$round} of {$rounds}
wf-minigame-dice-yours = Your dice: {$dice}
wf-minigame-dice-theirs = Their dice: {$count} under the cup
wf-minigame-dice-theirs-shown = Their dice last round: {$dice}
wf-minigame-dice-no-bid = No bid yet. You open.
wf-minigame-dice-your-bid = Your bid: {$count} x {$face}
wf-minigame-dice-their-bid = Their bid: {$count} x {$face}
wf-minigame-dice-start = Five dice each, no wilds. Win {$rounds} rounds in a row.
wf-minigame-dice-player-bid = You bid {$count} x {$face}.
wf-minigame-dice-npc-bid = They bid {$count} x {$face}.
wf-minigame-dice-player-called-wrong = You called. There were {$count} x {$face}. The bid stood.
wf-minigame-dice-npc-called-right = They called. There were only {$count} x {$face}.
wf-minigame-dice-player-called-right = You called. There were only {$count} x {$face}.
wf-minigame-dice-npc-called-wrong = They called. There were {$count} x {$face}. Your bid stood.
wf-minigame-dice-round-won = Round to you: there were {$count} x {$face}. {$won} of {$rounds}. New dice.

## Lockbox

wf-minigame-lock-rules = Guess the code. Hits: right digit, right place. Near: right digit, wrong place.
wf-minigame-lock-placeholder = Code
wf-minigame-lock-try = Try
wf-minigame-lock-tries = {$left ->
    [one] 1 try left
   *[other] {$left} tries left
}
wf-minigame-lock-code = The code was {$code}.
wf-minigame-lock-guess = {$code}: {$hits} hit, {$near} near
wf-minigame-lock-start = A {$digits}-digit code. You get {$tries} tries.
wf-minigame-lock-feedback = {$hits} hit, {$near} near. {$left} left.
wf-minigame-lock-open = It clicks open on try {$tries}.
wf-minigame-lock-jammed = It jams. The code was {$code}.

## Higher or Lower

wf-minigame-cards-rules = Call the next card higher or lower. A tie loses. Cash out after any right call.
wf-minigame-cards-higher = Higher
wf-minigame-cards-lower = Lower
wf-minigame-cards-cash-out = Cash out
wf-minigame-cards-card = {$rank} of {$suit}
wf-minigame-cards-run = Run {$streak} of {$max}. Next right call pays {$next}x.
wf-minigame-cards-run-top = Run {$streak}. Top of the ladder.
wf-minigame-cards-start = Climb the ladder to {$top}x. Fresh deck every card.
wf-minigame-cards-right = Right. Run of {$streak}, worth {$multiple}x.
wf-minigame-cards-wrong = Wrong call.
wf-minigame-cards-tie = Same rank. A tie loses.
wf-minigame-cards-cashed = You cash out at {$multiple}x.
wf-minigame-cards-top = Top of the ladder: {$multiple}x.
wf-minigame-cards-rank-1 = Two
wf-minigame-cards-rank-2 = Three
wf-minigame-cards-rank-3 = Four
wf-minigame-cards-rank-4 = Five
wf-minigame-cards-rank-5 = Six
wf-minigame-cards-rank-6 = Seven
wf-minigame-cards-rank-7 = Eight
wf-minigame-cards-rank-8 = Nine
wf-minigame-cards-rank-9 = Ten
wf-minigame-cards-rank-10 = Jack
wf-minigame-cards-rank-11 = Queen
wf-minigame-cards-rank-12 = King
wf-minigame-cards-rank-13 = Ace
wf-minigame-cards-suit-0 = Hearts
wf-minigame-cards-suit-1 = Diamonds
wf-minigame-cards-suit-2 = Clubs
wf-minigame-cards-suit-3 = Spades

## Memory

wf-minigame-memory-rules = Watch the pads light up, then press them back in order.
wf-minigame-memory-progress = {$entered} of {$length} pressed. Reach {$target} to win.
wf-minigame-memory-start = Reach a sequence of {$length}.
wf-minigame-memory-next = Right. Now {$length} of {$target}.
wf-minigame-memory-wrong = Wrong pad. You got to {$reached}.
wf-minigame-memory-won = Every one right.

## Riddles

wf-minigame-riddle-number = Riddle {$number} of {$total}
wf-minigame-riddle-start = Answer {$count} riddles right in a row.
wf-minigame-riddle-right = Right. {$left} to go.
wf-minigame-riddle-wrong = Wrong.
wf-minigame-riddle-won = Every one right.

## Hosts

wf-minigame-prompt-rules = How does this work?

wf-minigame-dealer-greeting = Take a seat. Cards, dice or the lockbox?
wf-minigame-dealer-farewell = Come back when your luck's in.
wf-minigame-dealer-prompt-cards = Deal me Higher or Lower.
wf-minigame-dealer-response-cards = Fresh deck, every card.
wf-minigame-dealer-prompt-dice = Liar's Dice.
wf-minigame-dealer-response-dice = Shake the cup.
wf-minigame-dealer-prompt-lock = I'll crack your lockbox.
wf-minigame-dealer-response-lock = Four digits. Not many tries.
wf-minigame-dealer-response-rules = Put your stake on the counter or show me your ID. Win and I pay several times over. Win once and you wait a while before I play you again.

wf-minigame-sage-greeting = Ah, a visitor. Do you like puzzles?
wf-minigame-sage-farewell = Think on it.
wf-minigame-sage-prompt-riddles = Ask me your riddles.
wf-minigame-sage-response-riddles = Three in a row, and no guessing.
wf-minigame-sage-prompt-memory = The memory game.
wf-minigame-sage-response-memory = Watch closely.
wf-minigame-sage-prompt-ore = Ore piles, high stakes.
wf-minigame-sage-response-ore = Nobody beats me at this. You can try.
wf-minigame-sage-response-rules = A stake on the counter, or your ID. Win and I pay well. Lose and I keep it.

wf-minigame-gambler-greeting = Hey, friend. Got a few credits? Let's make it interesting.
wf-minigame-gambler-farewell = See you down the road.
wf-minigame-gambler-prompt-ore = Ore piles.
wf-minigame-gambler-response-ore = Last one loses. Easy, right?
wf-minigame-gambler-prompt-dice = Liar's Dice.
wf-minigame-gambler-response-dice = Don't lie to me now.
wf-minigame-gambler-prompt-cards = Higher or Lower.
wf-minigame-gambler-response-cards = Here's your first card.
wf-minigame-gambler-response-rules = Cash on the table or your ID. I pay three to one. Don't win too often.

wf-minigame-miner-greeting = Hm. Don't get many visitors this deep. You play?
wf-minigame-miner-farewell = Mind the ceiling.
wf-minigame-miner-prompt-ore-easy = A friendly game of ore piles.
wf-minigame-miner-response-ore-easy = I'll go easy. Gold and silver if you win.
wf-minigame-miner-prompt-dice = Liar's Dice for diamonds.
wf-minigame-miner-response-dice = Diamonds and uranium in the pot.
wf-minigame-miner-prompt-ore-hard = Ore piles. Your best game.
wf-minigame-miner-response-ore-hard = Beat me and the diamond drill is yours. You won't.
wf-minigame-miner-response-rules = Stake in cash, on the crate or from your account. I don't pay in money. I pay in ore, and the best drill you've ever held.

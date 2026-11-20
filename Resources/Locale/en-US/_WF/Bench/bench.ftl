## The Bench app and terminal

wf-bench-program-name = Bench
wf-bench-terminal-title = Bench Terminal
wf-bench-stamp = Bench of Caelestinus

wf-bench-tab-ticket = Ticket
wf-bench-tab-ledger = Ledger
wf-bench-tab-laws = Laws

wf-bench-field-offender = Offender
wf-bench-field-ship = Vessel
wf-bench-field-ship-placeholder = Optional
wf-bench-field-notes = Notes
wf-bench-field-notes-placeholder = Optional
wf-bench-no-one-near = No one close enough
wf-bench-heading-ordinances = Posted ordinances
wf-bench-heading-custom = Other charges
wf-bench-none = None.
wf-bench-custom-title = Charge
wf-bench-custom-fine = Fine
wf-bench-custom-add = Add
wf-bench-remove = Remove
wf-bench-law-range = { $level }, { $min }-{ $max }
wf-bench-charge-line = { $title } ({ $level }): { $fine }
wf-bench-summary-empty = Tick the charges.
wf-bench-summary = { $count ->
    [one] 1 charge
   *[other] { $count } charges
}: { $total } thlr, { $level }
wf-bench-summary-capped = {" "}(capped)
wf-bench-issue = Issue ticket
wf-bench-issued = Ticket #{ $number } issued.

wf-bench-ledger-empty = No tickets this round.
wf-bench-ledger-pick = Pick a ticket.
wf-bench-ledger-row = #{ $number } { $name }: { $total } thlr, { $status }{ $owed ->
    [0] {""}
   *[other] , { $owed } owed
}
wf-bench-ledger-head = [bold]Ticket #{ $number }[/bold], { $name }, by { $judge }
wf-bench-ledger-ship = Vessel: { $ship }
wf-bench-ledger-money = Fine { $total }, taken { $paid }, owed { $owed }. { $status }.
wf-bench-ledger-deadline = Wanted in { $minutes } min if still owed.
wf-bench-ledger-notes = Notes: { $notes }
wf-bench-pardon = Pardon
wf-bench-pardon-confirm = Confirm pardon
wf-bench-pardon-done = Ticket #{ $number } pardoned.

wf-bench-status-paid = Paid
wf-bench-status-owing = Owing
wf-bench-status-overdue = Overdue
wf-bench-status-outlawed = Outlawed
wf-bench-status-pardoned = Pardoned

wf-bench-laws-preamble = [bold]The Common Writ of the Bench[/bold]
wf-bench-laws-preamble-text = [italic]By the hand of the Bench of Caelestinus. Binding on every soul within the anchors and beyond them. Ignorance of the Writ is no defence.[/italic]
wf-bench-law-entry = [bold]{ $name }[/bold] ({ $penalty }): { $text }
wf-bench-ordinance-entry = [bold]{ $name }[/bold] ({ $penalty }): { $text } [color=gray]Posted by { $by }.[/color]
wf-bench-penalty-fine = { $level }, { $fine }
wf-bench-post-heading = Post an ordinance
wf-bench-post-title = Title
wf-bench-post-text = What it forbids
wf-bench-post = Post

wf-bench-error-not-judge = Only the Bench can do that.
wf-bench-error-range = They need to be right in front of you.
wf-bench-error-self = You can't ticket yourself.
wf-bench-error-no-account = They have no account to fine.
wf-bench-error-no-charges = Tick at least one charge.

## The ticket and what the offender is told

wf-bench-ticket-head = [head=2]Bench of Caelestinus[/head]
wf-bench-ticket-number = [bold]Ticket #{ $number }[/bold]
wf-bench-ticket-offender = Offender: { $name }
wf-bench-ticket-ship = Vessel: { $ship }
wf-bench-ticket-judge = Judge: { $name }, at { $time }
wf-bench-ticket-charge = - { $title } ({ $level }): { $fine } thlr
wf-bench-ticket-total = [bold]Fine: { $total } thlr. Penalty: { $level }.[/bold]
wf-bench-ticket-capped = The charges add up to { $uncapped } thlr. The Bench's limit applies.
wf-bench-ticket-paid = Taken from your account: { $paid } thlr. Owed: { $owed } thlr.
wf-bench-ticket-debt = What you owe is taken from your earnings. Still owed after { $minutes } minutes, or over the Bench's limit, you are Wanted.
wf-bench-ticket-outlawry = You are outlawed. Any citizen may bring you before the Bench.
wf-bench-ticket-notes = Notes: { $notes }

wf-bench-notify = Bench ticket #{ $number }: { $paid } thlr taken from your account, { $owed } owed.
wf-bench-notify-outlawry = Bench ticket #{ $number }: you are outlawed.
wf-bench-garnished = { $amount } thlr of your earnings went to your Bench debt. { $owed } still owed.
wf-bench-now-wanted = The Bench has marked you Wanted.
wf-bench-pardoned = Bench ticket #{ $number } pardoned. { $amount } thlr returned to you.

wf-bench-history-ticket = Bench ticket #{ $number }: { $charges }. Fine { $total }, { $paid } taken.
wf-bench-history-pardon = Bench ticket #{ $number } pardoned.
wf-bench-wanted-debt = Owes the Bench { $owed } thlr.
wf-bench-wanted-outlaw = Outlawed by Bench ticket #{ $number }.

wf-bench-warrant-author = Bench of Caelestinus
wf-bench-warrant-description = { $name } is outlawed by the Bench: { $charges }.
wf-bench-warrant-paid = Bench warrant on { $name } claimed. { $amount } thlr paid to you.

guide-entry-wf-common-writ = The Common Writ

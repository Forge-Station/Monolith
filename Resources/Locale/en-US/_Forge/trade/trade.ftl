nc-store-window-title = Trade Terminal
nc-store-select-category = Select a category
nc-store-search-placeholder = Search listings...
nc-store-tab-buy = Buy
nc-store-tab-sell = Sell
nc-store-tab-contracts = Contracts

nc-store-cat-ready-short = Ready
nc-store-cat-crate-short = In crate
nc-store-cat-ready-full = Ready to sell
nc-store-cat-crate-full = Ready to sell (in crate)
nc-store-category-fallback = Miscellaneous

nc-store-mass-sell-button = Sell crate contents
nc-store-mass-sell-tooltip = Quickly sells everything inside a crate.
    Requirements:
    • The crate must be closed.
    • You must be pulling the crate.
nc-store-mass-sell-tooltip-with-reward = { nc-store-mass-sell-tooltip }

    Estimated value: { $reward }
nc-store-only-mass-sell = This listing can only be sold in bulk through a closed crate.
nc-store-show-more = Show more ({ $count })
nc-store-empty-search = No listings match your search.
nc-store-empty-category-search = This category has no listings matching your search.
nc-store-search-results-buy = Search results (buy): { $count }
nc-store-search-results-sell = Search results (sell): { $count }

nc-store-no-stock = Out of stock
nc-store-buying-finished = Limit reached
nc-store-in-stock = In stock: { $count }
nc-store-will-buy = Wanted: { $count }
nc-store-owned = You have: { $count }

nc-store-no-access = Access denied
nc-store-popup-no-access = You do not have access to this trade terminal.
nc-store-popup-too-far = You are too far from the trade terminal.
nc-store-popup-crate-open = Close the crate before selling its contents.
nc-store-popup-no-crate = This operation requires a closed crate that you are pulling.
nc-store-popup-crate-too-far = The crate is too far from the trade terminal.
nc-store-popup-invalid-listing = This listing is no longer available.
nc-store-popup-transaction-failed = The transaction failed.

nc-store-contracts-empty = No active contracts are available. Check back later.
nc-store-contracts-category-empty = No contracts are currently available in this category.
nc-store-contract-category-all = All
nc-store-contract-category-button = { $name } ({ $count })
nc-store-contract-category-all-tooltip = Show every available contract.
nc-store-contract-category-tooltip = Show contracts in the "{ $category }" category.
nc-store-contract-title-fallback = Contract
nc-store-contract-badge-single = One-time
nc-store-contract-badge-single-tooltip =
    This contract can only be completed once per shift.
    It will disappear after completion.
nc-store-contract-badge-taken = ACTIVE
nc-store-contract-badge-taken-tooltip = You already accepted this contract. It can no longer be removed from the board.
nc-store-contract-badge-completed = READY
nc-store-contract-badge-completed-tooltip = The job is done. Turn in the result to claim payment.
nc-store-contract-badge-awaiting-ghost-role = WAITING
nc-store-contract-badge-awaiting-ghost-role-tooltip = Waiting for an operator. If nobody takes the role in time, the contract will fail.
nc-store-contract-badge-ghost-role-active = TARGET ACTIVE
nc-store-contract-badge-ghost-role-active-tooltip = The target is in the field. Complete the order requirements and deliver them to the trade terminal.

nc-store-contract-goals-header = Order goals:
nc-store-contract-turn-in-header = Turn in at the machine:
nc-store-contract-turn-in-note = After completion: { $item }
nc-store-contract-reward-header = Reward:
nc-store-contract-action-claim = Complete contract
nc-store-contract-action-claim-progress = Turn in partial cargo ({ $progress }/{ $required })
nc-store-contract-action-can-claim = Ready to turn in
nc-store-contract-action-can-claim-proof = Ready, proof must be turned in
nc-store-contract-action-not-taken = Not accepted
nc-store-contract-action-not-done = Not complete
nc-store-contract-action-take = Accept contract
nc-store-contract-action-skip = Replace ({ $cost } { $currency })
nc-store-contract-action-pinpointer = Issue pinpointer
nc-store-contract-action-pinpointer-tooltip = Issue a new pinpointer for the current active contract target.

nc-store-contract-confirm-take-title = Accept Contract
nc-store-contract-confirm-skip-title = Skip Contract
nc-store-contract-confirm-take = Accept contract "{ $contract }"?
nc-store-contract-confirm-skip = Skip contract "{ $contract }" and replace it with a new one?
nc-store-contract-confirm-take-action = Accept
nc-store-contract-confirm-skip-action = Skip
nc-store-contract-confirm-no = No

nc-store-contract-claim-tooltip-single = Complete this one-time contract and receive the full reward.
nc-store-contract-claim-tooltip-repeatable = Turn in the current result and receive the reward.
nc-store-contract-claim-tooltip-partial = Turn in the available cargo. The reward is paid after full delivery.
nc-store-contract-claim-tooltip-not-done = The contract requirements are not complete yet.
nc-store-contract-take-tooltip = Accept this contract. Accepted contracts cannot be skipped.
nc-store-contract-skip-tooltip =
    Remove this contract from the board and replace it with a new one.
    Cost: { $cost } { $currency }.

nc-store-contract-completed = Contract completed!
nc-store-contract-partial-turned-in = Partial cargo accepted.
nc-store-contract-taken = Contract accepted.
nc-store-contract-take-failed = Failed to accept contract.
nc-store-contract-skipped = Contract removed. A new one is now available.
nc-store-contract-skip-failed = Failed to replace contract. Not enough funds.
nc-store-contract-pinpointer-issued = Pinpointer issued.
nc-store-contract-pinpointer-issue-failed = Failed to issue pinpointer.
nc-store-contract-active-deadline-line = Time until failure: { $time }.
nc-store-contract-active-timeout = The contract time limit expired. The contract has failed.

nc-store-contract-goal-line = { $item }: { $count }
nc-store-contract-goal-inline = { $item } x{ $count }
nc-store-contract-desc-default = Complete the contract requirements and claim the reward.
nc-store-contract-desc-generated = Required: { $goals }
nc-store-contract-progress-caption = Progress
nc-store-contract-progress-caption-delivered = Delivered
nc-store-contract-progress-value = { $progress } / { $required }
nc-store-currency-format = { $amount } { $currency }
nc-store-unknown-item = ???
nc-store-proto-tooltip-name-only = { $name }
nc-store-proto-tooltip = { $name }
    { $desc }
nc-store-contract-reward-none = No reward listed
nc-store-contract-reward-item-line = { $item } x{ $count }

nc-store-contract-type-delivery = Delivery
nc-store-contract-type-delivery-tooltip = A regular order to deliver required goods.
nc-store-contract-type-hunt = Hunt
nc-store-contract-type-hunt-tooltip = A target will appear after acceptance. Eliminate it, take proof, and return for payment.
nc-store-contract-type-ghost-role = Special Target
nc-store-contract-type-ghost-role-tooltip = Accepting this opens a special role. If someone takes it, a living target appears.
nc-store-contract-type-artifact-study = Study
nc-store-contract-type-artifact-study-tooltip = A contract artifact appears after acceptance. Fully reveal its nodes and bring it to the terminal.
nc-store-contract-offer-pool-tooltip = Offer group: { $pool }

nc-store-contract-route-source-line = Pickup: { $hint }
nc-store-contract-route-destination-line = Dropoff: { $hint }
nc-store-contract-route-proof-bearer-note = A delivery receipt will appear after dropoff. It is bearer-held: whoever brings it back receives the reward.
nc-store-contract-route-status-available = Route is not accepted yet.
nc-store-contract-route-status-progress = Cargo delivered: { $progress } / { $max }.
nc-store-contract-route-status-delivered = Cargo delivered. Complete the route.
nc-store-contract-route-status-find-cargo = Find the cargo and deliver it along the route.
nc-store-contract-route-status-proof-bearer = Delivery confirmed. Return the proof to the trader; the bearer receives the reward.
nc-store-contract-route-status-proof-return = Delivery confirmed. Return to the trader with the proof.
nc-store-contract-route-status-store-cargo-ready = Cargo delivered. Claim the reward from the trader.
nc-store-contract-route-status-ready = Route complete. Claim the reward from the trader.
nc-store-contract-route-action-available = Accept the delivery route.
nc-store-contract-route-action-progress = Deliver cargo: { $progress } / { $max }.
nc-store-contract-route-action-proof-after-delivery = Fully deliver the cargo to receive one proof of delivery.
nc-store-contract-route-action-wait-confirmation = Wait for delivery confirmation.
nc-store-contract-route-action-proof-bearer = Bring the proof to the trader. It can be handed off, stolen, or sold.
nc-store-contract-route-action-proof = Bring the proof to the trader.
nc-store-contract-route-action-store-cargo-ready = Reward is available from the trader. No proof is needed.
nc-store-contract-route-action-ready = Claim the reward from the trader.

nc-store-contract-hunt-mode-trophy = Turn in trophy
nc-store-contract-hunt-mode-trophy-tooltip = A final trophy appears after the last required kill. Bring it to the trader.
nc-store-contract-hunt-mode-unknown = Special hunt
nc-store-contract-hunt-trophy-turn-in-header = Turn in trophy:
nc-store-contract-hunt-trophy-turn-in-note = A trophy appears after the final target: { $item }. Take it and bring it to the trader.
nc-store-contract-hunt-trophy-status-available = Contract targets will appear after acceptance.
nc-store-contract-hunt-trophy-status-progress = Kill contract targets: { $progress }/{ $required }. The trophy has not appeared yet.
nc-store-contract-hunt-trophy-status-ready = Trophy acquired. Bring it to the trader and complete the order.
nc-store-contract-hunt-trophy-action-available = Accept the hunt.
nc-store-contract-hunt-trophy-action-progress = Targets: { $progress }/{ $required }. The trophy appears after the last one.
nc-store-contract-hunt-trophy-action-ready = Turn in the trophy to the trader.
nc-store-contract-hunt-mode-body = Bring body
nc-store-contract-hunt-mode-body-tooltip = The marked target's body is the proof. Kill the targets and drag the required body to the trader.
nc-store-contract-hunt-body-turn-in-header = Bring body:
nc-store-contract-hunt-body-turn-in-note = After the kill, drag the body to the trader: { $item }.
nc-store-contract-hunt-body-status-available = Contract targets will appear after acceptance. The marked body must be brought to the trader.
nc-store-contract-hunt-body-status-progress = Kill targets: { $progress }/{ $required }. Do not destroy the marked body; it must be delivered.
nc-store-contract-hunt-body-status-ready = All targets killed. Drag the marked body to the trader and complete the order.
nc-store-contract-hunt-body-action-available = Accept the hunt.
nc-store-contract-hunt-body-action-progress = Targets: { $progress }/{ $required }. Preserve the body.
nc-store-contract-hunt-body-action-ready = Drag the body to the trader.
nc-store-contract-hunt-target-lost = The hunt target was lost before all stages were completed. The contract has failed.
nc-store-contract-hunt-next-target-spawn-failed = Could not spawn the next hunt target stage. The contract has failed.
nc-store-contract-drone-hunt-mode = Drone intercept
nc-store-contract-drone-hunt-mode-tooltip = An enemy combat drone appears after acceptance. Find the contract proof core and bring it back to the trader.
nc-store-contract-drone-hunt-turn-in-header = Turn in core:
nc-store-contract-drone-hunt-turn-in-note = The combat drone carries the proof item: { $item }. Take it and bring it to the trader.
nc-store-contract-drone-hunt-status-available = The contract combat drone appears after acceptance.
nc-store-contract-drone-hunt-status-progress = Recover the combat drone contract core: { $progress }/{ $required }.
nc-store-contract-drone-hunt-status-ready = Proof core acquired. Bring it to the trader and complete the order.
nc-store-contract-drone-hunt-action-available = Accept the intercept.
nc-store-contract-drone-hunt-action-progress = Proof core: { $progress }/{ $required }. Recover it from the combat drone.
nc-store-contract-drone-hunt-action-ready = Turn in the proof core to the trader.
nc-store-contract-drone-hunt-target-lost = The contract combat drone and proof core were lost. The contract has failed.

nc-store-contract-artifact-study-status-available = A contract artifact will appear after acceptance.
nc-store-contract-artifact-study-status-active = Fully study the contract artifact.
nc-store-contract-artifact-study-status-progress = Revealed nodes: { $triggered }/{ $total }. Keep studying the artifact.
nc-store-contract-artifact-study-status-bring = All nodes are revealed. Bring the artifact to the terminal so it can read the data.
nc-store-contract-artifact-study-status-ready = The artifact is fully studied. The contract can be completed.
nc-store-contract-artifact-study-action-available = Accept the study order.
nc-store-contract-artifact-study-action-progress = Reveal every artifact node and bring the artifact to the terminal.
nc-store-contract-artifact-study-action-ready = Complete the contract at the trader.
nc-store-contract-artifact-study-target-lost = The contract artifact was lost. The contract has failed.

nc-store-contract-ghost-role-timeout = Nobody took this role in time. The contract has failed.
nc-store-contract-ghost-role-target-lost = The target was lost before the operation began. The contract has failed.
nc-store-contract-ghost-role-target-rotten = The target rotted. The contract has failed.
nc-store-contract-ghost-role-survival-succeeded = The target survived the allotted time. The contract has failed.
nc-store-contract-ghost-role-waiting-line = Waiting for an operator: { $time }
nc-store-contract-ghost-role-waiting-action = Waiting: { $time }
nc-store-contract-ghost-role-active-line = The target is in the field. Complete the order requirements and deliver them to the trade terminal.
nc-store-contract-ghost-role-active-short = Target active
nc-store-contract-ghost-role-survival-line = Time until failure: { $time }.
nc-store-contract-ghost-role-survival-action = Timer: { $time }
nc-store-contract-ghost-role-mode-alive = Deliver alive
nc-store-contract-ghost-role-mode-alive-hint = The target must be alive, free of normal damage, cuffed, near the trader, and not rotting.
nc-store-contract-ghost-role-mode-dead = Deliver dead
nc-store-contract-ghost-role-mode-dead-hint = Bring the target body to the trader before it rots. If it rots or is destroyed, the contract fails.
nc-store-contract-ghost-role-mode-unknown = Special delivery
nc-store-contract-ghost-role-mode-line-short = Mode: { $mode }.
nc-store-contract-ghost-role-hint-waiting = Wait until someone accepts the special role.
nc-store-contract-ghost-role-hint-deliver = Deliver the target to the trader.
nc-store-contract-ghost-role-hint-alive-revive = The target is dead. This order requires them revived, healed of normal damage, and cuffed.
nc-store-contract-ghost-role-hint-alive-cuff = The target is alive but not cuffed. Apply cuffs.
nc-store-contract-ghost-role-hint-alive-heal = The target is wounded. Heal normal damage before turn-in.
nc-store-contract-ghost-role-hint-alive-ready = The target is alive, healed of normal damage, and cuffed. Ready for turn-in.
nc-store-contract-ghost-role-hint-dead-kill = This order requires the target body. Kill the target and deliver the body.
nc-store-contract-ghost-role-hint-dead-deliver = The target body is ready. Deliver it to the trader before it rots.
nc-store-contract-ghost-role-hint-dead-ready = The body has been delivered. Ready for turn-in.
nc-store-contract-ghost-role-character-briefing = Contract role: { $contract }.
    { $description }
nc-store-contract-ghost-role-character-briefing-survival = Contract role: { $contract }.
    { $description }
    { $survival }
nc-store-contract-ghost-role-survival-briefing = Survive for { $time }. If you last until the end, the hunters' contract fails.
nc-store-contract-ghost-role-survival-objective-title = Survive: { $contract }
nc-store-contract-ghost-role-survival-objective-title-live = Survive: { $time }
nc-store-contract-ghost-role-survival-objective-title-done = Survival complete
nc-store-contract-ghost-role-survival-objective-description = Survive for { $time }. If you survive, the contract against you will fail.
nc-store-contract-ghost-role-roundend-header = [bold]Contract target results[/bold]
nc-store-contract-ghost-role-roundend-line = - Contract "{ $contract }": { $role } ({ $player }) - { $result }
nc-store-contract-ghost-role-roundend-unknown-role = unknown target
nc-store-contract-ghost-role-roundend-no-player = unclaimed
nc-store-contract-ghost-role-roundend-result-waiting = target was not claimed
nc-store-contract-ghost-role-roundend-result-active = target was not delivered by round end
nc-store-contract-ghost-role-roundend-result-delivered-alive = target delivered alive; contract completed
nc-store-contract-ghost-role-roundend-result-delivered-dead = target killed/delivered; contract completed
nc-store-contract-ghost-role-roundend-result-survived = target survived the timer ({ $time }); contract failed
nc-store-contract-ghost-role-roundend-result-not-accepted = target was not claimed; contract failed
nc-store-contract-ghost-role-roundend-result-target-lost = target was lost; contract failed
nc-store-contract-ghost-role-roundend-result-target-rotten = target body rotted; contract failed

nc-store-contract-delivery-target-lost = Cargo was lost. The contract has failed.
nc-store-contract-proof-generation-failed = Proof of completion could not be created. The contract has failed.
nc-store-contract-proof-destroyed = Proof item for this contract was destroyed; contract failed.
nc-store-contract-runtime-stage = Stage: { $stage } of { $goal }
nc-store-contract-duration-hours = { $count ->
    [one] { $count } hour
   *[other] { $count } hours
}
nc-store-contract-duration-minutes = { $count ->
    [one] { $count } minute
   *[other] { $count } minutes
}
nc-store-contract-duration-seconds = { $count ->
    [one] { $count } second
   *[other] { $count } seconds
}

## Executions and weapon suicides: what bystanders see happen to the head, by the weapon's kind and strength.
## A key ending in -machine is used instead when the head is a chassis part.

wolfmed-execution-ballistic-weak = { CAPITALIZE(THE($victim)) }'s head snaps back, blood pumping from the wound!
wolfmed-execution-ballistic-weak-machine = { CAPITALIZE(THE($victim)) }'s head snaps back, fluid running from the hole!
wolfmed-execution-ballistic-medium = The back of { THE($victim) }'s head bursts open!
wolfmed-execution-ballistic-heavy = { CAPITALIZE(THE($victim)) }'s head comes apart!

wolfmed-execution-energy-weak = A smoking hole is burned through { THE($victim) }'s head!
wolfmed-execution-energy-medium = { CAPITALIZE(THE($victim)) }'s head is burned black, through and through!
wolfmed-execution-energy-heavy = { CAPITALIZE(THE($victim)) }'s head burns away to ash!
wolfmed-execution-energy-heavy-machine = { CAPITALIZE(THE($victim)) }'s head melts down to slag!

wolfmed-execution-blade-weak = { CAPITALIZE(THE($victim)) }'s throat opens, blood pumping out!
wolfmed-execution-blade-weak-machine = { CAPITALIZE(THE($victim)) }'s neck is cut open, fluid running out!
wolfmed-execution-blade-medium = { CAPITALIZE(THE($victim)) }'s throat is cut to the bone!
wolfmed-execution-blade-medium-machine = { CAPITALIZE(THE($victim)) }'s neck is cut half through!
wolfmed-execution-blade-heavy = { CAPITALIZE(THE($victim)) }'s head comes off!

wolfmed-execution-blunt-weak = { CAPITALIZE(THE($victim)) }'s skull cracks under the blow!
wolfmed-execution-blunt-weak-machine = { CAPITALIZE(THE($victim)) }'s head casing buckles under the blow!
wolfmed-execution-blunt-medium = { CAPITALIZE(THE($victim)) }'s skull caves in!
wolfmed-execution-blunt-medium-machine = { CAPITALIZE(THE($victim)) }'s head is beaten out of shape!
wolfmed-execution-blunt-heavy = { CAPITALIZE(THE($victim)) }'s head is crushed to pulp!
wolfmed-execution-blunt-heavy-machine = { CAPITALIZE(THE($victim)) }'s head is crushed to scrap!

## A blunt weapon's own Execute lines, in place of the throat-slitting ones: set on each weapon's Execution component.

wolfmed-execution-bludgeon-initial-internal = You raise { THE($weapon) } over { THE($victim) }'s head.
wolfmed-execution-bludgeon-initial-external = { CAPITALIZE(THE($attacker)) } raises { POSS-ADJ($attacker) } { $weapon } over { THE($victim) }'s head.
wolfmed-execution-bludgeon-complete-internal = You bring { THE($weapon) } down on { THE($victim) }'s skull!
wolfmed-execution-bludgeon-complete-external = { CAPITALIZE(THE($attacker)) } brings { POSS-ADJ($attacker) } { $weapon } down on { THE($victim) }'s skull!

wolfmed-execution-bludgeon-self-initial-internal = You raise { THE($weapon) } to your own head.
wolfmed-execution-bludgeon-self-initial-external = { CAPITALIZE(THE($attacker)) } raises { POSS-ADJ($attacker) } { $weapon } to { POSS-ADJ($attacker) } own head.
wolfmed-execution-bludgeon-self-complete-internal = You bring { THE($weapon) } down on your own skull!
wolfmed-execution-bludgeon-self-complete-external = { CAPITALIZE(THE($attacker)) } brings { POSS-ADJ($attacker) } { $weapon } down on { POSS-ADJ($attacker) } own skull!

## "Are you sure?": what the executor is asked before either Execute verb starts, and the line a weapon that
## will not kill gets instead.

wolfmed-execution-confirm-title = Execute
wolfmed-execution-confirm-text = Kill { THE($victim) } with { THE($weapon) }? This cannot be taken back.
wolfmed-execution-confirm-accept = Execute
wolfmed-execution-confirm-deny = Cancel

wolfmed-execution-confirm-self-title = End your life
wolfmed-execution-confirm-self-text = End your own life with { THE($weapon) }? You will not be able to return to this body.
wolfmed-execution-confirm-self-text-plain = Shoot yourself in the head with { THE($weapon) }?
wolfmed-execution-confirm-self-accept = Do it
wolfmed-execution-confirm-self-deny = Cancel

wolfmed-execution-refuse = { CAPITALIZE(THE($weapon)) } won't kill anyone.

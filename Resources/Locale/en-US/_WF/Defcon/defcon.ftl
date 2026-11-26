wf-defcon-sender = Sector Defense Command
wf-defcon-title = DEFCON {$level}: {$name}
wf-defcon-chip-title = DEFCON {$level}
wf-defcon-pda = DEFCON: [color={$color}]{$level}[/color]
wf-defcon-body-rising = The sector is now at DEFCON {$level}, {$name}.
wf-defcon-body-falling = The sector has stood down to DEFCON {$level}, {$name}.
wf-defcon-body-reasons = Reported threats:
wf-defcon-body-reason = - {$reason}

wf-defcon-name-5 = FADE OUT
wf-defcon-name-4 = DOUBLE TAKE
wf-defcon-name-3 = ROUND HOUSE
wf-defcon-name-2 = FAST PACE
wf-defcon-name-1 = COCKED PISTOL

wf-defcon-desc-5 = No known threats. Normal traffic.
wf-defcon-desc-4 = Hostile activity reported. Keep watch.
wf-defcon-desc-3 = Sustained hostile activity. Arm up and fly in company.
wf-defcon-desc-2 = The sector is under attack. Civilians keep close to stations.
wf-defcon-desc-1 = The sector is overrun. All crews to battle stations.

wf-defcon-reason-ads = ADS incursion at {$tier}
wf-defcon-reason-reaver = Reaver campaign at {$tier}
wf-defcon-reason-war = Open war between the TSF and the PDV
wf-defcon-reason-threats = {$count ->
    [one] One hostile contact in the sector
   *[other] {$count} hostile contacts in the sector
}
wf-defcon-reason-distress = {$count ->
    [one] One open distress call
   *[other] {$count} open distress calls
}
wf-defcon-reason-deaths = {$count ->
    [one] One death reported in the last few minutes
   *[other] {$count} deaths reported in the last few minutes
}
wf-defcon-reason-bias = Sector Command assessment

cmd-wf_defcon-desc = Shows and drives the sector DEFCON.
cmd-wf_defcon-help = Usage: {$command} status | set <1-5> | lock <1-5> | unlock | bias <score> | eval
    set moves the level now and lets it follow the threat again; lock holds it until unlock; bias adds a score on top of the sources; eval scores the threat now.
cmd-wf_defcon-hint-sub = Subcommand
cmd-wf_defcon-hint-level = Level, 1 (severest) to 5
cmd-wf_defcon-hint-bias = Score to add
cmd-wf_defcon-bad-level = The level must be 1 to 5.
cmd-wf_defcon-bad-bias = The bias must be a number.
cmd-wf_defcon-set = DEFCON set to {$level}.
cmd-wf_defcon-locked = DEFCON locked at {$level}.
cmd-wf_defcon-unlocked = DEFCON follows the threat again.
cmd-wf_defcon-biased = DEFCON bias set to {$bias}.
cmd-wf_defcon-status = DEFCON {$level} ({$name}), score {$score}, floor {$floor}{$locked ->
    [true] , locked
   *[false] {""}
}
cmd-wf_defcon-status-line = {"  "}{$score}: {$reason}

cmd-wf_defconpanel-desc = Opens the sector DEFCON panel.
cmd-wf_defconpanel-help = Usage: {$command}

wf-defcon-panel-button = Sector DEFCON
wf-defcon-panel-title = Sector DEFCON
wf-defcon-panel-level = [font size=20][color={$color}]DEFCON {$level}: {$name}[/color][/font]
wf-defcon-panel-status = Score {$score}, floor DEFCON {$floor}{$locked ->
    [true] , locked
   *[false] {""}
}
wf-defcon-panel-disabled = DEFCON is off (wf.defcon.enabled).
wf-defcon-panel-set = Set level
wf-defcon-panel-level-button = {$level}
wf-defcon-panel-lock = Lock
wf-defcon-panel-lock-tooltip = Hold the level until unlocked, whatever the threat.
wf-defcon-panel-announce = Announce
wf-defcon-panel-unlock = Unlock
wf-defcon-panel-threat = Threat
wf-defcon-panel-bias = Bias
wf-defcon-panel-bias-tooltip = Score added on top of the sources. Negative calms the sector.
wf-defcon-panel-bias-apply = Apply
wf-defcon-panel-evaluate = Score now
wf-defcon-panel-no-threats = No threats reported.
wf-defcon-panel-reason = {$score}: {$reason}

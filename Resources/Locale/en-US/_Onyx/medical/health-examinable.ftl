# WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
health-examinable-pain-light = [color=yellow]hurts[/color]
health-examinable-pain-strong = [color=orange]hurts badly[/color]
health-examinable-pain-terrible = [color=red]hurts terribly[/color]
health-examinable-pain-agony = [color=crimson]is in agony[/color]
health-examinable-part-title-self = [font size=11][color=DarkGray]You check yourself for injuries.[/color][/font]
health-examinable-part-title-other = [font size=11][color=DarkGray]You check { $entity } for injuries.[/color][/font]
health-examinable-part-summary = [bold]{ CAPITALIZE($part) }[/bold]: { $severity }
health-examinable-part-summary-pain = [bold]{ CAPITALIZE($part) }[/bold]: { $severity }, { $pain }
health-examinable-part-chat-line = [font size=10]{ $summary }[/font]
health-examinable-part-chat-line-details = [font size=10]{ $summary }
    { "    " }[color=Gray]{ $details }[/color][/font]
health-examinable-part-injuries = [color=#B8B8B8]Injuries:[/color] { $types }
health-examinable-part-damage-blunt = bruises
health-examinable-part-damage-slash = cuts
health-examinable-part-damage-piercing = puncture wounds
health-examinable-part-damage-heat = burns
health-examinable-part-damage-cold = frostbite
health-examinable-part-damage-shock = electrical burns
health-examinable-part-damage-caustic = chemical burns
health-examinable-part-severity-none = [color=green]fine[/color]
health-examinable-part-severity-minor = [color=yellow]slightly damaged[/color]
health-examinable-part-severity-moderate = [color=orange]damaged[/color]
health-examinable-part-severity-severe = [color=red]badly damaged[/color]
health-examinable-part-severity-critical = [color=crimson]mangled[/color]
health-examinable-part-wound-stabilized = { $count } stabilized { $count ->
    [one] wound
   *[other] wounds
}
health-examinable-part-wound-closed = { $count } closed { $count ->
    [one] wound
   *[other] wounds
}
health-examinable-part-incision-open = { $count } open { $count ->
    [one] incision
   *[other] incisions
}
health-examinable-part-bleeding = active bleeding
wound-examine-fracture-hairline = slight swelling
wound-examine-fracture-simple = severe swelling
wound-examine-fracture-displaced = unnatural deformation
wound-examine-fracture-comminuted = shattered bone
wound-examine-frame-hairline = thin frame cracks
wound-examine-frame-simple = deep frame cracks
wound-examine-frame-displaced = deformed frame
wound-examine-frame-comminuted = destroyed frame
health-examinable-part-scars = { $count } { $count ->
    [one] scar
   *[other] scars
}

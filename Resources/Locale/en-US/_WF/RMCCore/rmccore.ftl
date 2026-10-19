# RMC-14 armor/rmc-block-backpack.ftl
rmc-block-backpack-cant-backpack = You can't equip that on your back while wearing this armor.
rmc-block-backpack-cant-other = You can't equip that while wearing a backpack.

# RMC-14 armor/rmc-examine-armor.ftl
rmc-armor-melee = strike resistance
rmc-armor-bullet = ballistic resistance
rmc-armor-bio = biohazard protection
rmc-armor-explosion-armor = blast absorption
rmc-examine-armor =  - A [color=yellow]{$text}[/color] rating of [color=lightblue]{$value}[/color].
rmc-examine-armor-xeno-header = [color=white]{CAPITALIZE(SUBJECT($xeno))} has:[/color]
rmc-examine-armor-xeno = - [color=lightblue]{$armor}[/color] [color=yellow]armor[/color]
rmc-examine-armor-xeno-frontal = - [color=lightblue]{$armor}[/color] [color=yellow]frontal armor[/color]
rmc-examine-armor-xeno-side = - [color=lightblue]{$armor}[/color] [color=yellow]side armor[/color]
rmc-examine-armor-xeno-explosion = - [color=lightblue]{$armor}[/color] [color=yellow]explosion armor[/color]
rmc-examine-armor-piercing-immune = [color=cyan]This has immunity to armor piercing rounds![/color]

# RMC-14 chat/emotes.ftl
rmc-hands-emotes-highfive-attempt-self = You raise your hand out for a high five from {$target}.
rmc-hands-emotes-highfive-attempt = {$ent} raises {POSS-ADJ($ent)} hand out for a high five from {$target}.
rmc-hands-emotes-highfive-self = You give {$target} a high five!
rmc-hands-emotes-highfive = {$ent} gives {$target} a high five!
rmc-hands-emotes-hug-self = You give {$target} a hug!
rmc-hands-emotes-hug = {$ent} gives {$target} a hug!
rmc-hands-emotes-hug-attempt-self = You raise your arms out for a hug from {$target}.
rmc-hands-emotes-hug-attempt = {$ent} raises {POSS-ADJ($ent)} arms out for a hug from {$target}.
rmc-hands-emotes-fistbump-self = You give {$target} a fistbump!
rmc-hands-emotes-fistbump = {$ent} gives {$target} a fistbump!
rmc-hands-emotes-fistbump-attempt-self = You raise your fist out for a fistbump from {$target}.
rmc-hands-emotes-fistbump-attempt = {$ent} raises {POSS-ADJ($ent)} fist out for a fistbump from {$target}.
rmc-hands-emotes-handshake-self = You and {$target} shake hands!
rmc-hands-emotes-handshake = {$ent} and {$target} shake hands!
rmc-hands-emotes-handshake-attempt-self = You reach out your hand for a handshake from {$target}.
rmc-hands-emotes-handshake-attempt = {$ent} reaches {POSS-ADJ($ent)} hand out for a handshake from {$target}.
rmc-hands-emotes-tailswipe-self = We clash our tail with {$target}!
rmc-hands-emotes-tailswipe = {$ent} clashes their tail with {$target}!
rmc-hands-emotes-tailswipe-attempt-self = We raise our tail out for a tail swipe from {$target}.
rmc-hands-emotes-tailswipe-attempt = {$ent} raises their tail out for a swipe from {$target}.
rmc-hands-emotes-left-hanging = You were left hanging!
rmc-hands-emotes-fistbump-perform = Perform a fistbump
rmc-hands-emotes-highfive-perform = Perform a high five
rmc-hands-emotes-hug-perform = Give a hug
rmc-hands-emotes-handshake-perform = Perform a handshake
rmc-hands-emotes-tailswipe-perform = Perform a tail swipe
rmc-hands-emotes-get-closer = Get closer.

# RMC-14 climb/rmc-climb.ftl
rmc-climb-prevented-by-obstacles = You can't climb through that path!

# RMC-14 deaf/deafness.ftl
rmc-deaf-start = You notice you can't hear anything... you're deaf!
rmc-deaf-end = You start hearing things again!

# RMC-14 mobs/obstacles.ftl
rmc-obstacle-slam-self = You slam into {THE($object)}!
rmc-obstacle-slam-others = {$ent} slams into {THE($object)}!

# RMC-14 pulling/pulling.ftl
cm-pull-whitelist-denied = We have no use for {THE($name)}, why would we want to touch it?
cm-pull-whitelist-denied-dead = {THE($name)} is dead, why would we want to touch it?
rmc-pull-paralyze-self = You try to pull {THE($pulled)}, but get a swipe to the head!
rmc-pull-paralyze-others = {$puller} tries to pull {THE($pulled)}, but gets a swipe to the head instead!
rmc-pull-infect-self = You try to pull {$pulled}, but get jumped on and infected in the process!
rmc-pull-infect-others = {$puller} tries to pull {$pulled}, but gets jumped on and infected in the process!
rmc-prevent-pull-alive = You can't pull {THE($target)} while {SUBJECT($target)} {CONJUGATE-BE($target)} alive!

# RMC-14 skills/rmc-skills.ftl
rmc-skills-cant-use = You don't know how to use {THE($item)}...

# RMC-14 tackle/tackle.ftl
cm-tackle-try-self = You try to tackle {$target}
cm-tackle-try-target = {$user} tries to tackle you
cm-tackle-try-observer = {$user} tries to tackle {$target}
cm-tackle-success-self = You tackle down {$target}!
cm-tackle-success-target = {$user} tackled you down!
cm-tackle-success-observer = {$user} tackles down {$target}!

# RMC-14 ui.ftl
rmc-dialog-input-placeholder-default = Enter text
rmc-dialog-input-placeholder-smart-check = More than 3 consecutive spaces will be trimmed

# RMC-14 vent-crawl.ftl
rmc-vent-crawling-welded = It's welded shut!
rmc-vent-crawling-full = It's full!
rmc-vent-crawling-moving = Something squeezes though the ducts...
rmc-vent-crawling-entrance-xeno = [color=yellow]We can vent crawl into this.[/color]

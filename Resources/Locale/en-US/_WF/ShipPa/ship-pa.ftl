# Ship PA situation codes (Resources/Prototypes/_WF/ShipPa/alert_codes.yml).
ship-alert-code-green = Code Green
ship-alert-code-green-desc = Normal operations. No known threats.
ship-alert-code-green-announcement = All hands, condition green. Resume normal operations.

ship-alert-code-yellow = Code Yellow
ship-alert-code-yellow-desc = Elevated readiness. Stand by for orders.
ship-alert-code-yellow-announcement = All hands, condition yellow. Secure loose equipment and stand by at your stations.

ship-alert-code-red = Code Red
ship-alert-code-red-desc = Hostile action underway. General quarters.
ship-alert-code-red-announcement = All hands, condition red. Set general quarters and secure all compartments. This is not a drill.

ship-alert-code-black = Code Black
ship-alert-code-black-desc = Ship loss imminent. Abandon ship.
ship-alert-code-black-announcement = All hands, abandon ship. Proceed to the nearest escape pod or docking port. This is not a drill.

# General quarters.
ship-pa-general-quarters-announcement = General quarters, general quarters. All hands man your battle stations.
ship-pa-general-quarters-secure = Secure from general quarters. All hands stand down.

# Announcements.
ship-pa-sender = {$ship} PA
ship-pa-unknown-ship = Unknown vessel
# Speech bubble over each speaker; the client renders BubbleHeader/BubbleContent like ordinary speech.
ship-pa-bubble = [BubbleHeader][bold]{$sender}[/bold][/BubbleHeader] [BubbleContent][color={$color}]{$message}[/color][/BubbleContent]
ship-pa-announce-cooldown = The PA system is still cycling. Wait a moment before announcing again.
ship-pa-announce-no-speakers = No functioning PA speakers aboard to carry that.

# Speaker examine text.
ship-pa-speaker-examine-ok = The status light glows a steady green. It's ready to carry ship announcements.
ship-pa-speaker-examine-damaged = The status light flickers amber, and faint static crackles from the grille.
ship-pa-speaker-examine-broken = The grille is cracked and silent. It won't carry a signal like this.
ship-pa-speaker-examine-unpowered = The status light is dark. It has no power.

# Console commands (Content.Server/_WF/ShipPa/Commands/ShipPaCommands.cs).
cmd-shippa_announce-desc = Broadcasts a PA announcement on a ship.
cmd-shippa_announce-help = Usage: {$command} <grid> <text...>

cmd-shippa_code-desc = Sets a ship's alert code.
cmd-shippa_code-help = Usage: {$command} <grid> <WFShipCodeX>

cmd-shippa_gq-desc = Toggles general quarters on a ship.
cmd-shippa_gq-help = Usage: {$command} <grid> <true|false>

cmd-shippa_speakers-desc = Lists a ship's PA speakers and their status.
cmd-shippa_speakers-help = Usage: {$command} <grid>
cmd-shippa_speakers-online = {$online}/{$total} online

# Shared command errors and hints.
cmd-shippa-invalid-grid = '{$grid}' is not a grid entity.
cmd-shippa-invalid-code = '{$code}' is not a ship alert code.
cmd-shippa-no-speakers = No functioning PA speakers on that grid; nothing was heard.
cmd-shippa-hint-code = <ship alert code>
cmd-shippa-hint-active = <true|false>

# Guidebook.
guide-entry-ship-pa = Ship PA System

## Console internet sounds
ship-pa-sound-disabled = The PA won't take outside audio.
ship-pa-sound-cooldown =
    { $seconds ->
        [one] Wait { $seconds } second before requesting another track.
       *[other] Wait { $seconds } seconds before requesting another track.
    }
ship-pa-sound-refused = The PA won't play that.
ship-pa-sound-queued = Fetching the track. It plays as each listener receives it.
ship-pa-playlist-added = Added to the queue, number {$position}.
ship-pa-playlist-full = The queue is full. Wait for a track to end or take one out.

ship-pa-subtitle-title = {$ship} · PA

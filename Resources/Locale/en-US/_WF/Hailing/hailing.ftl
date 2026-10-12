hail-console-button = HAIL
hail-console-button-tooltip = Hail another ship.
hail-window-title = Hailing

hail-unknown-vessel = Unknown vessel

hail-pa-sender = { $ship } comms
hail-pa-incoming = Incoming hail from { $ship }. Helm, respond.

hail-key-incoming = INCOMING
hail-key-calling = HAILING
hail-key-open = ON CALL
hail-key-closed = CLOSED
hail-key-declined = DECLINED
hail-key-no-answer = NO ANSWER
hail-key-busy = BUSY
hail-key-lost = LOST
hail-key-no-link = NO LINK

hail-strip-incoming = INCOMING: { $ship }
hail-strip-calling = HAILING: { $ship }
hail-strip-open = OPEN: { $ship }
hail-strip-hail = HAIL
hail-strip-answer = ANSWER
hail-strip-decline = DECLINE
hail-strip-cancel = CANCEL
hail-strip-view = VIEW
hail-strip-end = END

hail-banner-incoming = INCOMING HAIL: { $ship }
hail-banner-calling = HAILING: { $ship }
hail-banner-open = CHANNEL OPEN: { $ship }

hail-answer = Answer
hail-decline = Decline
hail-cancel = Cancel
hail-close-channel = Close channel
hail-back = Back to contacts

hail-contacts-heading = Ships in range
hail-no-contacts = No ships in range.
hail-contact-hail = Hail
hail-contact-busy = Busy
hail-contact-distance = { $distance } m

hail-screen-no-signal = NO SIGNAL
hail-screen-calling = HAILING
hail-screen-incoming = INCOMING HAIL
hail-screen-closed = CHANNEL CLOSED
hail-screen-no-visual = NO VISUAL

hail-chat-placeholder = Transmit...
hail-chat-listening = Only the person who took the call can transmit.
hail-chat-send = Send
hail-chat-sender = { $name }:{ " " }
hail-log-open = Channel open with { $ship }.

hail-ended-closed = Channel closed.
hail-ended-peer-closed = { $ship } closed the channel.
hail-ended-declined = { $ship } declined the hail.
hail-ended-no-answer = No answer from { $ship }.
hail-ended-ringing = The hail from { $ship } ended.
hail-ended-unavailable = That ship can't be reached.
hail-ended-busy = That ship is on another channel.
hail-ended-lost = Signal lost.
hail-ended-cooldown = The hailing array is resetting. Try again shortly.

hail-god-name = God
hail-pa-god = God is hailing { $ship }. Helm, respond.
hail-admin-verb = Hail as God
hail-admin-verb-description = Ring this ship's helm as God and talk to whoever answers. Nobody sees who you are.
hail-admin-no-console = That ship has no powered shuttle console free to ring.

hail-test-ship-name = Hail Test Vessel
hail-test-greeting = This is a test vessel. Anything you send comes straight back.
hail-test-reply = Receiving you: "{ $text }"

cmd-hailtest-desc = Brings in a test ship near the one you are on and has its pilot hail you. It also answers your hails, echoes what you send and removes itself after a few idle minutes.
cmd-hailtest-help = Usage: hailtest [clear]
cmd-hailtest-no-grid = Stand on the ship you want hailed.
cmd-hailtest-no-console = This ship has no powered shuttle console free to ring.
cmd-hailtest-started = A test ship is hailing { $ship }. You can hail it back from the HAIL list.
cmd-hailtest-cleared = Removed { $count } test { $count ->
    [one] ship
   *[other] ships
}.

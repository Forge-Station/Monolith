wf-round-setup-row-recruiting = Recruiting
wf-round-setup-row-recruiting-tooltip = This crew is looking for members.

wf-round-setup-scenario-formed = Formed Crew
wf-round-setup-formed-description = A crew formed mid-round around characters already playing. Nobody respawns or changes gear. Pick up a ship from a member's deed under Your ship.
wf-round-setup-formed-create = Form a crew
wf-round-setup-formed-create-help = You're already in the round, so the crew forms around your current character. Add openings on the Crew page, then invite players or post for crew.
wf-round-setup-formed-no-ship = This crew has no ship to spawn on yet.

wf-round-setup-live-join-help = You join with the character you're playing now. Your ID, job and gear stay as they are; you get the crew radio (:k) and crew ship access.
wf-round-setup-live-unavailable = Characters already in the round can only join Formed crews and launched Ship crews.

wf-round-setup-recruiting = Recruiting
wf-round-setup-recruiting-help = Recruiting crews sit at the top of every crew list with a pulsing border.
wf-round-setup-recruiting-toggle = Looking for crew
wf-round-setup-recruiting-hidden = Not shown yet. Recruiting needs Public or Request access, late joining on, and a free slot in an opening.

wf-round-setup-posting = Post for crew
wf-round-setup-posting-help = Send an in-character open invitation to every player who isn't in a crew. Turns recruiting on. Once every 10 minutes.
wf-round-setup-posting-placeholder = e.g. Salvage run to the debris field, need two hands and a medic.
wf-round-setup-posting-send = Post
wf-round-setup-posting-wait = You posted recently.
wf-round-setup-posting-cooldown = You can post again in { $minutes } { $minutes ->
    [one] minute
   *[other] minutes
}.
wf-round-setup-posting-invalid = Write a posting of up to 200 characters.
wf-round-setup-posting-too-soon = You posted recently. Wait before posting again.
wf-round-setup-posting-closed = Nobody can join right now. Use Public or Request access, allow late joining and add a free opening first.
wf-round-setup-posting-sent = Posting sent.
wf-round-setup-posting-chat = [color=#7FD3C4][bold]Crew Board:[/bold] { $crew } ({ $openings } open), posted by { $host }:[/color] { $message }

wf-round-setup-posting-card-title = Crew posting
wf-round-setup-posting-card-crew = { $crew } · { $difficulty } · { $openings } open
wf-round-setup-posting-card-message = { $host }: "{ $message }"
wf-round-setup-posting-card-view = View
wf-round-setup-posting-card-join-help = Open this crew in Crew Setup to pick a role and join.
wf-round-setup-posting-card-request-help = Open this crew in Crew Setup to pick a role and ask to join.
wf-round-setup-posting-card-dismiss = Dismiss
wf-round-setup-posting-card-mute = Mute
wf-round-setup-posting-card-mute-help = Stop receiving crew postings. Set wf.round_setup.show_postings to true to get them back.

cmd-crewunpost-desc = Withdraws a crew's posting and stops it recruiting.
cmd-crewunpost-help = Usage: crewunpost <crew id>. The id is in the admin log entry for the posting.
cmd-crewunpost-invalid = Give one crew id.
cmd-crewunpost-missing = No crew with id { $id }.
cmd-crewunpost-done = Withdrew crew { $id }'s posting.

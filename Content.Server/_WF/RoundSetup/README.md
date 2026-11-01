# RoundSetup

Crew Setup lets players organize a Crashlanded, Ship or Faction Crew start together before the round, or launch a new start mid-round. Hosts control public access, private requests, codes, direct invitations and whether survivors may join after launch. Each member chooses their own character through the existing character selector and selects their own permitted job.

The lobby retains the repository's menu column, character hero, docked chat and bottom information strip. Crew Setup sits directly above an equally sized red/green READY button before the round. READY opens a chooser with Start a crew (Default), which opens Crew Setup, or Ready up normally (Legacy), which uses saved job preferences after leaving any current crew. Closing the chooser does not change readiness, and READY — Cancel clears existing readiness directly. Mid-round there is one Join Round button, offering Crew (Default) for scenario setup or Standard (Legacy) for station jobs. Crew setup uses a distinct accent colour. The hero yields space to the menu at narrow widths, and secondary options remain accessible by scrolling. The footer measures its link text, including Changelog (new), instead of clipping it to a fixed height. Lobby and crew preparation panels are translucent, while text stays opaque and the full-screen background remains clear.

Compact crew rows appear below Leave Server and open full details when clicked; the welcome information is pinned below the scrolling menu. The setup browser uses the same rows, with Crews and Create tabs. Leader portraits show only public appearance, with existing job icons used throughout. Hosts can name their crew, write a plain-text description of up to 512 characters and switch between eligible crew types before launch without recreating it. Descriptions appear in crew details and can be edited in Who can join, including after launch. Changing type clears the vessel choice and readiness while retaining members, compatible roles and admission settings. Editing a character clears both normal and crew readiness. Once a player belongs to a crew, its preparation, ship, crew and access sections replace the browsing navigation across the top. The ship page places selection and departure controls beside a flexible preview without a page scrollbar; ship descriptions and preview controls have tooltips. Selectors and buttons wrap long text, pages scroll at smaller sizes, and confirmation and red Leave Crew buttons stay visible. Confirmation closes setup after the server accepts readiness or a mid-round join, including explicit ship purchase consent.

Open Crew Setup from the lobby, the group icon beside Character in the top-left gameplay toolbar, or the in-round game menu. The toolbar button stays pressed while setup is open and toggles it closed. The Invite players panel stays visible at the top of Who can join, with a Send invitation action and an explanation when nobody is eligible. Hosts invite connected players from this panel; only players awaiting a spawn and outside other crews appear in the picker. Recipients choose an available role in the invited crew and accept. Ready up before round start; a pregame group launches only when its host is ready. Mid-round hosts launch their waiting group explicitly. A disconnected host keeps ownership; a host can transfer ownership to another member or leave to pass it to the next member. Respawning leaves the previous crew and releases its reserved slot; remaining members retain their crew and an existing ship stays in the world. Round restart clears all lobbies and invitation codes.

The server owns membership and validates every request. Scenario job slots remain available when a helm is disabled or unpowered; ordinary shuttle power rules do not override crew admission. Normal pregame readiness and Standard joining do not create a scenario crew; only Ship, Crashlanded and Faction Crew can create one. Standard opens the existing picker unchanged, so players can still take station jobs or join a player-owned ship's crew. Only private scenario stations are hidden from it and rejected by server spawn validation. Every contributor’s balance is checked before the hull loads, so an unfunded crew never triggers a grid load. Lobby snapshots go out only when something changes (a request, a character edit, a connect or disconnect, a run-level change or an expired request), plus one safety refresh every 30 seconds. Players on the lobby screen, crew leaders and invitees always receive them; an in-round player receives them for 60 seconds after their last request, and the open Crew Setup window renews that every 20 seconds. Crashlanded uses a random civilian vessel and planet, a fixed three-minute preparation period with disabled flight controls, and the existing planetary descent and crash systems. Living occupants hear a looping alarm and see a shared on-screen countdown until atmospheric entry, highlighted for the final thirty seconds. Leaving the ship, dying or entering atmosphere clears the warning and alarm. The countdown never increases during client clock corrections. Up to three small, randomly spaced bursts flash and boom on the deck during preparation, leaving five-second cosmetic flames; these do not damage entities, ignite anything, alter the atmosphere or break tiles. Survivors receive an emergency EVA suit, breath mask and a survival kit with species-appropriate gas, a filled medkit, health analyser, compact repair tools and extinguisher, lantern, water, food and a harmonica alongside the existing medicine, rations and signalling tools; existing clothing is preserved. Later survivors spawn on a surviving floor in that group's wreck.

Crashlanded draws equally from twelve existing civilian designs: Guppy, Peregrine, Rosetta, Pillbox, Arribane, Noble, Sakuratsu, Shuilian, Spessbite, Takeaway, Tiger and Zhuangzi. This mixes mining, research, medical, food-service, botany, atmospheric and cargo layouts. A crew keeps its chosen hull and wreck for later arrivals.

Crashlanded requires the existing `wf.planet_networks` server setting and a sector containing registered planets. Its card is disabled when planet networks are off. Closing late admission prevents new members; players already accepted retain their place until the host removes them.

Ship crew lets the host select from CC’s ordinary, expedition, medical, scrap and C-Sec shipyard catalogs, subject to the selected qualified job’s ID access, narrow the list by shipyard class, and inspect the design with the embedded Wolfgate ship previewer, including pan, zoom and fit controls. Selecting a ship is free; the leader initially pays the listed price, or assigns each member a whole-number contribution on the Crew page. Shares must total the price. Each contributor explicitly approves the current allocation for their selected character; changing the ship, departure, payment plan or leader invalidates approvals. Midround leaders who approve before the others can reopen Crew Setup and launch once all contributors are ready and connected. The leader retains the deed. Loadouts are paid for separately from the remaining character balance. The server rechecks price, funds, Ironman restrictions and active ship limits. Failed purchases refund every contributor’s original character; if preferences are unavailable, the account, original character slot and amount are queued independently of the scenario across round restarts and retried after preferences finish loading; zero-share members and later arrivals pay nothing for the vessel. Hosts choose random clear sector space or CC; the central station is identified by its existing sector-service host component. Station starts first try a compatible, collision-checked berth and otherwise search clear space around that same station. A missing station or fully obstructed surroundings refuse launch before payment. Changing location clears readiness and purchase consent. The ship appears at the selected location and uses normal deeds, repair data and ownership. It starts in Crew access mode: issued IDs of current members open its default readers, with a separate Crew rule available per door. Membership changes update access immediately; respawning or leaving removes crew credentials while preserving ownership and explicitly granted guest permissions. Join Normally never authorizes a ship purchase.

New lobbies contain only the host, with an independent personal job choice and no advertised crew roles. New openings default to one slot. Ship hosts can advertise civilian and medical jobs independently of their own eligibility, including the existing Emergency Responder job (MdMedic). Medical, salvage and service rosters add editable openings without overwriting occupied roles. Hosts choose from the same job catalog on Prepare and can give themselves a custom title such as Captain; the title does not grant station command privileges. Job bans and qualifications still apply to each recruit. Hosts can edit vacancies and capacities after launch; existing spawned identities remain unchanged. Hosts can name crew roles, choose an underlying job and set 1 to 16 slots per role (up to 16 roles). Joining or accepting a request reserves a role; disconnected and spawned members retain their slots while they remain in the crew. Choosing Leave Crew or respawning releases membership and its reserved slot. Occupied roles cannot change jobs or be removed. Personal and advertised crew titles appear on the spawned ID, mind and station record while the underlying job still determines equipment and access. Public names always come from the selected character, never the account identifier. Crew details include a public roster of character names, advertised roles and readiness; private membership IDs, invitations, payment shares and balances are omitted from visitor snapshots.

After launch, the crew leader can use Your ship to choose a ship assigned to any connected crew member's worn ID, including after a crash start. The leader confirms their own ship directly; another owner receives a direct request and must approve it in Crew Setup. Owners can decline and leaders can cancel; changed deeds or leadership invalidate pending requests. The server rechecks the deed, membership, faction and surviving deck at approval; faction crews keep their original faction, rank rules and station job quotas. Replacement ships are already owned, so the starting T0/T1 purchase catalog does not limit later upgrades. Reassignment charges nothing and moves no players or ships. Automatic crew access leaves the old hull and applies to the new one, while deeds and manually granted permissions remain intact. Direct crew notices and ship PA announce the change; later joins and announcements use the replacement and its surviving fragments, and never fall back to an abandoned hull. Crew admission records use a separate private station so an existing ship's station and normal jobs are not rewritten. Saved setups retain their original starting design, not a round-specific ship entity.

Ship preparation follows Prepare, Your ship, Crew and Who can join, with a fixed Next action on the first three pages and Finish on the last. Prepare fits without a scrollbar; ship selection commits directly from its dropdown. Finish saves admission settings and confirms readiness or purchase and launch, while a ready member can reopen setup to revoke consent. Role selection shows the underlying job loadout and selected character before joining. Prepare opens the existing Wolfgate loadout picker even for jobs hidden in legacy preferences; choices save to that character and retain normal costs, restrictions and fallback equipment. Background snapshots wait until typing and open selectors finish before rebuilding controls. Disabled actions and locked job choices explain their requirements on hover. Job sprites scale to a readable 24-pixel size.

Hosts can save up to 20 named scenarios from Who can join. Create has a Load Saved Scenario button; its compact rows open full details with load and delete actions. Saves belong to the server account and persist in versioned files under the server user-data `round-setups` directory. They contain the crew name and description, scenario, vessel, departure, personal job/title, recruitment roles and admission settings, without members, invite codes or purchase authorization. Loading validates the current catalog and creates a new unready crew; a personal job the character no longer qualifies for is left unselected. Saving under an existing name replaces it. Writes retain a backup, and unreadable saves are protected from automatic overwrite.

Pregame lasts six minutes by default, including the Monolith server preset. For local playtests, the default-off `wf.round_setup.demo_enabled` setting seeds a public Crashlanded lobby when the real player named by `wf.round_setup.demo_host` connects. It has a ready host and three explicit Vagrant openings. See `Tools/_WF/RoundSetup/README.md` for the headless host launcher. Normal servers never create demo lobbies.

Faction Crew starts support the regular TSF and PDV command ladders and specialists, plus Viper Group (Infanteer, Lieutenant, Commander), USSP (Rifleman, Sergeant, Commissar) and the Underground (Fugitive, Godfather). Hosts must qualify for an available faction job; that selected job caps advertised roles, and each recruit must independently qualify. Special deployments such as MARSOC, Tarkhan and admin command remain in their existing systems. TSF and PDV offer only T0 and T1 ships from the existing faction voucher prototypes, including downstream additions; VG, USSP and the Underground, which have no tier vouchers, offer every ship in their shipyard group (Viper, Ussp, Syndicate). Before the round, faction jobs on base POIs the preset is certain to spawn (Camelot for USSP and VG, the Underground Black Market) count as available. Population balancing covers TSF and PDV only. Faction crews choose random space or their own faction base, identified by its existing command-job ledger even when that job is occupied. Base starts dock when possible and otherwise use clear space nearby. Faction ships use the same individually approved contributions and refund path as civilian ships. Spawning consumes the normal faction station job slot and creates its ordinary records, equipment, access, company and mind role; only spawn coordinates change. Existing faction population balancing remains active. Faction personal and advertised roles cannot be renamed; IDs, minds and records retain their official job titles. Older saved faction titles are cleared on load, and their former CC departure becomes faction base departure. A host can manage openings mid-round, but cannot promote a spawned character by editing a title or role.

Hosts can set a ship name on Your ship before launch; Next or Enter saves it. Purchase applies it to the hull, deed and shuttle records. Saved scenarios retain the name and faction. Accepted joins send every connected crew member a direct character-name notification; launched ships also announce the arrival over functioning PA speakers. Failed requests and reconnects do not announce a new membership.

Spawned crew members receive a private radio channel (`:k`) named after their crew, using the existing radio speech, languages and jamming pipeline. Membership is checked on every transmission and delivery; other crews and all-channel listeners cannot hear it. Leaving or respawning removes it without changing existing job radio channels. An SL arrow at the bottom of the status alerts points to the current leader beyond view range, follows leadership changes, turns red when the leader is down, and its tooltip names the leader, any downed, missing or off-map state, and the crew radio key. It clears on respawn and disconnect. Captains receive a non-modal bottom-right join-request card with Accept, Deny and Later; Later keeps the request in Who can join. Cards never take keyboard focus and stale role approvals are rejected. Crew purchases also initialize the normal shipyard sale service, so their owner can sell at a compatible dock without first buying another ship; occupancy and docking checks still apply.

Down With Ye' Ship! starts the crew aboard the SY Reach, free and with no deed, on clear space between drone zones 1 and 2 (the first two `WFDroneRingsComponent` radii, else 4-8 km out). The first arrival springs `WFDoomedReaverAmbush`: three Reaver gunships at tier 4 or 5, 800 m out on three sides, that attack the Reach and fight until nobody aboard is left (`disengage: Destroy`). Their crews fire on sight, and the Reach's FTL is jammed until the encounter resolves. The Reach uses crew access, so the crew opens its doors without owning it. Ship crew jobs apply.

Launched crews show how they are doing. Each spawned member's roster entry reads Alive, Critical, Dead or Missing (body deleted), crew details and the own-crew page show how many deployed members are alive, and crew rows replace the aboard count with a living count, red when nobody is left. The ship line rates the largest surviving grid against the largest deck seen since that ship was assigned: intact from 90%, damaged from 50%, wrecked below that, destroyed with no grid left. Statuses are sampled every two seconds and a change sends a snapshot. When every deployed member is down or the ship is wrecked or destroyed, the join and accept buttons show a warning and need a second press. Rules live in `WFCrewStatusPolicy`; the server side is `WFRoundSetupSystem.Status`.

Every scenario card, crew detail and crew row shows a colour-coded difficulty from `WFRoundSetupPolicy.Difficulty`: Ship and Faction Crew are Normal (green), Crashlanded Very Hard (orange), Down With Ye' Ship! Impossible (red). Easy (blue) and Hard (yellow) exist for later scenarios.

Players already in the round can form or join a crew without respawning (live members). Crew Setup's Create tab offers them one choice, Formed Crew: it starts at once around the host's current character, with no launch or purchase, and picks up a ship from a member's deed under Your ship. Live members can join Formed crews and launched Ship crews by request, invitation or open access; they take a free opening as a label only, keep their ID, job and gear, and get the crew radio, leader arrow, status tracking and crew ship access. Hosts can remove live members. Unspawned players join a Formed crew only once it has a ship to spawn on. Rules live in `WFRoundSetupPolicy.AllowsLiveMembers`; the server side is `WFRoundSetupSystem.Live`.

Hosts turn on Looking for crew in Who can join. A crew shows as recruiting only while outsiders could take an opening (Public or Request access, late joining on, a free slot); recruiting crews sort to the top of every crew list and the ordinary late-join ship picker with a slow pulsing border. Post for crew sends a 200-character in-character posting, at most every 10 minutes, as a Crew Board chat line and a bottom-right card to every connected player outside a crew who could join it. The card's View opens the crew in Crew Setup; Mute sets the client setting `wf.round_setup.show_postings` to false. Postings go to the admin log, and `crewunpost <id>` withdraws one. The server side is `WFRoundSetupSystem.Postings`; the card is `WFCrewPostingCardSystem`.

Entry points: `WFRoundSetupSystem`, `GameTicker.RoundSetup`, `WFCrashScenarioSystem`, `WFDoomedScenarioSystem`, `WFShipScenarioSystem`, and the client Crew Setup window.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/RoundSetup/GameTicker.RoundSetup.cs`](GameTicker.RoundSetup.cs)
- [`Content.Server/_WF/RoundSetup/RadioSystem.Crew.cs`](RadioSystem.Crew.cs)
- [`Content.Server/_WF/RoundSetup/ShuttleConsoleSystem.RoundSetup.cs`](ShuttleConsoleSystem.RoundSetup.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioComponent.cs`](WFCrashScenarioComponent.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioMachinerySystem.cs`](WFCrashScenarioMachinerySystem.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioSystem.Countdown.cs`](WFCrashScenarioSystem.Countdown.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioSystem.cs`](WFCrashScenarioSystem.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioSystem.Effects.cs`](WFCrashScenarioSystem.Effects.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioSystem.Equipment.cs`](WFCrashScenarioSystem.Equipment.cs)
- [`Content.Server/_WF/RoundSetup/WFCrashScenarioSystem.Flight.cs`](WFCrashScenarioSystem.Flight.cs)
- [`Content.Server/_WF/RoundSetup/WFCrewUnpostCommand.cs`](WFCrewUnpostCommand.cs)
- [`Content.Server/_WF/RoundSetup/WFDoomedScenarioComponent.cs`](WFDoomedScenarioComponent.cs)
- [`Content.Server/_WF/RoundSetup/WFDoomedScenarioSystem.cs`](WFDoomedScenarioSystem.cs)
- [`Content.Server/_WF/RoundSetup/WFPlayerCrewRadioComponent.cs`](WFPlayerCrewRadioComponent.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Broadcast.cs`](WFRoundSetupSystem.Broadcast.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Companies.cs`](WFRoundSetupSystem.Companies.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.cs`](WFRoundSetupSystem.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Demo.cs`](WFRoundSetupSystem.Demo.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Factions.cs`](WFRoundSetupSystem.Factions.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Hangar.cs`](WFRoundSetupSystem.Hangar.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Live.cs`](WFRoundSetupSystem.Live.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Notifications.cs`](WFRoundSetupSystem.Notifications.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Payments.cs`](WFRoundSetupSystem.Payments.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Postings.cs`](WFRoundSetupSystem.Postings.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Roles.cs`](WFRoundSetupSystem.Roles.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Saved.cs`](WFRoundSetupSystem.Saved.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.ShipAccess.cs`](WFRoundSetupSystem.ShipAccess.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Ships.cs`](WFRoundSetupSystem.Ships.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Signals.cs`](WFRoundSetupSystem.Signals.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Spawning.cs`](WFRoundSetupSystem.Spawning.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Status.cs`](WFRoundSetupSystem.Status.cs)
- [`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Titles.cs`](WFRoundSetupSystem.Titles.cs)
- [`Content.Server/_WF/RoundSetup/WFSafeSpawnSystem.cs`](WFSafeSpawnSystem.cs)
- [`Content.Server/_WF/RoundSetup/WFShipScenarioComponent.cs`](WFShipScenarioComponent.cs)
- [`Content.Server/_WF/RoundSetup/WFShipScenarioSystem.Assignment.cs`](WFShipScenarioSystem.Assignment.cs)
- [`Content.Server/_WF/RoundSetup/WFShipScenarioSystem.Catalog.cs`](WFShipScenarioSystem.Catalog.cs)
- [`Content.Server/_WF/RoundSetup/WFShipScenarioSystem.cs`](WFShipScenarioSystem.cs)
- [`Content.Server/_WF/RoundSetup/WFShipScenarioSystem.Docking.cs`](WFShipScenarioSystem.Docking.cs)
- [`Content.Server/_WF/RoundSetup/WFShipScenarioSystem.Factions.cs`](WFShipScenarioSystem.Factions.cs)
- [`Content.Server/_WF/RoundSetup/WFShipScenarioSystem.Payments.cs`](WFShipScenarioSystem.Payments.cs)
- [`Content.Server/_WF/RoundSetup/WFShipScenarioSystem.Refunds.cs`](WFShipScenarioSystem.Refunds.cs)

### Shared

- [`Content.Shared/_WF/RoundSetup/WFCrashCountdownClock.cs`](../../../Content.Shared/_WF/RoundSetup/WFCrashCountdownClock.cs)
- [`Content.Shared/_WF/RoundSetup/WFCrashCountdownEvent.cs`](../../../Content.Shared/_WF/RoundSetup/WFCrashCountdownEvent.cs)
- [`Content.Shared/_WF/RoundSetup/WFCrewPostingEvent.cs`](../../../Content.Shared/_WF/RoundSetup/WFCrewPostingEvent.cs)
- [`Content.Shared/_WF/RoundSetup/WFCrewSignalEvent.cs`](../../../Content.Shared/_WF/RoundSetup/WFCrewSignalEvent.cs)
- [`Content.Shared/_WF/RoundSetup/WFCrewStatus.cs`](../../../Content.Shared/_WF/RoundSetup/WFCrewStatus.cs)
- [`Content.Shared/_WF/RoundSetup/WFFactionCrewPolicy.cs`](../../../Content.Shared/_WF/RoundSetup/WFFactionCrewPolicy.cs)
- [`Content.Shared/_WF/RoundSetup/WFRoundSetupCVars.cs`](../../../Content.Shared/_WF/RoundSetup/WFRoundSetupCVars.cs)
- [`Content.Shared/_WF/RoundSetup/WFRoundSetupMessages.cs`](../../../Content.Shared/_WF/RoundSetup/WFRoundSetupMessages.cs)
- [`Content.Shared/_WF/RoundSetup/WFRoundSetupPolicy.cs`](../../../Content.Shared/_WF/RoundSetup/WFRoundSetupPolicy.cs)
- [`Content.Shared/_WF/RoundSetup/WFSavedSetup.cs`](../../../Content.Shared/_WF/RoundSetup/WFSavedSetup.cs)
- [`Content.Shared/_WF/RoundSetup/WFShipCrewJobs.cs`](../../../Content.Shared/_WF/RoundSetup/WFShipCrewJobs.cs)

### Client

- [`Content.Client/_WF/RoundSetup/LobbyCharacterPreviewPanel.RoundSetup.cs`](../../../Content.Client/_WF/RoundSetup/LobbyCharacterPreviewPanel.RoundSetup.cs)
- [`Content.Client/_WF/RoundSetup/LobbyGui.RoundSetup.cs`](../../../Content.Client/_WF/RoundSetup/LobbyGui.RoundSetup.cs)
- [`Content.Client/_WF/RoundSetup/LobbyState.RoundSetup.cs`](../../../Content.Client/_WF/RoundSetup/LobbyState.RoundSetup.cs)
- [`Content.Client/_WF/RoundSetup/PickerWindow.RoundSetup.cs`](../../../Content.Client/_WF/RoundSetup/PickerWindow.RoundSetup.cs)
- [`Content.Client/_WF/RoundSetup/WFCrashCountdownHud.cs`](../../../Content.Client/_WF/RoundSetup/WFCrashCountdownHud.cs)
- [`Content.Client/_WF/RoundSetup/WFCrashCountdownHudSystem.cs`](../../../Content.Client/_WF/RoundSetup/WFCrashCountdownHudSystem.cs)
- [`Content.Client/_WF/RoundSetup/WFCrewHudSystem.cs`](../../../Content.Client/_WF/RoundSetup/WFCrewHudSystem.cs)
- [`Content.Client/_WF/RoundSetup/WFCrewLocatorHud.cs`](../../../Content.Client/_WF/RoundSetup/WFCrewLocatorHud.cs)
- [`Content.Client/_WF/RoundSetup/WFCrewPostingCardSystem.cs`](../../../Content.Client/_WF/RoundSetup/WFCrewPostingCardSystem.cs)
- [`Content.Client/_WF/RoundSetup/WFLobbyLayout.cs`](../../../Content.Client/_WF/RoundSetup/WFLobbyLayout.cs)
- [`Content.Client/_WF/RoundSetup/WFRecruitingGlow.cs`](../../../Content.Client/_WF/RoundSetup/WFRecruitingGlow.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundJoinChoiceWindow.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundJoinChoiceWindow.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupClientSystem.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupClientSystem.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupCrewRow.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupCrewRow.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupLobbyPanel.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupLobbyPanel.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupOptionButton.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupOptionButton.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupUIController.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupUIController.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Descriptions.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Descriptions.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Identity.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Identity.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Invitations.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Invitations.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Layout.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Layout.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Live.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Live.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Loadouts.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Loadouts.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Payments.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Payments.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Preview.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Preview.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Refresh.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Refresh.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Roles.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Roles.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Saved.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Saved.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.ScenarioColumns.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.ScenarioColumns.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.ShipName.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.ShipName.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Ships.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Ships.cs)
- [`Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Status.cs`](../../../Content.Client/_WF/RoundSetup/WFRoundSetupWindow.Status.cs)
- [`Content.Client/_WF/RoundSetup/WFScenarioDifficultyLabel.cs`](../../../Content.Client/_WF/RoundSetup/WFScenarioDifficultyLabel.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/RoundSetup/CrashCountdownTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/CrashCountdownTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/CrashEffectsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/CrashEffectsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/CrashEquipmentTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/CrashEquipmentTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/CrashScenarioTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/CrashScenarioTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/CrewLoadoutTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/CrewLoadoutTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/DoomedScenarioTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/DoomedScenarioTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/RoundSetupDemoTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/RoundSetupDemoTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/RoundSetupUiTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/RoundSetupUiTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/RoundSetupUiTest.Refresh.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/RoundSetupUiTest.Refresh.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/ShipScenarioTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/ShipScenarioTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/ShipScenarioTest.Expeditions.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/ShipScenarioTest.Expeditions.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/ShipScenarioTest.Payments.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/ShipScenarioTest.Payments.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/ShipScenarioTest.Sales.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/ShipScenarioTest.Sales.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/WFCrewRadioTestSystem.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/WFCrewRadioTestSystem.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupEligibilityTestSystem.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupEligibilityTestSystem.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.Broadcast.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.Broadcast.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.Burst.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.Burst.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.Comms.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.Comms.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.Factions.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.Factions.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.Live.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.Live.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.Payments.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.Payments.cs)
- [`Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.ShipAssignment.cs`](../../../Content.IntegrationTests/Tests/_WF/RoundSetup/WFRoundSetupLobbyTest.ShipAssignment.cs)

### Unit tests

- [`Content.Tests/_WF/RoundSetup/WFCrashCountdownClockTest.cs`](../../../Content.Tests/_WF/RoundSetup/WFCrashCountdownClockTest.cs)
- [`Content.Tests/_WF/RoundSetup/WFCrewRecruitingPolicyTest.cs`](../../../Content.Tests/_WF/RoundSetup/WFCrewRecruitingPolicyTest.cs)
- [`Content.Tests/_WF/RoundSetup/WFCrewStatusPolicyTest.cs`](../../../Content.Tests/_WF/RoundSetup/WFCrewStatusPolicyTest.cs)
- [`Content.Tests/_WF/RoundSetup/WFFactionCrewPolicyTest.cs`](../../../Content.Tests/_WF/RoundSetup/WFFactionCrewPolicyTest.cs)
- [`Content.Tests/_WF/RoundSetup/WFRoundSetupPolicyTest.cs`](../../../Content.Tests/_WF/RoundSetup/WFRoundSetupPolicyTest.cs)
- [`Content.Tests/_WF/RoundSetup/WFRoundSetupRolePolicyTest.cs`](../../../Content.Tests/_WF/RoundSetup/WFRoundSetupRolePolicyTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/RoundSetup/crash.yml`](../../../Resources/Prototypes/_WF/RoundSetup/crash.yml)
- [`Resources/Prototypes/_WF/RoundSetup/crash_effects.yml`](../../../Resources/Prototypes/_WF/RoundSetup/crash_effects.yml)
- [`Resources/Prototypes/_WF/RoundSetup/doomed.yml`](../../../Resources/Prototypes/_WF/RoundSetup/doomed.yml)
- [`Resources/Prototypes/_WF/RoundSetup/radio.yml`](../../../Resources/Prototypes/_WF/RoundSetup/radio.yml)
- [`Resources/Prototypes/_WF/RoundSetup/ship.yml`](../../../Resources/Prototypes/_WF/RoundSetup/ship.yml)

### Localization

- [`Resources/Locale/en-US/_WF/RoundSetup/countdown-hud.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/countdown-hud.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/crash-effects.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/crash-effects.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/crash.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/crash.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/crew-comms.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/crew-comms.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/crew-status.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/crew-status.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/doomed.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/doomed.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/faction-crew.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/faction-crew.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/flow.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/flow.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/home.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/home.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/join-flow.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/join-flow.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/lobby-panel.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/lobby-panel.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/preview.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/preview.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/recruiting.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/recruiting.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/round-setup.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/round-setup.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/server.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/server.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/ship-assignment.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/ship-assignment.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/ship.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/ship.ftl)
- [`Resources/Locale/en-US/_WF/RoundSetup/wizard.ftl`](../../../Resources/Locale/en-US/_WF/RoundSetup/wizard.ftl)

### Tools

- [`Tools/_WF/RoundSetup/README.md`](../../../Tools/_WF/RoundSetup/README.md)
- [`Tools/_WF/RoundSetup/start-test-host.ps1`](../../../Tools/_WF/RoundSetup/start-test-host.ps1)

## Non-modular edits

- [`Content.Client/_NF/LateJoin/Controls/CrewPickerControl.xaml.cs`](../../../Content.Client/_NF/LateJoin/Controls/CrewPickerControl.xaml.cs)
  - recruiting crews pulse
  - recruiting crews first
- [`Content.Client/Lobby/LobbyState.cs`](../../../Content.Client/Lobby/LobbyState.cs)
  - the normal jobs picker is managed by the Round Setup lobby partial
  - the preparation header supplies its own lobby context
  - keep room for the crew browser at 720p
  - the responsive layout owns the dock size
  - the primary action handles readiness and crew membership together
  - the primary action owns readiness instead of a toggle subscription
  - release live crew updates and the normal job picker
  - the lobby directly handles normal joining or opens the player's crew
  - keep crew management available while normal late join is disabled
  - synchronize direct joining and crew membership with lobby readiness
- [`Content.Client/Lobby/UI/LobbyGui.xaml`](../../../Content.Client/Lobby/UI/LobbyGui.xaml)
  - roundSetup namespace mounts the live player lobby browser
  - retain the repository lobby menu and hero with dedicated crew and ready actions
- [`Content.Client/Lobby/UI/LobbyGui.xaml.cs`](../../../Content.Client/Lobby/UI/LobbyGui.xaml.cs)
  - unify character and crew preparation controls
  - expose group setup before the round starts
  - character editing clears crew readiness too
  - Join no longer toggles, but character edits still clear pregame readiness
- [`Content.Client/Options/UI/EscapeMenu.xaml`](../../../Content.Client/Options/UI/EscapeMenu.xaml): hosts can manage admission and survivors after spawning
- [`Content.Client/UserInterface/BoundKeyHelpers.cs`](../../../Content.Client/UserInterface/BoundKeyHelpers.cs): a menu button with no key, such as crew setup, has no function to look up
- [`Content.Client/UserInterface/Systems/Alerts/Widgets/AlertsUI.xaml.cs`](../../../Content.Client/UserInterface/Systems/Alerts/Widgets/AlertsUI.xaml.cs): The crew locator shares this column and isn't an AlertControl.
- [`Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs`](../../../Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs): manage the player lobby during the round
- [`Content.Client/UserInterface/Systems/MenuBar/Widgets/GameTopMenuBar.xaml`](../../../Content.Client/UserInterface/Systems/MenuBar/Widgets/GameTopMenuBar.xaml): open crew setup from the gameplay toolbar
- [`Content.IntegrationTests/Tests/_NF/ShipyardTests.cs`](../../../Content.IntegrationTests/Tests/_NF/ShipyardTests.cs): deliberately discounted test vessels are not production catalog prices
- [`Content.Server/_Battlefield14/FactionTracking/FactionTrackingSystem.cs`](../../_Battlefield14/FactionTracking/FactionTrackingSystem.cs): count faction crews by their validated setup job without changing saved preferences
- [`Content.Server/GameTicking/GameTicker.Spawning.cs`](../../GameTicking/GameTicker.Spawning.cs)
  - ordinary joining excludes private scenario stations
  - explicit Normal choices share the regular job allocation
  - authorize the assigned group member while preserving the saved profile
  - require authenticated scenario membership on every spawn path
- [`Content.Server/Radio/EntitySystems/RadioSystem.cs`](../../Radio/EntitySystems/RadioSystem.cs): show the private crew channel name
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.cs)
  - failed scenario helms reject pilots until the crash is complete.
  - scenario crew admission is independent of helm power
- [`Content.Server/Station/Systems/StationJobsSystem.cs`](../../Station/Systems/StationJobsSystem.cs): private scenario stations stay out of the late-join job list
- [`Content.Shared/CCVar/CCVars.Game.cs`](../../../Content.Shared/CCVar/CCVars.Game.cs): give players six minutes to configure and join scenario lobbies.
- [`Resources/ConfigPresets/_Mono/monolithCore.toml`](../../../Resources/ConfigPresets/_Mono/monolithCore.toml): keep the Monolith preset's pregame setup window at six minutes.

<!-- WOLFGATE-GENERATED END -->

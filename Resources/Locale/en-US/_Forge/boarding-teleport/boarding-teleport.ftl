boarding-teleport-window-title = Bluespace boarding
boarding-teleport-window-status-header = Lock status
boarding-teleport-window-status-none = No target selected.
boarding-teleport-window-sector-help = Hover a shuttle or grid on the sector map and click to select a target.
boarding-teleport-window-grid-help = Click any tile on the selected grid map for the landing point (walls and sealed areas included).
boarding-teleport-window-back = Back to sector map
boarding-teleport-window-flavor-sector = [color=#9ecbff]Sector scan:[/color] find a target and establish a bluespace lock.
boarding-teleport-window-flavor-grid = [color=#ffd27f]Set your landing point.[/color] Return jumps only work from your platform's linked remote.
boarding-teleport-window-clear-target = Clear target
boarding-teleport-window-platform-cooldown = Platform recharge: {$seconds}s
boarding-teleport-window-engine-stats = Engine: {$range} range | target speed ≤ {$speed}
boarding-teleport-window-engine-missing = No bluespace engine on this grid
boarding-teleport-window-return-window = Return window: {$seconds}s
boarding-teleport-window-return-remaining = Return channel: {$seconds}s left
boarding-teleport-window-apc-risk = APC underload: +{$percent}% risk
boarding-teleport-window-mode-sector = [color=#87b7ff]Mode:[/color] search and lock
boarding-teleport-window-mode-grid = [color=#ffd27f]Mode:[/color] landing point
boarding-teleport-window-mode-ready = [color=#8dff99]Mode:[/color] channel stable, platforms ready
boarding-teleport-window-mode-summary-Stealth = [color=#6bb8ff]Mode:[/color] Stealth — moderate risk, balanced speed
boarding-teleport-window-mode-summary-Precise = [color=#9bff96]Mode:[/color] Precise — longer charge, minimal scatter
boarding-teleport-window-mode-summary-Rapid = [color=#ffae63]Mode:[/color] Rapid — short charge, high risk
boarding-teleport-window-mode-button-Stealth = Stealth
boarding-teleport-window-mode-button-Precise = Precise
boarding-teleport-window-mode-button-Rapid = Rapid
boarding-teleport-window-mode-stats = Delay: {$delay}s | Scatter: {$scatter} | Risk: {$risk}%
boarding-teleport-sector-settings = Settings
boarding-teleport-sector-scan = Scan sector
boarding-teleport-sector-select = Select boarding target
boarding-teleport-sector-objects = Objects in sector
boarding-teleport-sector-selected-none = Target: none
boarding-teleport-sector-selected-grid = Target: {$name}
boarding-teleport-sector-tip = Scan the sector first, then pick your target from the list.

boarding-teleport-status-None = No target selected.
boarding-teleport-status-TargetSelected = Target grid selected. Pick a landing tile.
boarding-teleport-status-LandingSelected = Landing point set. Platforms are ready.
boarding-teleport-status-InvalidTarget = This grid cannot be targeted.
boarding-teleport-status-TargetTooFar = Target is outside bluespace acquisition range.
boarding-teleport-status-TargetMoving = Target is moving too fast for a stable lock.
boarding-teleport-status-InvalidLanding = That tile cannot be used for landing (no tile or open space).
boarding-teleport-status-NoGrid = The console must be installed on a grid.
boarding-teleport-status-NoEngine = No bluespace engine on this grid. Install one on the same shuttle as the console.
boarding-teleport-status-TargetShielded = Target has active shields — bluespace lock denied.
boarding-teleport-status-TargetShieldTooStrong = Target shields exceed this engine tier. Upgrade the drive or wait for shields to drop.
boarding-teleport-status-SourceShieldBlocksTeleport = Your ship shield blocks outgoing bluespace boarding.
boarding-teleport-status-TargetInFtl = Target is in FTL — landing lock unavailable.
boarding-teleport-status-NoEnginePower = Bluespace engine is unpowered.
boarding-teleport-status-EngineRecharging = Bluespace engine is recharging after a jump.
boarding-teleport-status-TargetScrambled = Target grid has an active bluespace scrambler — lock denied.
boarding-teleport-status-TargetFriendly = Cannot board your own grid or a docked friendly vessel.
boarding-teleport-status-TargetGridProtected = This grid is protected — teleportation onto it is forbidden.
boarding-teleport-status-LockExpired = Landing lock expired. Reconfirm the target at the console.

boarding-teleport-window-sync-volley = Launch all ready platforms
boarding-teleport-window-lock-age = Lock age: {$seconds}s
boarding-teleport-window-lock-degrade = Lock drift: +{$scatter} scatter, +{$risk}% risk
boarding-teleport-window-platform-list-header = Linked platforms
boarding-teleport-window-platform-entry = Platform {$slot}: {$name} — {$cooldown} | Landing: {$landing}
boarding-teleport-window-platform-entry-ready = ready
boarding-teleport-window-platform-entry-cooldown = recharge {$seconds}s
boarding-teleport-window-platform-entry-landing-yes = custom
boarding-teleport-window-platform-entry-landing-no = shared

boarding-teleport-console-volley-none = No platforms are ready to launch.
boarding-teleport-console-volley-started =
    { $count ->
        [one] Synchronized volley: {$count} platform charging.
       *[other] Synchronized volley: {$count} platforms charging.
    }

boarding-teleport-platform-lock-broken = Bluespace lock broken — target is no longer valid!

boarding-teleport-remote-no-anchor = Return channel inactive.
boarding-teleport-remote-return-remaining = Return channel: {$seconds}s remaining.
boarding-teleport-remote-emergency-available = Return channel collapsed — emergency return available (high risk).
boarding-teleport-remote-return-expired = Return channel lost permanently.

alerts-boarding-teleport-return-name = Return channel
alerts-boarding-teleport-return-desc = Time left to return to your boarding platform.

boarding-teleport-platform-cooldown = Platform is still recharging.
boarding-teleport-platform-pending = The bluespace coil is already charging.
boarding-teleport-platform-departure-delay = Lock established. Jump in {$seconds}s.
boarding-teleport-platform-unpowered = Platform is unpowered.
boarding-teleport-platform-not-on-platform = Stand on the linked platform to depart.
boarding-teleport-platform-no-console = Platform is not linked to a console.
boarding-teleport-platform-no-target = No landing point selected on the console.
boarding-teleport-platform-wrong-platform = This remote is linked to another platform.
boarding-teleport-platform-destabilized = Bluespace destabilization! Spatial coherence disrupted.
boarding-teleport-platform-landing-invalid = Landing zone is no longer valid.
boarding-teleport-platform-home-invalid = Return platform unreachable. The bluespace anchor collapses.
boarding-teleport-platform-return-expired = Return channel collapsed.
boarding-teleport-platform-charge-cancelled = Charge aborted.

boarding-teleport-platform-return-started = Return channel opening. Jump in {$seconds}s.
boarding-teleport-platform-emergency-return-started = Emergency return! Unstable jump in {$seconds}s.

boarding-teleport-emergency-return-confirm-title = Emergency bluespace return
boarding-teleport-emergency-return-confirm-button = Jump
boarding-teleport-emergency-return-confirm-message = [color=#ffd27f]The return channel has collapsed.[/color] You may attempt a one-time emergency jump back to your platform. [bullet/] Charge time: [color=#ffae63]{$seconds}s[/color] [bullet/] Destabilization risk: [color=#ff6868]{$risk}%[/color] [bullet/] Landing scatter: [color=#ff6868]up to {$scatter} tiles[/color] [bullet/] Failure may [color=#ff6868]stun[/color] you and scramble your position. This can only be used once. Proceed only if you are stranded.
boarding-teleport-emergency-return-cancelled = Emergency return aborted.
boarding-teleport-emergency-return-pending = Confirm or cancel the emergency return window first.
boarding-teleport-emergency-return-no-session = Cannot open the emergency return window right now.

boarding-teleport-early-return-confirm-title = Early return
boarding-teleport-early-return-confirm-button = Return now
boarding-teleport-early-return-confirm-message = [color=#ffd27f]The return channel is still stabilizing.[/color] Jumping back early is dangerous. [bullet/] Time until stable: [color=#ffae63]{$remaining}s[/color] [bullet/] Risk of [color=#ff6868]total body failure[/color]: [color=#ff6868]{$risk}%[/color] The longer you wait, the safer the jump.
boarding-teleport-platform-early-return-started = Early return! Unstable jump in {$seconds}s.
boarding-teleport-platform-early-return-swelling = Your body distorts and swells — unstable matter is flooding back in!
boarding-teleport-platform-early-return-catastrophe = Bluespace shear on the platform! Your body fails to reintegrate!

boarding-teleport-window-shared-landing-on = Shared landing point: on
boarding-teleport-window-shared-landing-off = Per-platform landing

boarding-teleport-port-name = Teleport
boarding-teleport-port-description = Activates the linked boarding platform.

boarding-teleport-instructions = [head=2]Bluespace boarding[/head]

    Quick: console + engine + platforms + remotes on one grid → lock target in the console → jump from a platform → return with the same remote while the channel is open.

    Full guide and [bold]tier stat tables[/bold] — NT Guidebook: Frontier → [bold]Bluespace boarding[/bold] ([textlink="overview" link="BoardingTeleport"], [textlink="stats table" link="BoardingTeleportBalanceTable"]).

research-discipline-forge-boarding-teleport = Bluespace boarding
research-discipline-forge-boarding-teleport-advanced = Advanced bluespace boarding
research-discipline-forge-boarding-teleport-tier3 = Military bluespace boarding
research-discipline-forge-boarding-teleport-experimental = Experimental bluespace boarding
research-technology-forge-boarding-teleport-tier1 = Basic boarding kit
research-technology-forge-boarding-teleport-tier1-desc = Unlocks fabrication of tier-1 bluespace boarding components after super parts are available.
research-technology-forge-boarding-teleport-tier2 = Improved bluespace drive
research-technology-forge-boarding-teleport-tier3-base = Military boarding (in development)
research-technology-forge-boarding-teleport-tier4 = Phase-shift drive T4
research-technology-forge-boarding-teleport-tier4-desc = Experimental engine and platform flatpacks. Requires a tier-4 disk in the research server and completed tier-2 boarding research.

# --- Entity prototypes (EN display names; RU in ss14-ru/prototypes/_Forge/...) ---

ent-PaperBoardingTeleportInstructions = bluespace boarding instructions
    .desc = A quick setup sheet for a bluespace boarding kit.

ent-DisciplinesDiskBoardingTeleportTier4 = tier-4 bluespace boarding technology disk
    .desc = Encrypted drone blackbox data for phase-shift boarding drives. Insert into a research server to unlock the experimental discipline and tier-4 recipes.

ent-BoardingTeleportRemote = bluespace boarding remote
    .desc = A handheld trigger for a linked boarding platform.
ent-BoardingTeleportRemoteFlatpack = bluespace boarding remote flatpack
    .desc = A flatpack for assembling a boarding remote.

ent-BoardingTeleportConsoleCircuitboard = bluespace boarding console board
    .desc = A computer circuit board for a bluespace boarding console.
ent-BoardingBluespaceScramblerMachineCircuitboard = bluespace scrambler board
    .desc = A machine circuit board for a bluespace scrambler.
ent-BoardingTeleportEngineMachineCircuitboard = bluespace boarding engine board
    .desc = A machine circuit board for a bluespace boarding engine.
ent-BoardingTeleportPlatformMachineCircuitboard = bluespace boarding platform board
    .desc = A machine circuit board for a boarding platform.

ent-ComputerBoardingTeleport = bluespace boarding console
    .desc = Selects a hostile grid and landing point for linked boarding platforms.
ent-BoardingTeleportEngine = bluespace boarding engine
    .desc = Powers the console on the same grid. Sets acquisition range and lock tolerances.
ent-BoardingTeleportEngineAdvanced = advanced bluespace boarding engine
    .desc = Extended acquisition range and tier-2 shield penetration.
ent-BoardingTeleportEngineTier3 = military bluespace boarding engine
    .desc = A hardened drive that pierces tier-1 and tier-2 shields when establishing a landing lock.
ent-BoardingTeleportEngineExperimental = experimental bluespace boarding engine
    .desc = Phase-shift tuned drive with higher risk tolerance and faster lock recovery.
ent-BoardingBluespaceScrambler = bluespace scrambler
    .desc = Disrupts hostile boarding teleports targeting this grid. Blocks locks while powered.
ent-BoardingTeleportPlatform = bluespace boarding platform
    .desc = Sends one boarder to the console-selected landing tile.
ent-BoardingTeleportPlatformAdvanced = advanced boarding platform
    .desc = Faster recharge and a wider activation field.
ent-BoardingTeleportPlatformExperimental = experimental boarding platform
    .desc = Higher destabilization risk, but phase-shift scatter control improves landing spread.

ent-BoardingTeleportEngineFlatpack = bluespace boarding engine flatpack
    .desc = A flatpack for constructing a bluespace boarding engine.
ent-BoardingTeleportEngineAdvancedFlatpack = advanced boarding engine flatpack
    .desc = A flatpack for an advanced bluespace boarding engine.
ent-BoardingTeleportEngineTier3Flatpack = military boarding engine flatpack
    .desc = A tier-3 boarding engine flatpack. Company research required before fabrication.
ent-BoardingTeleportEngineExperimentalFlatpack = experimental boarding engine flatpack
    .desc = A flatpack for a tier-4 phase-shift boarding engine.
ent-BoardingBluespaceScramblerFlatpack = bluespace scrambler flatpack
    .desc = A flatpack for constructing a bluespace scrambler.
ent-BoardingTeleportConsoleFlatpack = bluespace boarding console flatpack
    .desc = A flatpack for constructing a boarding console.
ent-BoardingTeleportPlatformFlatpack = bluespace boarding platform flatpack
    .desc = A flatpack for constructing a boarding platform.
ent-BoardingTeleportPlatformAdvancedFlatpack = advanced boarding platform flatpack
    .desc = A flatpack for an advanced boarding platform.
ent-BoardingTeleportPlatformExperimentalFlatpack = experimental boarding platform flatpack
    .desc = A flatpack for a tier-4 experimental boarding platform.

ent-CrateBoardingTeleportKit = bluespace boarding kit crate
    .desc = Packed equipment for a four-platform bluespace boarding system.

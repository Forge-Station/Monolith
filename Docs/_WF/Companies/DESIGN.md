# Companies: design

Status: all five phases built. Licence upkeep (optional) is not built. Numbers marked *tunable* are server settings.

## Goals

- A persistent organisation above the per-round crew: membership, ranks, money and ships carry over between rounds.
- Closed money loop. Wages and payouts only move credits between the company and its members; the registry fee and
  optional licence upkeep destroy credits. Nothing mints money.
- Owners run their company from the game, not through admins.
- Replace the character creator's Mono company picker. Gameplay factions keep working unchanged.

## Terms

| Term | Meaning |
|---|---|
| Company | A player-run organisation stored in the database. |
| Member | One character (not account) in a company, with one rank. |
| Rank | A company-defined title with permissions, a wage, a base job and two icons. |
| Account | The company's credit balance. |
| Ledger | The log of every credit movement in or out of the account. |
| Licence | A permanent right to launch one copy of a ship design each round. |
| Hangar | The company page that lists licences and launches company ships. |
| Company crew | A round's crew (RoundSetup) flying under a company's name, IFF and logo. |
| System company | A Mono company prototype kept for gameplay factions. |

## Membership

- Membership is per character, keyed by the character's database profile id. A character belongs to at most one company.
- Deleting a character removes its membership. If it owned the company, ownership passes to the highest-ranked
  remaining member (longest-serving breaks ties); a company with no members is disbanded and its balance is lost.
- Joining: an invitation from a member with Invite, sent to an online player's current character and accepted on a
  non-modal card (as crew invitations work), or an application from the company directory that a member with Invite
  approves.
- Leaving is always allowed. Members with Kick can remove anyone of a lower rank.
- Ironman characters cannot join (they cannot draw on shared money elsewhere either).
- The company directory lists every company with logo, tag, name, description, member count and a Recruiting flag.

## Ranks and permissions

The owner sits above all ranks and holds every permission. New companies get Officer, Member and Recruit. Up to 12
ranks (*tunable*), ordered; a member can only act on ranks below their own.

| Permission | Allows |
|---|---|
| Invite | Send invitations, approve applications. |
| Kick | Remove members of lower rank. |
| SetRanks | Promote and demote members below their own rank. |
| EditRanks | Edit ranks below their own: name, wage, base job, permissions they hold themselves. |
| Withdraw | Take credits to their own character, up to the rank's daily cap. |
| FieldCrew | Run a company crew in RoundSetup and post company job postings. |
| Launch | Launch a company ship from the hangar. |
| Design | Edit the logo and rank icons. |
| EditDetails | Edit the description, colour and Recruiting flag. |

Owner only: transfer ownership, disband, rename, tag, the automatic cut, the launch fee, buying and selling licences.

Each rank also has:

- **Title**, shown as the job title on the ID card, station records, mind and wherever the job title shows in chat.
- **Base job**, a real job that decides access, equipment and loadouts (from the ship crew job list RoundSetup uses).
- **Wage** per hour played.
- **Withdraw cap** per real day, when the rank has Withdraw.
- **Job icon** (8x8, the HUD status icon) and **ID icon**, both drawn by the company. Defaults to the base job's icons.

## Account and ledger

- Any member can deposit from their character's bank (Company page, ATM or a PDA program).
- Withdrawals go to the withdrawing character's bank, within the rank's daily cap; the owner has no cap.
- Every movement writes a ledger row: time, kind, amount, balance after, acting character, other character, note.
  Kinds: Deposit, Withdraw, Wage, Cut, LicenceBuy, LicenceUpkeep, RegistryFee, LaunchFee, AdminAdjust, AdminRefund.
- Members with Withdraw or EditRanks see the full ledger; other members see their own rows.
- Applications are open to every company; Recruiting only highlights it in the list.
- Every movement also goes to the admin log.
- The balance never goes below zero; anything that would overdraw is refused.

## Payroll

- Time counts while the session controls a living body spawned from the member character (critical counts, ghost and
  dead do not) and the player is not AFK by the existing AFK check.
- Server samples once a minute, adds the seconds to the member's unpaid time, and pays one wage for every 3600 seconds.
  Unpaid seconds carry over between rounds.
- Wages go to the character's bank through the bank system.
- If the account cannot cover every wage due at a sample, each due member receives the same fraction of their wage, no
  debt accrues, and the owner gets a notice. A ledger row records the shortfall.

## Automatic cut

- The owner sets a cut of 0-50% (default 10%, *tunable*) of members' earnings, taken before the earnings reach the
  character's bank and paid into the account.
- Earnings means the bank deposits the bank system already treats as taxable income: cargo sales, bounties, encounter
  rewards and similar. Wages, withdrawals and transfers are never cut.
- Each cut writes its own ledger row; payouts are infrequent enough that batching isn't needed.
- Built coverage: cargo pallet sales (taken from the cash the console spawns), Wolfgate encounter kill bounties and
  rewards, and bounties and shipbreaker warrants. NF cargo bounty cash and other income are not cut yet.

## Identity

- **Character creator.** The Company section shows the character's company, rank and logo, with a button to open the
  Company window. The Mono picker is hidden, and the profile's Mono company no longer applies at spawn, so old
  values pointing at removed flavour companies do nothing.
- **Join as rank.** Late join and Crew Setup list "Join as <rank title>" for members. The member spawns as the rank's
  base job with the rank title applied (the crew title path RoundSetup already uses for ID, mind and records). If the
  company has a crew in the round, the member joins it; otherwise they spawn at the station and use its normal slot
  for the base job. Built: Join as rank is a button in the Company window; joining a company crew this way is not
  built yet, so it always uses a station.
- **ID card and examine** show the company name, tag and rank. The HUD job icon is the rank's job icon, and its
  tooltip names the company, so a forged icon is easy to check.
- **Radio.** One channel per company, live in every crew of that company and outside crews. Key `:4` (`:h` is the headset default),
  routed per company like the crew channel (`:k`).
- **IFF.** Company ships show the company tag and colour as their IFF label.

## Company crews

- A RoundSetup host with FieldCrew picks "Fly as <company>" when creating or setting up a crew.
- The crew row and details show the company tag and logo; postings read "<company> is hiring".
- Company members see their company's crews first in every crew list.
- Non-members can join as hired hands. The host can send them a company invitation afterwards.
- A company can run several crews in one round.

## Hangar and fleet licences

- A company holds up to 3 licences (*tunable*).
- Licence price: 200x the design's shipyard price, minimum 20,000,000 (*tunable*). Bought by the owner from the account.
  Selling a licence refunds nothing.
- Each licence can be launched once per round. All of a company's licences can be out at once.
- Launching (Launch permission) creates a company crew for that design. The purchase price is skipped.
- **Registry fee** (credits destroyed), paid from the account by how many company ships are already out this round:
  1st 5%, 2nd 15%, 3rd 30% of the shipyard price (*tunable*). The launch is refused if the account can't cover it.
- **Launch fee**, set by the owner (0 or more), paid by the launching member's character into the account.
- The ship carries the company's IFF and name. It can't be sold or have its deed transferred; crew access follows the
  company crew. Once destroyed, its licence stays used for the rest of the round.
- **Licence upkeep** (optional, off by default): 0.25% of the licence price per real day the company is active. Unpaid
  upkeep suspends the licence until it is paid; nothing is lost.

## Art

Reuses the CustomMarkings pipeline: pixel editor, PNG import with colour-space conversion, art stored once per hash in
the database, sent to clients on first use, registered as in-memory resources so existing sprite code can draw it.

- Logo: 32x32. Job icon: 8x8. ID icon: the size of the ID card job icon.
- Editing needs Design. Admins can wipe any company's art and lock its editing (as `CustomMarkingBlockCommand` does).

## System companies

Kept as Mono company prototypes (referenced by jobs, NPC crews, encounters, wars or code):
None, TSF, TSFCivilian, TSFHighComm, PDV, PDVCivilian, PDVHighComm, MD, Colonial, USSP, MMC, TheViperGroup,
RedSailCorsairs, paycheckbratva, blackhawkpmc, WFAds, WFAsf, WFJudges, WFAshfallReavers, WFUnderground.

Removed (selectable flavour, unreferenced): DrakeIndustries, SteelHammerManufacturing, HarmonyMedicalEnterprises,
MidnightArmsCo, DarkMatterEnterprises, AetherionDynamics, HorizonEnergy, UniversalStatesOfAmerica, CivilDefenseMilita,
UllmanIndustries, NosskeEienResearchandDevelopment, TheHive, CerberusSurplusProvisions, PLXCOR.
(`ShipyardConsoleUiKey.DrakeIndustries` is a shipyard console key, not the company, and stays.)

The Mono `CompanyMembers` whitelist table stays in the schema but is no longer read.

## Admin tools

`companylist`, `companyinfo`, `companyledger`, `companyfreeze` (blocks all money movement), `companyadjust` and `companyrefund`
(ledger-linked corrections), `companyrename`, `companytransfer`, `companydisband`, `companyart wipe|lock`. All logged.

## Storage

New tables (Wolfgate migrations, SQLite and Postgres):

Tables are named `WolfgateCompany*` in the model.

- `wf_company`: id, name (unique), tag (2-5 chars, unique), description, colour, owner profile, balance, cut, launch
  fee, recruiting, frozen, art locked, logo hash, created.
- `wf_company_rank`: id, company, order, title, permissions, wage, withdraw cap, base job, job icon hash, ID icon hash.
- `wf_company_member`: company, profile (cascade on delete), rank, joined, unpaid seconds, withdrawn today.
- `wf_company_ledger`: id, company, time, kind, amount, balance after, actor profile, other profile, note.
- `wf_company_licence`: id, company, vessel, price paid, bought, suspended, upkeep paid until.
- `wf_company_art`: hash, kind, PNG bytes.

Invitations, applications and per-round launch state live in memory.

## Risks to verify while building

- Drawing runtime art as the HUD job icon: `IdCardComponent.JobIcon` is a prototype id, so the client needs a hook in
  the status icon event to swap in the rank icon.
- IFF labels read `CompanyComponent.CompanyName` as a prototype id; company ships need a marked hook to show the
  player company instead.
- The bank system's taxable deposit path must cover every earnings source the cut should see.
- Mapping a session's spawned character to its profile id for payroll.
- Ships, deeds and access when a company ship has no personal owner.

## Build phases

1. **Core** (built). Tables, companies, members, ranks and permissions, account, ledger, directory, invitations and
   applications, the Company window (from Crew Setup and the character creator), admin commands. Character creator
   change, flavour companies removed.
2. **Money** (built). Payroll and the automatic cut.
3. **Identity** (built). Join as rank, rank titles on ID and records, company radio, ID and examine, IFF, company crews and
   postings.
4. **Art** (built). Logo and rank job icon editors with PNG import. One 8x8 icon per rank serves as both the HUD job
   icon and the ID's icon, since the HUD reads the job icon from the ID. Art is stored as raw RGBA on the company and
   rank rows rather than a hashed art table.
5. **Hangar** (built). Licences, launching through Crew Setup, registry and launch fees. Upkeep is not built.
   Licences are offered only for designs with no job access requirement, so any launching member can fly them.

Each phase ships on its own PR with integration tests under `Content.IntegrationTests/Tests/_WF/Companies`.

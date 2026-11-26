# SafetyDepositBox

Safety deposit boxes ported from Wolfgate. The console sells small (2x2), medium
(2x4) and large (2x6) boxes for 2,000,000, 3,500,000 and 5,000,000 credits.
Insert a box into the console to save its contents between rounds, withdraw it
to retrieve the items, or reclaim a lost box as a new empty one. Ownership is
bound to the player's account and character slot.

The port uses Monolith's existing database methods and `BankSystem.TryBankWithdraw`
for payment and balance updates. Wolfgate's ship service tariffs and disabled
MonoCoins savings are not used here. Saved item cooldowns are cleared on withdrawal;
items that no longer fit the box are returned beside it.

Entry points:

- `SafetyDepositBoxSystem`: purchase, deposit, withdrawal and reclaim.
- `Content.Shared/_WF/SafetyDepositBox`: components, UI messages and examination text.
- `Content.Client/_WF/SafetyDepositBox`: console and purchase confirmation windows.
- `Resources/Prototypes/_WF/SafetyDepositBox`: console, three box sizes and storage restrictions.
- `Resources/Locale/en-US/_WF/SafetyDepositBox`: interface and popup text.
- `Resources/Textures/_WF/SafetyDepositBox`: box and console sprites with original attribution.

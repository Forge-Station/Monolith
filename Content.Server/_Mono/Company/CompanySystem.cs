using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Timing;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

/// <summary>
/// This system handles assigning a company to players when they join.
/// TODO: remove hardcoded slop.
/// whoever hardcoded ts is getting slimed out no joke.
/// </summary>
public sealed partial class CompanySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedIdCardSystem _idCardSystem = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private CompanyManager _manager = default!;


    // Dictionary to store original company preferences for players
    private readonly Dictionary<string, string> _playerOriginalCompanies = new();

    public override void Initialize()
    {
        base.Initialize();

        // Subscribe to player spawn event to add the company component
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);

        // Subscribe to player detached event to clean up stored preferences
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        // Clean up stored preferences when player disconnects
        _playerOriginalCompanies.Remove(args.Player.UserId.ToString());
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        // Add the company component with the player's saved company
        var companyComp = EnsureComp<CompanyComponent>(args.Mob);

        var playerId = args.Player.UserId.ToString();
        var profileCompany = args.Profile.Company;

        // Store the player's original company preference if not already stored
        if (!_playerOriginalCompanies.ContainsKey(playerId))
        {
            _playerOriginalCompanies[playerId] = profileCompany;
        }

        if (args.JobId != null && _prototypeManager.TryIndex<JobPrototype>(args.JobId, out var job))
        {
            companyComp.CompanyName = FactionCompanyResolver.ResolveSpawnCompany(job, profileCompany);
        }
        else
        {
            companyComp.CompanyName = FactionCompanyResolver.IsFactionCompany(profileCompany)
                ? "None"
                : string.IsNullOrEmpty(profileCompany) ? "None" : profileCompany;
        }

        // Forge-change-start
        if (_prototypeManager.TryIndex<CompanyPrototype>(companyComp.CompanyName, out var proto))
        {
            foreach (var special in proto.Special)
            {
                special.AfterEquip(args.Mob);
            }
        }
        // Forge-change-end

        // Ensure the component is networked to clients
        Dirty(args.Mob, companyComp);

        // Update the player's ID card with the company information
        var companyName = companyComp.CompanyName.ToString();
        if (!UpdateIdCardCompany(args.Mob, companyName))
        {
            // Loadout gear may finish equipping after this event; retry next tick.
            Timer.Spawn(TimeSpan.Zero, () => UpdateIdCardCompany(args.Mob, companyName));
        }
    }

    /// <summary>
    /// Updates the player's ID card with their company information
    /// </summary>
    private bool UpdateIdCardCompany(EntityUid playerEntity, string companyName)
    {
        // Try to get the player's ID card
        if (!_inventorySystem.TryGetSlotEntity(playerEntity, "id", out var idUid))
            return false;

        var cardId = idUid.Value;

        // Check if it's a PDA with an ID card inside
        if (TryComp<PdaComponent>(idUid, out var pdaComponent) && pdaComponent.ContainedId != null)
            cardId = pdaComponent.ContainedId.Value;

        // Update the ID card with company information
        if (!TryComp<IdCardComponent>(cardId, out var idCard))
            return false;

        return _idCardSystem.TryChangeCompanyName(cardId, companyName, idCard);
    }
}

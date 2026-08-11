using Content.Server.Preferences.Managers;
using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

/// <summary>
/// This system handles assigning a company to players when they join.
/// </summary>
public sealed partial class CompanySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedIdCardSystem _idCardSystem = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private CompanyManager _manager = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;

    private readonly Dictionary<string, string> _playerOriginalCompanies = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        _playerOriginalCompanies.Remove(args.Player.UserId.ToString());
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var companyComp = EnsureComp<CompanyComponent>(args.Mob);
        var playerId = args.Player.UserId.ToString();
        var profileCompany = args.Profile.Company;
        var slot = ResolveCharacterSlot(args.Player.UserId, args.Profile);

        if (!_playerOriginalCompanies.ContainsKey(playerId))
            _playerOriginalCompanies[playerId] = profileCompany;

        var membership = _manager.GetMemberForCharacter(args.Player.UserId, slot);
        var membershipCompany = membership?.Company.Id;
        JobPrototype? spawnJob = null;
        if (args.JobId != null)
            _prototypeManager.TryIndex(args.JobId, out spawnJob);

        var jobForcesCompany = spawnJob != null && FactionCompanyResolver.JobForcesCompany(spawnJob);

        // Faction jobs still force their company; otherwise membership wins and is synced to the doll/profile.
        if (jobForcesCompany)
        {
            companyComp.CompanyName = spawnJob!.AssignedCompany;
        }
        else if (!string.IsNullOrEmpty(membershipCompany) && membershipCompany != "None")
        {
            companyComp.CompanyName = membershipCompany;
            SyncProfileCompany(args.Player.UserId, slot, args.Profile, membershipCompany);
        }
        else if (spawnJob != null)
        {
            companyComp.CompanyName = FactionCompanyResolver.ResolveSpawnCompany(spawnJob, profileCompany);
        }
        else
        {
            companyComp.CompanyName = FactionCompanyResolver.IsFactionCompany(profileCompany)
                ? "None"
                : string.IsNullOrEmpty(profileCompany) ? "None" : profileCompany;
        }

        if (_prototypeManager.TryIndex<CompanyPrototype>(companyComp.CompanyName, out var proto))
        {
            foreach (var special in proto.Special)
                special.AfterEquip(args.Mob);
        }

        Dirty(args.Mob, companyComp);

        var companyName = companyComp.CompanyName.ToString();
        if (!UpdateIdCardCompany(args.Mob, companyName))
        {
            Timer.Spawn(TimeSpan.Zero, () =>
            {
                UpdateIdCardCompany(args.Mob, companyName);
                ApplyCompanyAccessTags(args.Mob, companyComp.CompanyName, args.Player);
            });
        }
        else
        {
            ApplyCompanyAccessTags(args.Mob, companyComp.CompanyName, args.Player);
        }
    }

    /// <summary>
    /// If the player's selected character has a company membership, apply it to their attached mob.
    /// </summary>
    public void TryApplyMembershipToSession(ICommonSession session)
    {
        if (session.AttachedEntity is not { } mob)
            return;

        if (!_manager.TryGetCharacterContext(session.UserId, out var slot, out _))
            return;

        var membership = _manager.GetMemberForCharacter(session.UserId, slot);
        if (membership == null || membership.Value.Company == "None")
            return;

        ApplyCompanyToMob(mob, membership.Value.Company, session);
    }

    /// <summary>
    /// Applies company affiliation to a living mob (e.g. after accepting an invite).
    /// </summary>
    public void ApplyCompanyToMob(EntityUid mob, ProtoId<CompanyPrototype> company, ICommonSession session)
    {
        var companyComp = EnsureComp<CompanyComponent>(mob);
        companyComp.CompanyName = company;
        Dirty(mob, companyComp);

        if (_prefs.TryGetCachedPreferences(session.UserId, out var prefs) &&
            prefs.SelectedCharacter is HumanoidCharacterProfile profile)
        {
            SyncProfileCompany(session.UserId, prefs.SelectedCharacterIndex, profile, company);
        }

        if (_prototypeManager.TryIndex(company, out var proto))
        {
            foreach (var special in proto.Special)
                special.AfterEquip(mob);
        }

        var companyName = company.Id;
        if (!UpdateIdCardCompany(mob, companyName))
        {
            Timer.Spawn(TimeSpan.Zero, () =>
            {
                UpdateIdCardCompany(mob, companyName);
                ApplyCompanyAccessTags(mob, company, session);
            });
        }
        else
        {
            ApplyCompanyAccessTags(mob, company, session);
        }
    }

    private void SyncProfileCompany(NetUserId userId, int slot, HumanoidCharacterProfile profile, string company)
    {
        if (profile.Company == company)
            return;

        _ = _prefs.SetProfile(userId, slot, profile.WithCompany(company));
    }

    private int ResolveCharacterSlot(NetUserId userId, HumanoidCharacterProfile profile)
    {
        if (!_prefs.TryGetCachedPreferences(userId, out var prefs))
            return 0;

        foreach (var (slot, character) in prefs.Characters)
        {
            if (character is HumanoidCharacterProfile humanoid &&
                humanoid.Name == profile.Name)
                return slot;
        }

        return prefs.SelectedCharacterIndex;
    }

    private bool UpdateIdCardCompany(EntityUid playerEntity, string companyName)
    {
        if (!_inventorySystem.TryGetSlotEntity(playerEntity, "id", out var idUid))
            return false;

        var cardId = idUid.Value;
        if (TryComp<PdaComponent>(idUid, out var pdaComponent) && pdaComponent.ContainedId != null)
            cardId = pdaComponent.ContainedId.Value;

        if (!TryComp<IdCardComponent>(cardId, out var idCard))
            return false;

        return _idCardSystem.TryChangeCompanyName(cardId, companyName, idCard);
    }
}

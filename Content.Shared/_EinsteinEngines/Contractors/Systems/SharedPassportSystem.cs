using Content.Shared._EE.Contractors.Components;
using Content.Shared._EE.Contractors.Prototypes;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Preferences;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Content.Shared.GameTicking;
using Content.Shared.UserInterface;
using Content.Shared._Forge.Contractors;

namespace Content.Shared._EE.Contractors.Systems;

public sealed class SharedPassportSystem : EntitySystem
{
    public const int CurrentYear = 3026;
    const string PIDChars = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedTransformSystem _sharedTransformSystem = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PassportComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        // Forge-change-start
        SubscribeLocalEvent<PassportComponent, BeforeActivatableUIOpenEvent>(OnBeforeUiOpen);
        SubscribeLocalEvent<PassportComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<PassportComponent, BoundUIClosedEvent>(OnUiClosed);
        // Forge-change-end
    }

    private void OnExamined(EntityUid uid, PassportComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange
            || component.IsClosed // Forge-Change
            || component.OwnerProfile == null)
            return;

        var species = _prototypeManager.Index<SpeciesPrototype>(component.OwnerProfile.Species);

        args.PushMarkup(Loc.GetString("passport-registered-to", ("name", component.OwnerProfile.Name)), 50);
        args.PushMarkup(Loc.GetString("passport-species", ("species", Loc.GetString(species.Name))), 49);
        args.PushMarkup(Loc.GetString("passport-gender", ("gender", component.OwnerProfile.Gender.ToString())), 48);
        args.PushMarkup(Loc.GetString("passport-height", ("height", MathF.Round(component.OwnerProfile.Appearance.Height * species.AverageHeight))), 47);
        args.PushMarkup(Loc.GetString("passport-year-of-birth", ("year", CurrentYear - component.OwnerProfile.Age)), 47);

        args.PushMarkup(
            Loc.GetString("passport-pid", ("pid", GetPassportId(component.OwnerProfile))),
            46);
    }

    // Forge-change-start
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!ShouldSpawnPassports)
            return;

        var profile = ev.Profile;

        if (ev.JobId != null && _prototypeManager.TryIndex<JobPrototype>(ev.JobId, out var job) && job.AssignedNationality != null)
        {
            profile.Nationality = job.AssignedNationality;
        }

        SpawnPassportForPlayer(ev.Mob, profile, ev.JobId);
    }
    // Forge-change-end

    public void SpawnPassportForPlayer(EntityUid mob, HumanoidCharacterProfile profile, string? jobId)
    {
        if (jobId == null
            || !_prototypeManager.HasIndex<JobPrototype>(jobId)
            || Deleted(mob)
            || !Exists(mob)
            || !ShouldSpawnPassports)
            return;

        var coords = _sharedTransformSystem.GetMapCoordinates(mob);
        var passportEntity = TryCreatePassport(profile, coords);
        if (passportEntity == null)
            return;

        RaiseLocalEvent(new PassportIssuedEvent(mob, profile, jobId, GetPassportId(profile))); // Forge-Change

        bool passportStored = false;

        // Try to find wallet
        if (_inventory.TryGetSlotEntity(mob, "wallet", out var wallet) &&
            _entityManager.TryGetComponent<StorageComponent>(wallet, out var walletStorage))
        // Try inserting the entity into the wallet
        {
            if (_entityManager.TryGetComponent<ItemComponent>(passportEntity.Value, out var itemComp) &&
                _storage.CanInsert(wallet.Value, passportEntity.Value, out _, walletStorage, itemComp) &&
                _storage.Insert(wallet.Value, passportEntity.Value, out _, playSound: false))
            {
                passportStored = true;
            }
        }

        // Try to find back-mounted storage apparatus
        if (passportStored != true && _inventory.TryGetSlotEntity(mob, "back", out var item) && _entityManager.TryGetComponent<StorageComponent>(item, out var inventory))
        // Try inserting the entity into the storage, if it can't, it leaves the loadout item on the ground
        {
            if (!_entityManager.TryGetComponent<ItemComponent>(passportEntity.Value, out var itemComp)
                || !_storage.CanInsert(item.Value, passportEntity.Value, out _, inventory, itemComp)
                || !_storage.Insert(item.Value, passportEntity.Value, out _, playSound: false))
            {
                _adminLogManager.Add(
                    LogType.EntitySpawn,
                    LogImpact.Low,
                    $"Passport for {profile.Name} was spawned on the floor due to missing bag space");
            }
        }
    }

    private bool ShouldSpawnPassports =>
        _configManager.GetCVar<bool>("contractors.enabled");

    // Forge-Change
    public EntityUid? TryCreatePassport(HumanoidCharacterProfile profile, MapCoordinates coordinates)
    {
        if (!_prototypeManager.TryIndex(profile.Nationality, out NationalityPrototype? nationality) ||
            !_prototypeManager.TryIndex(nationality.PassportPrototype, out EntityPrototype? prototype))
            return null;

        var passport = _entityManager.SpawnEntity(prototype.ID, coordinates);
        var passportComp = _entityManager.GetComponent<PassportComponent>(passport);
        UpdatePassportProfile(new Entity<PassportComponent>(passport, passportComp), profile);
        return passport;
    }

    public void UpdatePassportProfile(Entity<PassportComponent> passport, HumanoidCharacterProfile profile)
    {
        passport.Comp.OwnerProfile = profile;
        Dirty(passport);
        var evt = new PassportProfileUpdatedEvent(profile);
        RaiseLocalEvent(passport, ref evt);

        // Forge-change-start
        if (_uiSystem.IsUiOpen(passport.Owner, PassportUiKey.Key))
            UpdateUserInterface(passport);
        // Forge-change-end
    }

    // Forge-change-start
    private void OnBeforeUiOpen(Entity<PassportComponent> passport, ref BeforeActivatableUIOpenEvent args)
    {
        UpdateUserInterface(passport);
    }

    private void OnUiOpened(Entity<PassportComponent> passport, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not PassportUiKey)
            return;

        SetClosed(passport, false);
    }

    private void OnUiClosed(Entity<PassportComponent> passport, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is not PassportUiKey)
            return;

        if (_uiSystem.IsUiOpen(passport.Owner, PassportUiKey.Key))
            return;

        SetClosed(passport, true);
    }

    private void UpdateUserInterface(Entity<PassportComponent> passport)
    {
        var pid = passport.Comp.OwnerProfile != null
            ? GetPassportId(passport.Comp.OwnerProfile)
            : string.Empty;

        _uiSystem.SetUiState(passport.Owner, PassportUiKey.Key,
            new PassportBoundUserInterfaceState(passport.Comp.OwnerProfile, pid));
    }

    private void SetClosed(Entity<PassportComponent> passport, bool closed)
    {
        if (passport.Comp.IsClosed == closed)
            return;

        passport.Comp.IsClosed = closed;
        Dirty(passport);

        var passportEvent = new PassportToggleEvent();
        RaiseLocalEvent(passport, ref passportEvent);
    }
    // Forge-change-end

    public static string GetPassportId(HumanoidCharacterProfile profile)
    {
        return GenerateIdentityString(profile.Name
            + profile.Appearance.Height
            + profile.Age
            + profile.Appearance.Height
            + profile.FlavorText);
    }

    private static string GenerateIdentityString(string seed)
    {
        var hashCode = seed.GetHashCode();
        System.Random random = new System.Random(hashCode);

        char[] result = new char[17]; // 15 characters + 2 dashes

        int j = 0;
        for (int i = 0; i < 15; i++)
        {
            if (i == 5 || i == 10)
            {
                result[j++] = '-';
            }
            result[j++] = PIDChars[random.Next(PIDChars.Length)];
        }

        return new string(result);
    }

    [ByRefEvent]
    public sealed class PassportToggleEvent : HandledEntityEventArgs
    {

    }

    [ByRefEvent]
    public sealed class PassportProfileUpdatedEvent(HumanoidCharacterProfile profile) : HandledEntityEventArgs
    {
        public HumanoidCharacterProfile Profile { get; } = profile;
    }
}

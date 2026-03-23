using Content.Shared._EE.Contractors.Components;
using Content.Shared._EE.Contractors.Prototypes;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Preferences;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared;
using Content.Shared.CCVar;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.GameTicking;

namespace Content.Shared._EE.Contractors.Systems;

public class SharedPassportSystem : EntitySystem
{
    public const int CurrentYear = 3026;
    const string PIDChars = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedTransformSystem _sharedTransformSystem = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PassportComponent, UseInHandEvent>(OnUseInHand);
        // SubscribeLocalEvent<PlayerLoadoutAppliedEvent>(OnPlayerLoadoutApplied);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PassportComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, PassportComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange
            || component.IsClosed
            || component.OwnerProfile == null)
            return;

        var species = _prototypeManager.Index<SpeciesPrototype>(component.OwnerProfile.Species);

        args.PushMarkup(Loc.GetString("passport-registered-to", ("name", component.OwnerProfile.Name)), 50);
        args.PushMarkup(Loc.GetString("passport-species", ("species", Loc.GetString(species.Name))), 49);
        args.PushMarkup(Loc.GetString("passport-gender", ("gender", component.OwnerProfile.Gender.ToString())), 48);
        args.PushMarkup(Loc.GetString("passport-height", ("height", MathF.Round(component.OwnerProfile.Appearance.Height * species.AverageHeight))), 47);
        args.PushMarkup(Loc.GetString("passport-year-of-birth", ("year", CurrentYear - component.OwnerProfile.Age)), 47);

        args.PushMarkup(
            Loc.GetString("passport-pid", ("pid", GenerateIdentityString(component.OwnerProfile.Name
            + component.OwnerProfile.Appearance.Height
            + component.OwnerProfile.Age
            + component.OwnerProfile.Appearance.Height
            + component.OwnerProfile.FlavorText))),
            46);
    }

    // Forge-change-start: i know, that shit. But, in my defense - im using _Mono/Company code as a reference.
    // private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev) =>
    //     SpawnPassportForPlayer(ev.Mob, ev.Profile, ev.JobId);
//    private readonly HashSet<string> _imperialJobs = new() // Forge-change-delete
//    {
//        "Praefect",
//        "Arbiter",
//        "Cardinal",
//        "Inquisitor",
//        "Consul",
//        "Praetorian",
//        "Auxilia",
//        "Neophyte",
//    };

    private readonly HashSet<string> _tsfJobs = new()
    {
        "Sheriff",
        "Bailiff",
        "SeniorOfficer", // Sergeant
        "Deputy",
        "Brigmedic",
        "NFDetective",
        "PublicAffairsLiaison",
        "Cadet",
        "TsfEngineer",
        "TsfBorg",
    };
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!ShouldSpawnPassports)
            return;

        var profile = ev.Profile;

        if (ev.JobId != null)
        {
//            if (_imperialJobs.Contains(ev.JobId))
//            {
//                profile.Nationality = "Imperial";
//            }
            if (_tsfJobs.Contains(ev.JobId))
            {
                profile.Nationality = "TransSolarFederation";
            }
        }
        SpawnPassportForPlayer(ev.Mob, profile, ev.JobId);
    }
    // Forge-change-end
    public void SpawnPassportForPlayer(EntityUid mob, HumanoidCharacterProfile profile, string? jobId)
    {
        if (jobId == null || !_prototypeManager.TryIndex(
                jobId,
                out JobPrototype? jobPrototype)
            // || !jobPrototype.CanHavePassport
            || Deleted(mob)
            || !Exists(mob)
            || !ShouldSpawnPassports)
            return;

        if (!_prototypeManager.TryIndex(
            profile.Nationality,
            out NationalityPrototype? nationalityPrototype) || !_prototypeManager.TryIndex(nationalityPrototype.PassportPrototype, out EntityPrototype? entityPrototype))
            return;

        var passportEntity = _entityManager.SpawnEntity(entityPrototype.ID, _sharedTransformSystem.GetMapCoordinates(mob));
        var passportComponent = _entityManager.GetComponent<PassportComponent>(passportEntity);

        UpdatePassportProfile(new(passportEntity, passportComponent), profile);

        // Try to find back-mounted storage apparatus
        if (_inventory.TryGetSlotEntity(mob, "back", out var item) &&
                EntityManager.TryGetComponent<StorageComponent>(item, out var inventory))
        // Try inserting the entity into the storage, if it can't, it leaves the loadout item on the ground
        {
            if (!EntityManager.TryGetComponent<ItemComponent>(passportEntity, out var itemComp)
                || !_storage.CanInsert(item.Value, passportEntity, out _, inventory, itemComp)
                || !_storage.Insert(item.Value, passportEntity, out _, playSound: false))
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

    public void UpdatePassportProfile(Entity<PassportComponent> passport, HumanoidCharacterProfile profile)
    {
        passport.Comp.OwnerProfile = profile;
        var evt = new PassportProfileUpdatedEvent(profile);
        RaiseLocalEvent(passport, ref evt);
    }

    private void OnUseInHand(Entity<PassportComponent> passport, ref UseInHandEvent evt)
    {
        if (evt.Handled || !_timing.IsFirstTimePredicted)
            return;

        evt.Handled = true;
        passport.Comp.IsClosed = !passport.Comp.IsClosed;

        var passportEvent = new PassportToggleEvent();
        RaiseLocalEvent(passport, ref passportEvent);
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
    public sealed class PassportToggleEvent : HandledEntityEventArgs {}

    [ByRefEvent]
    public sealed class PassportProfileUpdatedEvent(HumanoidCharacterProfile profile) : HandledEntityEventArgs
    {
        public HumanoidCharacterProfile Profile { get; } = profile;
    }
}

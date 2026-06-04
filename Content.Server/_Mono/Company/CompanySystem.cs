using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Content.Shared.Access; // Forge-Change: Tel-Adi PDA swap
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems; // Forge-Change: hand the Tel-Adi key over if no headset slot
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Radio.Components; // Forge-Change: Tel-Adi headset encryption key
using Content.Shared.Storage; // Forge-Change: deliver Tel-Adi key into the backpack
using Content.Shared.Storage.EntitySystems; // Forge-Change
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.StatusIcon; // Forge-Change: copy job icon onto Tel-Adi card
using Robust.Shared.Containers; // Forge-Change
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
    [Dependency] private SharedHandsSystem _hands = default!; // Forge-Change
    [Dependency] private SharedStorageSystem _storage = default!; // Forge-Change: put Tel-Adi key in backpack


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
        EntityUid? pdaUid = null;

        // Check if it's a PDA with an ID card inside
        if (TryComp<PdaComponent>(idUid, out var pdaComponent) && pdaComponent.ContainedId != null)
        {
            pdaUid = idUid.Value;
            cardId = pdaComponent.ContainedId.Value;
        }

        // Update the ID card with company information
        if (!TryComp<IdCardComponent>(cardId, out var idCard))
            return false;

        _idCardSystem.TryChangeCompanyName(cardId, companyName, idCard);

        // Forge-Change: Tel-Adi employees get a branded PDA + ID card + corp radio key.
        if (companyName == "TelAdi")
        {
            GiveTelAdiEncryptionKey(playerEntity);

            // Already swapped (e.g. on a retry tick) — nothing to do.
            if (pdaUid != null && MetaData(pdaUid.Value).EntityPrototype?.ID == "TelAdiPDA")
                return true;

            if (pdaUid != null)
                ReplacePdaWithTelAdi(playerEntity, pdaUid.Value, cardId);
            else
                SpawnTelAdiPdaWithCard(playerEntity, cardId);
        }

        return true;
    }

    /// <summary>
    /// Slots a Tel-Adi encryption key into the player's headset (granting the corp radio channel). If the
    /// headset has no free key slot, the key is handed to the player instead. Skips if already present.
    /// </summary>
    private void GiveTelAdiEncryptionKey(EntityUid playerEntity)
    {
        // Don't hand out a second key on a retry tick.
        if (HasTelAdiKey(playerEntity))
            return;

        var key = Spawn("EncryptionKeyTelAdi", Transform(playerEntity).Coordinates);

        // Put it into the employee's backpack so it always lands somewhere (they slot it
        // into their headset themselves to get the :g corp channel).
        if (_inventorySystem.TryGetSlotEntity(playerEntity, "back", out var bag)
            && TryComp<StorageComponent>(bag, out var storage)
            && _storage.Insert(bag.Value, key, out _, null, storage, false, false))
            return;

        // No backpack / no room → hand it over (or drop at feet).
        _hands.PickupOrDrop(playerEntity, key);
    }

    /// <summary>
    /// True if the player already carries a Tel-Adi encryption key (headset key slots or backpack).
    /// Used to avoid handing out duplicates across retry ticks.
    /// </summary>
    private bool HasTelAdiKey(EntityUid playerEntity)
    {
        if (_inventorySystem.TryGetSlotEntity(playerEntity, "ears", out var headset)
            && TryComp<EncryptionKeyHolderComponent>(headset, out var holder))
        {
            foreach (var k in holder.KeyContainer.ContainedEntities)
                if (MetaData(k).EntityPrototype?.ID == "EncryptionKeyTelAdi")
                    return true;
        }

        if (_inventorySystem.TryGetSlotEntity(playerEntity, "back", out var bag)
            && TryComp<StorageComponent>(bag, out var storage))
        {
            foreach (var k in storage.Container.ContainedEntities)
                if (MetaData(k).EntityPrototype?.ID == "EncryptionKeyTelAdi")
                    return true;
        }

        return false;
    }

    /// <summary>
    /// Replaces the player's existing PDA with TelAdiPDA, applying name/job/access to the card inside it.
    /// </summary>
    private void ReplacePdaWithTelAdi(EntityUid playerEntity, EntityUid oldPdaUid, EntityUid oldCardId)
    {
        string? fullName = null;
        string? jobTitle = null;
        ProtoId<JobIconPrototype> jobIcon = "JobIconUnknown";

        if (TryComp<IdCardComponent>(oldCardId, out var oldCard))
        {
            fullName = oldCard.FullName;
            jobTitle = oldCard.LocalizedJobTitle;
            jobIcon = oldCard.JobIcon;
        }

        HashSet<ProtoId<AccessLevelPrototype>> oldTags = new();
        if (TryComp<AccessComponent>(oldCardId, out var oldAccess))
            oldTags = new HashSet<ProtoId<AccessLevelPrototype>>(oldAccess.Tags);

        _inventorySystem.TryUnequip(playerEntity, "id", force: true);
        QueueDel(oldCardId);
        QueueDel(oldPdaUid);

        var newPda = Spawn("TelAdiPDA", Transform(playerEntity).Coordinates);

        if (TryComp<PdaComponent>(newPda, out var newPdaComp) && newPdaComp.ContainedId != null)
            ApplyCardData(newPdaComp.ContainedId.Value, fullName, jobTitle, jobIcon, oldTags);

        _inventorySystem.TryEquip(playerEntity, newPda, "id", force: true);
    }

    /// <summary>
    /// Spawns a fresh TelAdiPDA when the player had a bare card (no PDA), moving over name/job/access.
    /// </summary>
    private void SpawnTelAdiPdaWithCard(EntityUid playerEntity, EntityUid oldCardId)
    {
        string? fullName = null;
        string? jobTitle = null;
        ProtoId<JobIconPrototype> jobIcon = "JobIconUnknown";

        if (TryComp<IdCardComponent>(oldCardId, out var oldCard))
        {
            fullName = oldCard.FullName;
            jobTitle = oldCard.LocalizedJobTitle;
            jobIcon = oldCard.JobIcon;
        }

        HashSet<ProtoId<AccessLevelPrototype>> oldTags = new();
        if (TryComp<AccessComponent>(oldCardId, out var oldAccess))
            oldTags = new HashSet<ProtoId<AccessLevelPrototype>>(oldAccess.Tags);

        _inventorySystem.TryUnequip(playerEntity, "id", force: true);
        QueueDel(oldCardId);

        var newPda = Spawn("TelAdiPDA", Transform(playerEntity).Coordinates);

        if (TryComp<PdaComponent>(newPda, out var newPdaComp) && newPdaComp.ContainedId != null)
            ApplyCardData(newPdaComp.ContainedId.Value, fullName, jobTitle, jobIcon, oldTags);

        _inventorySystem.TryEquip(playerEntity, newPda, "id", force: true);
    }

    /// <summary>
    /// Applies name, job title, job icon, company name, and access tags (+ TelAdi) to a card entity.
    /// </summary>
    private void ApplyCardData(
        EntityUid cardUid,
        string? fullName,
        string? jobTitle,
        ProtoId<JobIconPrototype> jobIcon,
        HashSet<ProtoId<AccessLevelPrototype>> extraTags)
    {
        if (TryComp<IdCardComponent>(cardUid, out var idCard))
        {
            _idCardSystem.TryChangeFullName(cardUid, fullName, idCard);
            _idCardSystem.TryChangeJobTitle(cardUid, jobTitle, idCard);
            _idCardSystem.TryChangeCompanyName(cardUid, "TelAdi", idCard);

            // Carry over the job icon so the SecHUD shows the role, not a grey square.
            if (_prototypeManager.TryIndex(jobIcon, out var jobIconProto))
                _idCardSystem.TryChangeJobIcon(cardUid, jobIconProto, idCard);
        }

        if (TryComp<AccessComponent>(cardUid, out var access))
        {
            foreach (var tag in extraTags)
                access.Tags.Add(tag);
            access.Tags.Add("TelAdi");
            Dirty(cardUid, access);
        }
    }
}

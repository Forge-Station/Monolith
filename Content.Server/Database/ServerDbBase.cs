using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Mono.Company;
using Content.Server._WF.Voice; // WOLFGATE(Voice)
using Content.Server._WF.Prototypes; // WOLFGATE(Prototypes)
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._Common.Consent; // WOLFGATE(Genitals)
using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Augments; // WOLFGATE(Augments)
using Content.Shared._WF.Biotype; // WOLFGATE(Biotype)
using Content.Shared._WF.BloodColour; // WOLFGATE(BloodColour)
using Content.Shared._WF.ChatFont; // WOLFGATE(ChatFont)
using Content.Shared._WF.TypingBubble; // WOLFGATE(TypingBubble)
using Content.Shared._WF.EmoteVoices; // WOLFGATE(EmoteVoices)
using Content.Shared._WF.CustomMarkings; // WOLFGATE(CustomMarkings)
using Content.Shared._WF.LegStyle; // WOLFGATE(LegStyle)
using Content.Shared._WF.Prototypes; // WOLFGATE(Prototypes)
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Ghost.Roles;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Database
{
    // WOLFGATE(CustomMarkings): partial, so the module's queries sit in its own folder
    public abstract partial class ServerDbBase
    {
        private readonly ISawmill _opsLog;

        public event Action<DatabaseNotification>? OnNotificationReceived;

        /// <param name="opsLog">Sawmill to trace log database operations to.</param>
        public ServerDbBase(ISawmill opsLog)
        {
            _opsLog = opsLog;
        }

        #region Preferences
        public async Task<PlayerPreferences?> GetPlayerPreferencesAsync(
            NetUserId userId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var prefs = await db.DbContext
                .Preference
                .Include(p => p.Profiles).ThenInclude(h => h.Jobs)
                .Include(p => p.Profiles).ThenInclude(h => h.Antags)
                .Include(p => p.Profiles).ThenInclude(h => h.Traits)
                .Include(p => p.Profiles).ThenInclude(h => h.Components) // Mono
                .Include(p => p.Profiles).ThenInclude(h => h.Items) // Mono
                .Include(p => p.Profiles)
                    .ThenInclude(h => h.Loadouts)
                    .ThenInclude(l => l.Groups)
                    .ThenInclude(group => group.Loadouts)
                .AsSplitQuery()
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);

            if (prefs is null)
                return null;

            // WOLFGATE(Prototypes) START: rows holding renamed prototype ids are saved under the current ids on load.
            var legacyRows = false;
            foreach (var profile in prefs.Profiles)
                legacyRows |= WFLegacyDbRows.Update(profile);

            if (legacyRows)
                await WFLegacyDbRows.Save(db.DbContext, _opsLog, cancel);
            // WOLFGATE END

            var maxSlot = prefs.Profiles.Max(p => p.Slot) + 1;
            var profiles = new Dictionary<int, ICharacterProfile>(maxSlot);
            foreach (var profile in prefs.Profiles)
            {
                profiles[profile.Slot] = ConvertProfiles(profile);
            }

            return new PlayerPreferences(profiles, prefs.SelectedCharacterSlot, Color.FromHex(prefs.AdminOOCColor));
        }

        public async Task SaveSelectedCharacterIndexAsync(NetUserId userId, int index)
        {
            await using var db = await GetDb();

            await SetSelectedCharacterSlotAsync(userId, index, db.DbContext);

            await db.DbContext.SaveChangesAsync();
        }

        public async Task SaveCharacterSlotAsync(NetUserId userId, ICharacterProfile? profile, int slot)
        {
            await using var db = await GetDb();

            if (profile is null)
            {
                await DeleteCharacterSlot(db.DbContext, userId, slot);
                await db.DbContext.SaveChangesAsync();
                return;
            }

            if (profile is not HumanoidCharacterProfile humanoid)
            {
                // TODO: Handle other ICharacterProfile implementations properly
                throw new NotImplementedException();
            }

            var oldProfile = db.DbContext.Profile
                .Include(p => p.Preference)
                .Where(p => p.Preference.UserId == userId.UserId)
                .Include(p => p.Jobs)
                .Include(p => p.Antags)
                .Include(p => p.Traits)
                .Include(p => p.Components) // Mono
                .Include(p => p.Items) // Mono
                .Include(p => p.Loadouts)
                    .ThenInclude(l => l.Groups)
                    .ThenInclude(group => group.Loadouts)
                .AsSplitQuery()
                .SingleOrDefault(h => h.Slot == slot);

            var newProfile = ConvertProfiles(humanoid, slot, oldProfile);
            if (oldProfile == null)
            {
                var prefs = await db.DbContext
                    .Preference
                    .Include(p => p.Profiles)
                    .SingleAsync(p => p.UserId == userId.UserId);

                prefs.Profiles.Add(newProfile);
            }

            await db.DbContext.SaveChangesAsync();
        }

        private static async Task DeleteCharacterSlot(ServerDbContext db, NetUserId userId, int slot)
        {
            var profile = await db.Profile.Include(p => p.Preference)
                .Where(p => p.Preference.UserId == userId.UserId && p.Slot == slot)
                .SingleOrDefaultAsync();

            if (profile == null)
            {
                return;
            }

            db.Profile.Remove(profile);
        }

        public async Task<PlayerPreferences> InitPrefsAsync(NetUserId userId, ICharacterProfile defaultProfile)
        {
            await using var db = await GetDb();

            var profile = ConvertProfiles((HumanoidCharacterProfile) defaultProfile, 0);
            var prefs = new Preference
            {
                UserId = userId.UserId,
                SelectedCharacterSlot = 0,
                AdminOOCColor = Color.Red.ToHex()
            };

            prefs.Profiles.Add(profile);

            db.DbContext.Preference.Add(prefs);

            await db.DbContext.SaveChangesAsync();

            return new PlayerPreferences(new[] {new KeyValuePair<int, ICharacterProfile>(0, defaultProfile)}, 0, Color.FromHex(prefs.AdminOOCColor));
        }

        public async Task DeleteSlotAndSetSelectedIndex(NetUserId userId, int deleteSlot, int newSlot)
        {
            await using var db = await GetDb();

            await DeleteCharacterSlot(db.DbContext, userId, deleteSlot);
            await SetSelectedCharacterSlotAsync(userId, newSlot, db.DbContext);

            await db.DbContext.SaveChangesAsync();
        }

        public async Task SaveAdminOOCColorAsync(NetUserId userId, Color color)
        {
            await using var db = await GetDb();
            var prefs = await db.DbContext
                .Preference
                .Include(p => p.Profiles)
                .SingleAsync(p => p.UserId == userId.UserId);
            prefs.AdminOOCColor = color.ToHex();

            await db.DbContext.SaveChangesAsync();

        }

        private static async Task SetSelectedCharacterSlotAsync(NetUserId userId, int newSlot, ServerDbContext db)
        {
            var prefs = await db.Preference.SingleAsync(p => p.UserId == userId.UserId);
            prefs.SelectedCharacterSlot = newSlot;
        }

        private static HumanoidCharacterProfile ConvertProfiles(Profile profile)
        {
            var jobs = profile.Jobs.ToDictionary(j => new ProtoId<JobPrototype>(j.JobName), j => (JobPriority) j.Priority);
            var antags = profile.Antags.Select(a => new ProtoId<AntagPrototype>(a.AntagName));
            var traits = profile.Traits.Select(t => new ProtoId<TraitPrototype>(t.TraitName));

            var sex = Sex.Male;
            if (Enum.TryParse<Sex>(profile.Sex, true, out var sexVal))
                sex = sexVal;

            var spawnPriority = (SpawnPriorityPreference) profile.SpawnPriority;

            var gender = sex == Sex.Male ? Gender.Male : Gender.Female;
            if (Enum.TryParse<Gender>(profile.Gender, true, out var genderVal))
                gender = genderVal;

            var balance = profile.BankBalance;

            // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
            var markingsRaw = profile.Markings?.Deserialize<List<string>>();

            List<Marking> markings = new();
            if (markingsRaw != null)
            {
                foreach (var marking in markingsRaw)
                {
                    var parsed = Marking.ParseFromDbString(marking);

                    if (parsed is null) continue;

                    markings.Add(parsed);
                }
            }

            var loadouts = new Dictionary<string, RoleLoadout>();

            foreach (var role in profile.Loadouts)
            {
                var loadout = new RoleLoadout(role.RoleName)
                {
                    EntityName = role.EntityName,
                    CustomJobTitle = role.CustomJobTitle, // WOLFGATE(Roles)
                };

                foreach (var group in role.Groups)
                {
                    var groupLoadouts = loadout.SelectedLoadouts.GetOrNew(group.GroupName);
                    foreach (var profLoadout in group.Loadouts)
                    {
                        groupLoadouts.Add(new Loadout()
                        {
                            // WOLFGATE(Prototypes): rows saved before a loadout rename load under its current id.
                            Prototype = WFLegacyPrototypeIds.Resolve(WFLegacyPrototypeIds.Loadouts, profLoadout.LoadoutName),
                        });
                    }
                }

                loadouts[role.RoleName] = loadout;
            }

            // Get the company with fallback to default "None"
            var company = profile.Company ?? "None";

            // Validate height and width to prevent sprite scale errors
            // Database migration set default values to 0f for existing profiles
            var height = profile.Height <= 0.005f ? 1.0f : profile.Height;
            var width = profile.Width <= 0.005f ? 1.0f : profile.Width;

            return new HumanoidCharacterProfile(
                profile.CharacterName,
                profile.FlavorText,
                WFLegacyPrototypeIds.Resolve(WFLegacyPrototypeIds.Species, profile.Species), // WOLFGATE(Prototypes): renamed species ids
                profile.Age,
                sex,
                gender,
                balance,
                new HumanoidCharacterAppearance
                (
                    profile.HairName,
                    ParseHairColors(profile.HairColor),
                    profile.FacialHairName,
                    ParseProfileColor(profile.FacialHairColor),
                    ParseProfileColor(profile.EyeColor),
                    ParseProfileColor(profile.SkinColor),
                    markings,
                    height,
                    width
                ),
                spawnPriority,
                jobs,
                (PreferenceUnavailableMode) profile.PreferenceUnavailable,
                antags.ToHashSet(),
                traits.ToHashSet(),
                loadouts,
                company,
                profile.CustomSpeciesName ?? string.Empty, // WOLFGATE(Humanoid)
                // Mono start
                profile.Flags,
                profile.Components.Select(component => new PersistentProfileComponent(
                    component.Data,
                    component.Sticky)),
                profile.Items.Select(item => new PersistentProfileItem(
                    item.Data,
                    // WOLFGATE(EmoteVoices) START: the chosen voices follow the Mono data
                    // item.Sticky))); // Mono end
                    item.Sticky)), // Mono end
                EmoteVoiceRules.FromStored(profile.ScreamVoice),
                EmoteVoiceRules.FromStored(profile.LaughVoice))
                // WOLFGATE END
            {
                HeadshotUrl = profile.HeadshotUrl ?? string.Empty, // WOLFGATE(Headshot)
                MismatchedParts = profile.MismatchedParts, // WOLFGATE(Species)
                CustomMarkings = CustomMarkingRules.FromStored(profile.CustomMarkings), // WOLFGATE(CustomMarkings)
                LegStance = (LegStance) profile.LegStance, // WOLFGATE(LegStyle)
                Biotype = (WFBiotype) profile.Biotype, // WOLFGATE(Biotype)
                Augments = WFAugmentRules.FromStored(profile.Augments), // WOLFGATE(Augments)
                AugmentUnlocks = WFAugmentRules.UnlocksFromStored(profile.AugmentUnlocks), // WOLFGATE(Augments)
                BloodColor = WFBloodColourRules.FromStored(profile.BloodColor), // WOLFGATE(BloodColour)
                ChatFont = WFChatFontRules.FromStored(profile.ChatFont), // WOLFGATE(ChatFont)
                TypingIndicator = WFTypingBubbleRules.FromStored(profile.TypingIndicator), // WOLFGATE(TypingBubble)
                SpeechVoice = SpeechVoiceJson.FromStored(profile.SpeechVoice), // WOLFGATE(Voice)
            };
        }

        private static List<Color> ParseHairColors(string stored)
        {
            var colors = new List<Color>();
            foreach (var part in HairColorParts(stored))
                colors.Add(ParseProfileColor(part));

            if (colors.Count == 0)
                colors.Add(Color.Black);

            return colors;
        }

        /// <summary>
        /// Older rows stored a hair colour list as a JSON array (<c>["#RRGGBBAA"]</c>).
        /// Current rows are comma-separated hex. A bad value stays black instead of dropping the player.
        /// </summary>
        private static IEnumerable<string> HairColorParts(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                yield break;

            var trimmed = stored.Trim();
            if (trimmed.StartsWith('['))
            {
                List<string>? parsed = null;
                try
                {
                    parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                }
                catch (JsonException)
                {
                    parsed = null;
                }

                if (parsed != null)
                {
                    foreach (var part in parsed)
                        yield return part;

                    yield break;
                }
            }

            foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return part;
        }

        private static Color ParseProfileColor(string stored)
        {
            var hex = stored.Trim().Trim('"');
            if (hex.Length == 0)
                return Color.Black;

            return Color.FromHex(hex, Color.Black);
        }

        private static Profile ConvertProfiles(HumanoidCharacterProfile humanoid, int slot, Profile? profile = null)
        {
            profile ??= new Profile();
            var appearance = (HumanoidCharacterAppearance) humanoid.CharacterAppearance;
            List<string> markingStrings = new();
            foreach (var marking in appearance.Markings)
            {
                markingStrings.Add(marking.ToString());
            }
            var markings = JsonSerializer.SerializeToDocument(markingStrings);

            profile.CharacterName = humanoid.Name;
            profile.FlavorText = humanoid.FlavorText;
            profile.Species = humanoid.Species;
            profile.Age = humanoid.Age;
            profile.Sex = humanoid.Sex.ToString();
            profile.Gender = humanoid.Gender.ToString();
            profile.BankBalance = humanoid.BankBalance;
            profile.HairName = appearance.HairStyleId;
            profile.HairColor = string.Join(",", appearance.HairColor.Select(color => color.ToHex()));
            profile.FacialHairName = appearance.FacialHairStyleId;
            profile.FacialHairColor = appearance.FacialHairColor.ToHex();
            profile.EyeColor = appearance.EyeColor.ToHex();
            profile.SkinColor = appearance.SkinColor.ToHex();
            profile.Height = appearance.Height;
            profile.Width = appearance.Width;
            profile.SpawnPriority = (int) humanoid.SpawnPriority;
            profile.Markings = markings;
            profile.Slot = slot;
            profile.PreferenceUnavailable = (DbPreferenceUnavailableMode) humanoid.PreferenceUnavailable;
            profile.Company = humanoid.Company;
            profile.CustomSpeciesName = humanoid.CustomSpeciesName; // WOLFGATE(Humanoid)
            profile.HeadshotUrl = humanoid.HeadshotUrl; // WOLFGATE(Headshot)
            profile.MismatchedParts = humanoid.MismatchedParts; // WOLFGATE(Species)
            profile.CustomMarkings = CustomMarkingRules.ToStored(humanoid.CustomMarkings); // WOLFGATE(CustomMarkings)
            profile.LegStance = (int) humanoid.LegStance; // WOLFGATE(LegStyle)
            profile.Biotype = (int) humanoid.Biotype; // WOLFGATE(Biotype)
            profile.Augments = WFAugmentRules.ToStored(humanoid.Augments); // WOLFGATE(Augments)
            profile.AugmentUnlocks = WFAugmentRules.ToStored(humanoid.AugmentUnlocks); // WOLFGATE(Augments)
            profile.BloodColor = WFBloodColourRules.ToStored(humanoid.BloodColor); // WOLFGATE(BloodColour)
            profile.ChatFont = WFChatFontRules.ToStored(humanoid.ChatFont); // WOLFGATE(ChatFont)
            profile.TypingIndicator = WFTypingBubbleRules.ToStored(humanoid.TypingIndicator); // WOLFGATE(TypingBubble)
            profile.ScreamVoice = EmoteVoiceRules.ToStored(humanoid.ScreamVoice); // WOLFGATE(EmoteVoices)
            profile.LaughVoice = EmoteVoiceRules.ToStored(humanoid.LaughVoice); // WOLFGATE(EmoteVoices)
            profile.SpeechVoice = SpeechVoiceJson.ToStored(humanoid.SpeechVoice); // WOLFGATE(Voice)

            // Mono start
            profile.Flags = [..humanoid.Flags];
            profile.Components.Clear();
            profile.Components.AddRange(humanoid.Components.Select(component => new ProfileComponent
            {
                Data = component.Data,
                Sticky = component.Sticky,
            }));
            profile.Items.Clear();
            profile.Items.AddRange(humanoid.Items.Select(item => new ProfileItem
            {
                Data = item.Data,
                Sticky = item.Sticky,
            }));
            // Mono end

            profile.Jobs.Clear();
            profile.Jobs.AddRange(
                humanoid.JobPriorities
                    .Where(j => j.Value != JobPriority.Never)
                    .Select(j => new Job {JobName = j.Key, Priority = (DbJobPriority) j.Value})
            );

            profile.Antags.Clear();
            profile.Antags.AddRange(
                humanoid.AntagPreferences
                    .Select(a => new Antag {AntagName = a})
            );

            profile.Traits.Clear();
            profile.Traits.AddRange(
                humanoid.TraitPreferences
                        .Select(t => new Trait {TraitName = t})
            );

            profile.Loadouts.Clear();

            foreach (var (role, loadouts) in humanoid.Loadouts)
            {
                var dz = new ProfileRoleLoadout()
                {
                    RoleName = role,
                    EntityName = loadouts.EntityName ?? string.Empty,
                    CustomJobTitle = loadouts.CustomJobTitle, // WOLFGATE(Roles)
                };

                foreach (var (group, groupLoadouts) in loadouts.SelectedLoadouts)
                {
                    var profileGroup = new ProfileLoadoutGroup()
                    {
                        GroupName = group,
                    };

                    foreach (var loadout in groupLoadouts)
                    {
                        profileGroup.Loadouts.Add(new ProfileLoadout()
                        {
                            LoadoutName = loadout.Prototype,
                        });
                    }

                    dz.Groups.Add(profileGroup);
                }

                profile.Loadouts.Add(dz);
            }

            return profile;
        }
        #endregion

        #region User Ids
        public async Task<NetUserId?> GetAssignedUserIdAsync(string name)
        {
            await using var db = await GetDb();

            var assigned = await db.DbContext.AssignedUserId.SingleOrDefaultAsync(p => p.UserName == name);
            return assigned?.UserId is { } g ? new NetUserId(g) : default(NetUserId?);
        }

        public async Task AssignUserIdAsync(string name, NetUserId netUserId)
        {
            await using var db = await GetDb();

            db.DbContext.AssignedUserId.Add(new AssignedUserId
            {
                UserId = netUserId.UserId,
                UserName = name
            });

            await db.DbContext.SaveChangesAsync();
        }
        #endregion

        #region MonoCoins

        public async Task<long> GetMonoCoinsAsync(NetUserId userId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var prefs = await db.DbContext.Preference
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);

            return prefs?.MonoCoins ?? 0l;
        }

        public async Task SetMonoCoinsAsync(NetUserId userId, long balance, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var prefs = await db.DbContext.Preference
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);

            if (prefs != null)
            {
                prefs.MonoCoins = Math.Max(0l, balance); // Ensure balance is never negative
                await db.DbContext.SaveChangesAsync(cancel);
            }
        }

        public async Task<long> AddMonoCoinsAsync(NetUserId userId, long amount, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var prefs = await db.DbContext.Preference
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);

            if (prefs != null)
            {
                prefs.MonoCoins += amount;
                prefs.MonoCoins = Math.Max(0l, prefs.MonoCoins); // Ensure balance is never negative
                await db.DbContext.SaveChangesAsync(cancel);
                return prefs.MonoCoins;
            }

            return 0;
        }

        #endregion

        #region Bans
        /*
         * BAN STUFF
         */
        /// <summary>
        ///     Looks up a ban by id.
        ///     This will return a pardoned ban as well.
        /// </summary>
        /// <param name="id">The ban id to look for.</param>
        /// <returns>The ban with the given id or null if none exist.</returns>
        public abstract Task<ServerBanDef?> GetServerBanAsync(int id);

        /// <summary>
        ///     Looks up an user's most recent received un-pardoned ban.
        ///     This will NOT return a pardoned ban.
        ///     One of <see cref="address"/> or <see cref="userId"/> need to not be null.
        /// </summary>
        /// <param name="address">The ip address of the user.</param>
        /// <param name="userId">The id of the user.</param>
        /// <param name="hwId">The legacy HWId of the user.</param>
        /// <param name="modernHWIds">The modern HWIDs of the user.</param>
        /// <returns>The user's latest received un-pardoned ban, or null if none exist.</returns>
        public abstract Task<ServerBanDef?> GetServerBanAsync(
            IPAddress? address,
            NetUserId? userId,
            ImmutableArray<byte>? hwId,
            ImmutableArray<ImmutableArray<byte>>? modernHWIds);

        /// <summary>
        ///     Looks up an user's ban history.
        ///     This will return pardoned bans as well.
        ///     One of <see cref="address"/> or <see cref="userId"/> need to not be null.
        /// </summary>
        /// <param name="address">The ip address of the user.</param>
        /// <param name="userId">The id of the user.</param>
        /// <param name="hwId">The legacy HWId of the user.</param>
        /// <param name="modernHWIds">The modern HWIDs of the user.</param>
        /// <param name="includeUnbanned">Include pardoned and expired bans.</param>
        /// <returns>The user's ban history.</returns>
        public abstract Task<List<ServerBanDef>> GetServerBansAsync(
            IPAddress? address,
            NetUserId? userId,
            ImmutableArray<byte>? hwId,
            ImmutableArray<ImmutableArray<byte>>? modernHWIds,
            bool includeUnbanned);

        public abstract Task AddServerBanAsync(ServerBanDef serverBan);
        public abstract Task AddServerUnbanAsync(ServerUnbanDef serverUnban);

        public async Task EditServerBan(int id, string reason, NoteSeverity severity, DateTimeOffset? expiration, Guid editedBy, DateTimeOffset editedAt)
        {
            await using var db = await GetDb();

            var ban = await db.DbContext.Ban.SingleOrDefaultAsync(b => b.Id == id);
            if (ban is null)
                return;
            ban.Severity = severity;
            ban.Reason = reason;
            ban.ExpirationTime = expiration?.UtcDateTime;
            ban.LastEditedById = editedBy;
            ban.LastEditedAt = editedAt.UtcDateTime;
            await db.DbContext.SaveChangesAsync();
        }

        protected static async Task<ServerBanExemptFlags?> GetBanExemptionCore(
            DbGuard db,
            NetUserId? userId,
            CancellationToken cancel = default)
        {
            if (userId == null)
                return null;

            var exemption = await db.DbContext.BanExemption
                .SingleOrDefaultAsync(e => e.UserId == userId.Value.UserId, cancellationToken: cancel);

            return exemption?.Flags;
        }

        public async Task UpdateBanExemption(NetUserId userId, ServerBanExemptFlags flags)
        {
            await using var db = await GetDb();

            if (flags == 0)
            {
                // Delete whatever is there.
                await db.DbContext.BanExemption.Where(u => u.UserId == userId.UserId).ExecuteDeleteAsync();
                return;
            }

            var exemption = await db.DbContext.BanExemption.SingleOrDefaultAsync(u => u.UserId == userId.UserId);
            if (exemption == null)
            {
                exemption = new ServerBanExemption
                {
                    UserId = userId
                };

                db.DbContext.BanExemption.Add(exemption);
            }

            exemption.Flags = flags;
            await db.DbContext.SaveChangesAsync();
        }

        public async Task<ServerBanExemptFlags> GetBanExemption(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var flags = await GetBanExemptionCore(db, userId, cancel);
            return flags ?? ServerBanExemptFlags.None;
        }

        #endregion

        #region Role Bans
        /*
         * ROLE BANS
         */
        /// <summary>
        ///     Looks up a role ban by id.
        ///     This will return a pardoned role ban as well.
        /// </summary>
        /// <param name="id">The role ban id to look for.</param>
        /// <returns>The role ban with the given id or null if none exist.</returns>
        public abstract Task<ServerRoleBanDef?> GetServerRoleBanAsync(int id);

        /// <summary>
        ///     Looks up an user's role ban history.
        ///     This will return pardoned role bans based on the <see cref="includeUnbanned"/> bool.
        ///     Requires one of <see cref="address"/>, <see cref="userId"/>, or <see cref="hwId"/> to not be null.
        /// </summary>
        /// <param name="address">The IP address of the user.</param>
        /// <param name="userId">The NetUserId of the user.</param>
        /// <param name="hwId">The Hardware Id of the user.</param>
        /// <param name="modernHWIds">The modern HWIDs of the user.</param>
        /// <param name="includeUnbanned">Whether expired and pardoned bans are included.</param>
        /// <returns>The user's role ban history.</returns>
        public abstract Task<List<ServerRoleBanDef>> GetServerRoleBansAsync(IPAddress? address,
            NetUserId? userId,
            ImmutableArray<byte>? hwId,
            ImmutableArray<ImmutableArray<byte>>? modernHWIds,
            bool includeUnbanned);

        public abstract Task<ServerRoleBanDef> AddServerRoleBanAsync(ServerRoleBanDef serverRoleBan);
        public abstract Task AddServerRoleUnbanAsync(ServerRoleUnbanDef serverRoleUnban);

        public async Task EditServerRoleBan(int id, string reason, NoteSeverity severity, DateTimeOffset? expiration, Guid editedBy, DateTimeOffset editedAt)
        {
            await using var db = await GetDb();
            var roleBanDetails = await db.DbContext.RoleBan
                .Where(b => b.Id == id)
                .Select(b => new { b.BanTime, b.PlayerUserId })
                .SingleOrDefaultAsync();

            if (roleBanDetails == default)
                return;

            await db.DbContext.RoleBan
                .Where(b => b.BanTime == roleBanDetails.BanTime && b.PlayerUserId == roleBanDetails.PlayerUserId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(b => b.Severity, severity)
                    .SetProperty(b => b.Reason, reason)
                    .SetProperty(b => b.ExpirationTime, expiration.HasValue ? expiration.Value.UtcDateTime : (DateTime?)null)
                    .SetProperty(b => b.LastEditedById, editedBy)
                    .SetProperty(b => b.LastEditedAt, editedAt.UtcDateTime)
                );
        }
        #endregion

        #region Playtime
        public async Task<List<PlayTime>> GetPlayTimes(Guid player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            return await db.DbContext.PlayTime
                .Where(p => p.PlayerId == player)
                .ToListAsync(cancel);
        }

        public async Task UpdatePlayTimes(IReadOnlyCollection<PlayTimeUpdate> updates)
        {
            await using var db = await GetDb();

            // Ideally I would just be able to send a bunch of UPSERT commands, but EFCore is a pile of garbage.
            // So... In the interest of not making this take forever at high update counts...
            // Bulk-load play time objects for all players involved.
            // This allows us to semi-efficiently load all entities we need in a single DB query.
            // Then we can update & insert without further round-trips to the DB.

            var players = updates.Select(u => u.User.UserId).Distinct().ToList();
            var dbTimes = (await db.DbContext.PlayTime
                    .Where(p => players.Contains(p.PlayerId))
                    .ToArrayAsync())
                .GroupBy(p => p.PlayerId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(p => p.Tracker, p => p));

            foreach (var (user, tracker, time) in updates)
            {
                if (dbTimes.TryGetValue(user.UserId, out var userTimes)
                    && userTimes.TryGetValue(tracker, out var ent))
                {
                    // Already have a tracker in the database, update it.
                    ent.TimeSpent = time;
                    continue;
                }

                // No tracker, make a new one.
                var playTime = new PlayTime
                {
                    Tracker = tracker,
                    PlayerId = user.UserId,
                    TimeSpent = time
                };

                db.DbContext.PlayTime.Add(playTime);
            }

            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region Player Records
        /*
         * PLAYER RECORDS
         */
        public async Task UpdatePlayerRecord(
            NetUserId userId,
            string userName,
            IPAddress address,
            ImmutableTypedHwid? hwId)
        {
            await using var db = await GetDb();

            var record = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == userId.UserId);
            if (record == null)
            {
                db.DbContext.Player.Add(record = new Player
                {
                    FirstSeenTime = DateTime.UtcNow,
                    UserId = userId.UserId,
                });
            }

            record.LastSeenTime = DateTime.UtcNow;
            record.LastSeenAddress = address;
            record.LastSeenUserName = userName;
            record.LastSeenHWId = hwId;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task<PlayerRecord?> GetPlayerRecordByUserName(string userName, CancellationToken cancel)
        {
            await using var db = await GetDb();

            // Sort by descending last seen time.
            // So if, due to account renames, we have two people with the same username in the DB,
            // the most recent one is picked.
            var record = await db.DbContext.Player
                .OrderByDescending(p => p.LastSeenTime)
                .FirstOrDefaultAsync(p => p.LastSeenUserName == userName, cancel);

            return record == null ? null : MakePlayerRecord(record);
        }

        public async Task<PlayerRecord?> GetPlayerRecordByUserId(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDb();

            var record = await db.DbContext.Player
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);

            return record == null ? null : MakePlayerRecord(record);
        }

        protected async Task<bool> PlayerRecordExists(DbGuard db, NetUserId userId)
        {
            return await db.DbContext.Player.AnyAsync(p => p.UserId == userId);
        }

        [return: NotNullIfNotNull(nameof(player))]
        protected PlayerRecord? MakePlayerRecord(Player? player)
        {
            if (player == null)
                return null;

            return new PlayerRecord(
                new NetUserId(player.UserId),
                new DateTimeOffset(NormalizeDatabaseTime(player.FirstSeenTime)),
                player.LastSeenUserName,
                new DateTimeOffset(NormalizeDatabaseTime(player.LastSeenTime)),
                player.LastSeenAddress,
                player.LastSeenHWId);
        }

        #endregion

        #region Connection Logs
        /*
         * CONNECTION LOG
         */
        public abstract Task<int> AddConnectionLogAsync(NetUserId userId,
            string userName,
            IPAddress address,
            ImmutableTypedHwid? hwId,
            float trust,
            ConnectionDenyReason? denied,
            int serverId);

        public async Task AddServerBanHitsAsync(int connection, IEnumerable<ServerBanDef> bans)
        {
            await using var db = await GetDb();

            foreach (var ban in bans)
            {
                db.DbContext.ServerBanHit.Add(new ServerBanHit
                {
                    ConnectionId = connection, BanId = ban.Id!.Value
                });
            }

            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region Admin Ranks
        /*
         * ADMIN RANKS
         */
        public async Task<Admin?> GetAdminDataForAsync(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            return await db.DbContext.Admin
                .Include(p => p.Flags)
                .Include(p => p.AdminRank)
                .ThenInclude(p => p!.Flags)
                .AsSplitQuery() // tests fail because of a random warning if you dont have this!
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);
        }

        public abstract Task<((Admin, string? lastUserName)[] admins, AdminRank[])>
            GetAllAdminAndRanksAsync(CancellationToken cancel);

        public async Task<AdminRank?> GetAdminRankDataForAsync(int id, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            return await db.DbContext.AdminRank
                .Include(r => r.Flags)
                .SingleOrDefaultAsync(r => r.Id == id, cancel);
        }

        public async Task RemoveAdminAsync(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var admin = await db.DbContext.Admin.SingleAsync(a => a.UserId == userId.UserId, cancel);
            db.DbContext.Admin.Remove(admin);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task AddAdminAsync(Admin admin, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            db.DbContext.Admin.Add(admin);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task UpdateAdminAsync(Admin admin, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var existing = await db.DbContext.Admin.Include(a => a.Flags).SingleAsync(a => a.UserId == admin.UserId, cancel);
            existing.Flags = admin.Flags;
            existing.Title = admin.Title;
            existing.AdminRankId = admin.AdminRankId;
            existing.Deadminned = admin.Deadminned;
            existing.Suspended = admin.Suspended;

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task UpdateAdminDeadminnedAsync(NetUserId userId, bool deadminned, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var adminRecord = db.DbContext.Admin.Where(a => a.UserId == userId);
            await adminRecord.ExecuteUpdateAsync(
                set => set.SetProperty(p => p.Deadminned, deadminned),
                cancellationToken: cancel);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task RemoveAdminRankAsync(int rankId, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var admin = await db.DbContext.AdminRank.SingleAsync(a => a.Id == rankId, cancel);
            db.DbContext.AdminRank.Remove(admin);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task AddAdminRankAsync(AdminRank rank, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            db.DbContext.AdminRank.Add(rank);

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task<int> AddNewRound(Server server, params Guid[] playerIds)
        {
            await using var db = await GetDb();

            var playerIdsList = playerIds.ToList();

            var players = await db.DbContext.Player
                .Where(player => playerIdsList.Contains(player.UserId))
                .ToListAsync();

            var round = new Round
            {
                StartDate = DateTime.UtcNow,
                Players = players,
                ServerId = server.Id
            };

            db.DbContext.Round.Add(round);

            await db.DbContext.SaveChangesAsync();

            return round.Id;
        }

        public async Task<Round> GetRound(int id)
        {
            await using var db = await GetDb();

            var round = await db.DbContext.Round
                .Include(round => round.Players)
                .SingleAsync(round => round.Id == id);

            return round;
        }

        public async Task AddRoundPlayers(int id, Guid[] playerIds)
        {
            await using var db = await GetDb();
            var playerIdsList = playerIds.ToList();

            // ReSharper disable once SuggestVarOrType_Elsewhere
            Dictionary<Guid, int> players = await db.DbContext.Player
                .Where(player => playerIdsList.Contains(player.UserId))
                .ToDictionaryAsync(player => player.UserId, player => player.Id);

            foreach (var player in playerIds)
            {
                await db.DbContext.Database.ExecuteSqlAsync($"""
INSERT INTO player_round (players_id, rounds_id) VALUES ({players[player]}, {id}) ON CONFLICT DO NOTHING
""");
            }

            await db.DbContext.SaveChangesAsync();
        }

        [return: NotNullIfNotNull(nameof(round))]
        protected RoundRecord? MakeRoundRecord(Round? round)
        {
            if (round == null)
                return null;

            return new RoundRecord(
                round.Id,
                NormalizeDatabaseTime(round.StartDate),
                MakeServerRecord(round.Server));
        }

        public async Task UpdateAdminRankAsync(AdminRank rank, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);

            var existing = await db.DbContext.AdminRank
                .Include(r => r.Flags)
                .SingleAsync(a => a.Id == rank.Id, cancel);

            existing.Flags = rank.Flags;
            existing.Name = rank.Name;
            existing.ShortName = rank.ShortName; // Mono

            await db.DbContext.SaveChangesAsync(cancel);
        }
        #endregion

        #region Admin Logs

        public async Task<(Server, bool existed)> AddOrGetServer(string serverName)
        {
            await using var db = await GetDb();
            var server = await db.DbContext.Server
                .Where(server => server.Name.Equals(serverName))
                .SingleOrDefaultAsync();

            if (server != default)
                return (server, true);

            server = new Server
            {
                Name = serverName
            };

            db.DbContext.Server.Add(server);

            await db.DbContext.SaveChangesAsync();

            return (server, false);
        }

        [return: NotNullIfNotNull(nameof(server))]
        protected ServerRecord? MakeServerRecord(Server? server)
        {
            if (server == null)
                return null;

            return new ServerRecord(server.Id, server.Name);
        }

        public async Task AddAdminLogs(List<AdminLog> logs)
        {
            const int maxRetryAttempts = 5;
            var initialRetryDelay = TimeSpan.FromSeconds(5);

            DebugTools.Assert(logs.All(x => x.RoundId > 0), "Adding logs with invalid round ids.");

            var attempt = 0;
            var retryDelay = initialRetryDelay;

            while (attempt < maxRetryAttempts)
            {
                try
                {
                    await using var db = await GetDb();
                    db.DbContext.AdminLog.AddRange(logs);
                    await db.DbContext.SaveChangesAsync();
                    _opsLog.Debug($"Successfully saved {logs.Count} admin logs.");
                    break;
                }
                catch (Exception ex)
                {
                    attempt += 1;
                    _opsLog.Error($"Attempt {attempt} failed to save logs: {ex}");

                    if (attempt >= maxRetryAttempts)
                    {
                        _opsLog.Error($"Max retry attempts reached. Failed to save {logs.Count} admin logs.");
                        return;
                    }

                    _opsLog.Warning($"Retrying in {retryDelay.TotalSeconds} seconds...");
                    await Task.Delay(retryDelay);

                    retryDelay *= 2;
                }
            }
        }

        protected abstract IQueryable<AdminLog> StartAdminLogsQuery(ServerDbContext db, LogFilter? filter = null);

        private IQueryable<AdminLog> GetAdminLogsQuery(ServerDbContext db, LogFilter? filter = null)
        {
            // Save me from SQLite
            var query = StartAdminLogsQuery(db, filter);

            if (filter == null)
            {
                return query.OrderBy(log => log.Date);
            }

            if (filter.Round != null)
            {
                query = query.Where(log => log.RoundId == filter.Round);
            }

            if (filter.Types != null)
            {
                query = query.Where(log => filter.Types.Contains(log.Type));
            }

            if (filter.Impacts != null)
            {
                query = query.Where(log => filter.Impacts.Contains(log.Impact));
            }

            if (filter.Before != null)
            {
                query = query.Where(log => log.Date < filter.Before);
            }

            if (filter.After != null)
            {
                query = query.Where(log => log.Date > filter.After);
            }

            if (filter.IncludePlayers)
            {
                if (filter.AnyPlayers != null)
                {
                    query = query.Where(log =>
                        log.Players.Any(p => filter.AnyPlayers.Contains(p.PlayerUserId)) ||
                        log.Players.Count == 0 && filter.IncludeNonPlayers);
                }

                if (filter.AllPlayers != null)
                {
                    query = query.Where(log =>
                        log.Players.All(p => filter.AllPlayers.Contains(p.PlayerUserId)) ||
                        log.Players.Count == 0 && filter.IncludeNonPlayers);
                }
            }
            else
            {
                query = query.Where(log => log.Players.Count == 0);
            }

            if (filter.LastLogId != null)
            {
                query = filter.DateOrder switch
                {
                    DateOrder.Ascending => query.Where(log => log.Id > filter.LastLogId),
                    DateOrder.Descending => query.Where(log => log.Id < filter.LastLogId),
                    _ => throw new ArgumentOutOfRangeException(nameof(filter),
                        $"Unknown {nameof(DateOrder)} value {filter.DateOrder}")
                };
            }

            query = filter.DateOrder switch
            {
                DateOrder.Ascending => query.OrderBy(log => log.Date),
                DateOrder.Descending => query.OrderByDescending(log => log.Date),
                _ => throw new ArgumentOutOfRangeException(nameof(filter),
                    $"Unknown {nameof(DateOrder)} value {filter.DateOrder}")
            };

            const int hardLogLimit = 500_000;
            if (filter.Limit != null)
            {
                query = query.Take(Math.Min(filter.Limit.Value, hardLogLimit));
            }
            else
            {
                query = query.Take(hardLogLimit);
            }

            return query;
        }

        public async IAsyncEnumerable<string> GetAdminLogMessages(LogFilter? filter = null)
        {
            await using var db = await GetDb();
            var query = GetAdminLogsQuery(db.DbContext, filter);

            await foreach (var log in query.Select(log => log.Message).AsAsyncEnumerable())
            {
                yield return log;
            }
        }

        public async IAsyncEnumerable<SharedAdminLog> GetAdminLogs(LogFilter? filter = null)
        {
            await using var db = await GetDb();
            var query = GetAdminLogsQuery(db.DbContext, filter);
            query = query.Include(log => log.Players);

            await foreach (var log in query.AsAsyncEnumerable())
            {
                var players = new Guid[log.Players.Count];
                for (var i = 0; i < log.Players.Count; i++)
                {
                    players[i] = log.Players[i].PlayerUserId;
                }

                yield return new SharedAdminLog(log.Id, log.Type, log.Impact, log.Date, log.Message, players);
            }
        }

        public async IAsyncEnumerable<JsonDocument> GetAdminLogsJson(LogFilter? filter = null)
        {
            await using var db = await GetDb();
            var query = GetAdminLogsQuery(db.DbContext, filter);

            await foreach (var json in query.Select(log => log.Json).AsAsyncEnumerable())
            {
                yield return json;
            }
        }

        public async Task<int> CountAdminLogs(int round)
        {
            await using var db = await GetDb();
            return await db.DbContext.AdminLog.CountAsync(log => log.RoundId == round);
        }

        #endregion

        #region Whitelist

        public async Task<bool> GetWhitelistStatusAsync(NetUserId player)
        {
            await using var db = await GetDb();

            return await db.DbContext.Whitelist.AnyAsync(w => w.UserId == player);
        }

        public async Task AddToWhitelistAsync(NetUserId player)
        {
            await using var db = await GetDb();

            db.DbContext.Whitelist.Add(new Whitelist { UserId = player });
            await db.DbContext.SaveChangesAsync();
        }

        public async Task RemoveFromWhitelistAsync(NetUserId player)
        {
            await using var db = await GetDb();
            var entry = await db.DbContext.Whitelist.SingleAsync(w => w.UserId == player);
            db.DbContext.Whitelist.Remove(entry);
            await db.DbContext.SaveChangesAsync();
        }

        public async Task<DateTimeOffset?> GetLastReadRules(NetUserId player)
        {
            await using var db = await GetDb();

            return NormalizeDatabaseTime(await db.DbContext.Player
                .Where(dbPlayer => dbPlayer.UserId == player)
                .Select(dbPlayer => dbPlayer.LastReadRules)
                .SingleOrDefaultAsync());
        }

        public async Task SetLastReadRules(NetUserId player, DateTimeOffset? date)
        {
            await using var db = await GetDb();

            var dbPlayer = await db.DbContext.Player.Where(dbPlayer => dbPlayer.UserId == player).SingleOrDefaultAsync();
            if (dbPlayer == null)
            {
                return;
            }

            dbPlayer.LastReadRules = date?.UtcDateTime;
            await db.DbContext.SaveChangesAsync();
        }

        public async Task<bool> GetBlacklistStatusAsync(NetUserId player)
        {
            await using var db = await GetDb();

            return await db.DbContext.Blacklist.AnyAsync(w => w.UserId == player);
        }

        public async Task AddToBlacklistAsync(NetUserId player)
        {
            await using var db = await GetDb();

            db.DbContext.Blacklist.Add(new Blacklist() { UserId = player });
            await db.DbContext.SaveChangesAsync();
        }

        public async Task RemoveFromBlacklistAsync(NetUserId player)
        {
            await using var db = await GetDb();
            var entry = await db.DbContext.Blacklist.SingleAsync(w => w.UserId == player);
            db.DbContext.Blacklist.Remove(entry);
            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region Uploaded Resources Logs

        public async Task AddUploadedResourceLogAsync(NetUserId user, DateTimeOffset date, string path, byte[] data)
        {
            await using var db = await GetDb();

            db.DbContext.UploadedResourceLog.Add(new UploadedResourceLog() { UserId = user, Date = date.UtcDateTime, Path = path, Data = data });
            await db.DbContext.SaveChangesAsync();
        }

        public async Task PurgeUploadedResourceLogAsync(int days)
        {
            await using var db = await GetDb();

            var date = DateTime.UtcNow.Subtract(TimeSpan.FromDays(days));

            await foreach (var log in db.DbContext.UploadedResourceLog
                               .Where(l => date > l.Date)
                               .AsAsyncEnumerable())
            {
                db.DbContext.UploadedResourceLog.Remove(log);
            }

            await db.DbContext.SaveChangesAsync();
        }

        #endregion

        #region Admin Notes

        public virtual async Task<int> AddAdminNote(AdminNote note)
        {
            await using var db = await GetDb();
            db.DbContext.AdminNotes.Add(note);
            await db.DbContext.SaveChangesAsync();
            return note.Id;
        }

        public virtual async Task<int> AddAdminWatchlist(AdminWatchlist watchlist)
        {
            await using var db = await GetDb();
            db.DbContext.AdminWatchlists.Add(watchlist);
            await db.DbContext.SaveChangesAsync();
            return watchlist.Id;
        }

        public virtual async Task<int> AddAdminMessage(AdminMessage message)
        {
            await using var db = await GetDb();
            db.DbContext.AdminMessages.Add(message);
            await db.DbContext.SaveChangesAsync();
            return message.Id;
        }

        public async Task<AdminNoteRecord?> GetAdminNote(int id)
        {
            await using var db = await GetDb();
            var entity = await db.DbContext.AdminNotes
                .Where(note => note.Id == id)
                .Include(note => note.Round)
                .ThenInclude(r => r!.Server)
                .Include(note => note.CreatedBy)
                .Include(note => note.LastEditedBy)
                .Include(note => note.DeletedBy)
                .Include(note => note.Player)
                .SingleOrDefaultAsync();

            return entity == null ? null : MakeAdminNoteRecord(entity);
        }

        private AdminNoteRecord MakeAdminNoteRecord(AdminNote entity)
        {
            return new AdminNoteRecord(
                entity.Id,
                MakeRoundRecord(entity.Round),
                MakePlayerRecord(entity.Player),
                entity.PlaytimeAtNote,
                entity.Message,
                entity.Severity,
                MakePlayerRecord(entity.CreatedBy),
                NormalizeDatabaseTime(entity.CreatedAt),
                MakePlayerRecord(entity.LastEditedBy),
                NormalizeDatabaseTime(entity.LastEditedAt),
                NormalizeDatabaseTime(entity.ExpirationTime),
                entity.Deleted,
                MakePlayerRecord(entity.DeletedBy),
                NormalizeDatabaseTime(entity.DeletedAt),
                entity.Secret);
        }

        public async Task<AdminWatchlistRecord?> GetAdminWatchlist(int id)
        {
            await using var db = await GetDb();
            var entity = await db.DbContext.AdminWatchlists
                .Where(note => note.Id == id)
                .Include(note => note.Round)
                .ThenInclude(r => r!.Server)
                .Include(note => note.CreatedBy)
                .Include(note => note.LastEditedBy)
                .Include(note => note.DeletedBy)
                .Include(note => note.Player)
                .SingleOrDefaultAsync();

            return entity == null ? null : MakeAdminWatchlistRecord(entity);
        }

        public async Task<AdminMessageRecord?> GetAdminMessage(int id)
        {
            await using var db = await GetDb();
            var entity = await db.DbContext.AdminMessages
                .Where(note => note.Id == id)
                .Include(note => note.Round)
                .ThenInclude(r => r!.Server)
                .Include(note => note.CreatedBy)
                .Include(note => note.LastEditedBy)
                .Include(note => note.DeletedBy)
                .Include(note => note.Player)
                .SingleOrDefaultAsync();

            return entity == null ? null : MakeAdminMessageRecord(entity);
        }

        private AdminMessageRecord MakeAdminMessageRecord(AdminMessage entity)
        {
            return new AdminMessageRecord(
                entity.Id,
                MakeRoundRecord(entity.Round),
                MakePlayerRecord(entity.Player),
                entity.PlaytimeAtNote,
                entity.Message,
                MakePlayerRecord(entity.CreatedBy),
                NormalizeDatabaseTime(entity.CreatedAt),
                MakePlayerRecord(entity.LastEditedBy),
                NormalizeDatabaseTime(entity.LastEditedAt),
                NormalizeDatabaseTime(entity.ExpirationTime),
                entity.Deleted,
                MakePlayerRecord(entity.DeletedBy),
                NormalizeDatabaseTime(entity.DeletedAt),
                entity.Seen,
                entity.Dismissed);
        }

        public async Task<ServerBanNoteRecord?> GetServerBanAsNoteAsync(int id)
        {
            await using var db = await GetDb();

            var ban = await db.DbContext.Ban
                .Include(ban => ban.Unban)
                .Include(ban => ban.Round)
                .ThenInclude(r => r!.Server)
                .Include(ban => ban.CreatedBy)
                .Include(ban => ban.LastEditedBy)
                .Include(ban => ban.Unban)
                .SingleOrDefaultAsync(b => b.Id == id);

            if (ban is null)
                return null;

            var player = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == ban.PlayerUserId);
            return new ServerBanNoteRecord(
                ban.Id,
                MakeRoundRecord(ban.Round),
                MakePlayerRecord(player),
                ban.PlaytimeAtNote,
                ban.Reason,
                ban.Severity,
                MakePlayerRecord(ban.CreatedBy),
                ban.BanTime,
                MakePlayerRecord(ban.LastEditedBy),
                ban.LastEditedAt,
                ban.ExpirationTime,
                ban.Hidden,
                MakePlayerRecord(ban.Unban?.UnbanningAdmin == null
                    ? null
                    : await db.DbContext.Player.SingleOrDefaultAsync(p =>
                        p.UserId == ban.Unban.UnbanningAdmin.Value)),
                ban.Unban?.UnbanTime);
        }

        public async Task<ServerRoleBanNoteRecord?> GetServerRoleBanAsNoteAsync(int id)
        {
            await using var db = await GetDb();

            var ban = await db.DbContext.RoleBan
                .Include(ban => ban.Unban)
                .Include(ban => ban.Round)
                .ThenInclude(r => r!.Server)
                .Include(ban => ban.CreatedBy)
                .Include(ban => ban.LastEditedBy)
                .Include(ban => ban.Unban)
                .SingleOrDefaultAsync(b => b.Id == id);

            if (ban is null)
                return null;

            var player = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == ban.PlayerUserId);
            var unbanningAdmin =
                ban.Unban is null
                ? null
                : await db.DbContext.Player.SingleOrDefaultAsync(b => b.UserId == ban.Unban.UnbanningAdmin);

            return new ServerRoleBanNoteRecord(
                ban.Id,
                MakeRoundRecord(ban.Round),
                MakePlayerRecord(player),
                ban.PlaytimeAtNote,
                ban.Reason,
                ban.Severity,
                MakePlayerRecord(ban.CreatedBy),
                ban.BanTime,
                MakePlayerRecord(ban.LastEditedBy),
                ban.LastEditedAt,
                ban.ExpirationTime,
                ban.Hidden,
                new [] { ban.RoleId.Replace(BanManager.JobPrefix, null) },
                MakePlayerRecord(unbanningAdmin),
                ban.Unban?.UnbanTime);
        }

        public async Task<List<IAdminRemarksRecord>> GetAllAdminRemarks(Guid player)
        {
            await using var db = await GetDb();
            List<IAdminRemarksRecord> notes = new();
            notes.AddRange(
                (await (from note in db.DbContext.AdminNotes
                        where note.PlayerUserId == player &&
                              !note.Deleted &&
                              (note.ExpirationTime == null || DateTime.UtcNow < note.ExpirationTime)
                        select note)
                    .Include(note => note.Round)
                    .ThenInclude(r => r!.Server)
                    .Include(note => note.CreatedBy)
                    .Include(note => note.LastEditedBy)
                    .Include(note => note.Player)
                    .ToListAsync()).Select(MakeAdminNoteRecord));
            notes.AddRange(await GetActiveWatchlistsImpl(db, player));
            notes.AddRange(await GetMessagesImpl(db, player));
            notes.AddRange(await GetServerBansAsNotesForUser(db, player));
            notes.AddRange(await GetGroupedServerRoleBansAsNotesForUser(db, player));
            return notes;
        }
        public async Task EditAdminNote(int id, string message, NoteSeverity severity, bool secret, Guid editedBy, DateTimeOffset editedAt, DateTimeOffset? expiryTime)
        {
            await using var db = await GetDb();

            var note = await db.DbContext.AdminNotes.Where(note => note.Id == id).SingleAsync();
            note.Message = message;
            note.Severity = severity;
            note.Secret = secret;
            note.LastEditedById = editedBy;
            note.LastEditedAt = editedAt.UtcDateTime;
            note.ExpirationTime = expiryTime?.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task EditAdminWatchlist(int id, string message, Guid editedBy, DateTimeOffset editedAt, DateTimeOffset? expiryTime)
        {
            await using var db = await GetDb();

            var note = await db.DbContext.AdminWatchlists.Where(note => note.Id == id).SingleAsync();
            note.Message = message;
            note.LastEditedById = editedBy;
            note.LastEditedAt = editedAt.UtcDateTime;
            note.ExpirationTime = expiryTime?.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task EditAdminMessage(int id, string message, Guid editedBy, DateTimeOffset editedAt, DateTimeOffset? expiryTime)
        {
            await using var db = await GetDb();

            var note = await db.DbContext.AdminMessages.Where(note => note.Id == id).SingleAsync();
            note.Message = message;
            note.LastEditedById = editedBy;
            note.LastEditedAt = editedAt.UtcDateTime;
            note.ExpirationTime = expiryTime?.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task DeleteAdminNote(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var note = await db.DbContext.AdminNotes.Where(note => note.Id == id).SingleAsync();

            note.Deleted = true;
            note.DeletedById = deletedBy;
            note.DeletedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task DeleteAdminWatchlist(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var watchlist = await db.DbContext.AdminWatchlists.Where(note => note.Id == id).SingleAsync();

            watchlist.Deleted = true;
            watchlist.DeletedById = deletedBy;
            watchlist.DeletedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task DeleteAdminMessage(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var message = await db.DbContext.AdminMessages.Where(note => note.Id == id).SingleAsync();

            message.Deleted = true;
            message.DeletedById = deletedBy;
            message.DeletedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task HideServerBanFromNotes(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var ban = await db.DbContext.Ban.Where(ban => ban.Id == id).SingleAsync();

            ban.Hidden = true;
            ban.LastEditedById = deletedBy;
            ban.LastEditedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task HideServerRoleBanFromNotes(int id, Guid deletedBy, DateTimeOffset deletedAt)
        {
            await using var db = await GetDb();

            var roleBan = await db.DbContext.RoleBan.Where(roleBan => roleBan.Id == id).SingleAsync();

            roleBan.Hidden = true;
            roleBan.LastEditedById = deletedBy;
            roleBan.LastEditedAt = deletedAt.UtcDateTime;

            await db.DbContext.SaveChangesAsync();
        }

        public async Task<List<IAdminRemarksRecord>> GetVisibleAdminRemarks(Guid player)
        {
            await using var db = await GetDb();
            List<IAdminRemarksRecord> notesCol = new();
            notesCol.AddRange(
                (await (from note in db.DbContext.AdminNotes
                        where note.PlayerUserId == player &&
                              !note.Secret &&
                              !note.Deleted &&
                              (note.ExpirationTime == null || DateTime.UtcNow < note.ExpirationTime)
                        select note)
                    .Include(note => note.Round)
                    .ThenInclude(r => r!.Server)
                    .Include(note => note.CreatedBy)
                    .Include(note => note.Player)
                    .ToListAsync()).Select(MakeAdminNoteRecord));
            notesCol.AddRange(await GetMessagesImpl(db, player));
            notesCol.AddRange(await GetServerBansAsNotesForUser(db, player));
            notesCol.AddRange(await GetGroupedServerRoleBansAsNotesForUser(db, player));
            return notesCol;
        }

        public async Task<List<AdminWatchlistRecord>> GetActiveWatchlists(Guid player)
        {
            await using var db = await GetDb();
            return await GetActiveWatchlistsImpl(db, player);
        }

        protected async Task<List<AdminWatchlistRecord>> GetActiveWatchlistsImpl(DbGuard db, Guid player)
        {
            var entities = await (from watchlist in db.DbContext.AdminWatchlists
                          where watchlist.PlayerUserId == player &&
                                !watchlist.Deleted &&
                                (watchlist.ExpirationTime == null || DateTime.UtcNow < watchlist.ExpirationTime)
                          select watchlist)
                .Include(note => note.Round)
                .ThenInclude(r => r!.Server)
                .Include(note => note.CreatedBy)
                .Include(note => note.LastEditedBy)
                .Include(note => note.Player)
                .ToListAsync();

            return entities.Select(MakeAdminWatchlistRecord).ToList();
        }

        private AdminWatchlistRecord MakeAdminWatchlistRecord(AdminWatchlist entity)
        {
            return new AdminWatchlistRecord(entity.Id, MakeRoundRecord(entity.Round), MakePlayerRecord(entity.Player), entity.PlaytimeAtNote, entity.Message, MakePlayerRecord(entity.CreatedBy), NormalizeDatabaseTime(entity.CreatedAt), MakePlayerRecord(entity.LastEditedBy), NormalizeDatabaseTime(entity.LastEditedAt), NormalizeDatabaseTime(entity.ExpirationTime), entity.Deleted, MakePlayerRecord(entity.DeletedBy), NormalizeDatabaseTime(entity.DeletedAt));
        }

        public async Task<List<AdminMessageRecord>> GetMessages(Guid player)
        {
            await using var db = await GetDb();
            return await GetMessagesImpl(db, player);
        }

        protected async Task<List<AdminMessageRecord>> GetMessagesImpl(DbGuard db, Guid player)
        {
            var entities = await (from message in db.DbContext.AdminMessages
                        where message.PlayerUserId == player && !message.Deleted &&
                              (message.ExpirationTime == null || DateTime.UtcNow < message.ExpirationTime)
                        select message).Include(note => note.Round)
                    .ThenInclude(r => r!.Server)
                    .Include(note => note.CreatedBy)
                    .Include(note => note.LastEditedBy)
                    .Include(note => note.Player)
                    .ToListAsync();

            return entities.Select(MakeAdminMessageRecord).ToList();
        }

        public async Task MarkMessageAsSeen(int id, bool dismissedToo)
        {
            await using var db = await GetDb();
            var message = await db.DbContext.AdminMessages.SingleAsync(m => m.Id == id);
            message.Seen = true;
            if (dismissedToo)
                message.Dismissed = true;
            await db.DbContext.SaveChangesAsync();
        }

        // These two are here because they get converted into notes later
        protected async Task<List<ServerBanNoteRecord>> GetServerBansAsNotesForUser(DbGuard db, Guid user)
        {
            // You can't group queries, as player will not always exist. When it doesn't, the
            // whole query returns nothing
            var player = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == user);
            var bans = await db.DbContext.Ban
                .Where(ban => ban.PlayerUserId == user && !ban.Hidden)
                .Include(ban => ban.Unban)
                .Include(ban => ban.Round)
                .ThenInclude(r => r!.Server)
                .Include(ban => ban.CreatedBy)
                .Include(ban => ban.LastEditedBy)
                .Include(ban => ban.Unban)
                .ToArrayAsync();

            var banNotes = new List<ServerBanNoteRecord>();
            foreach (var ban in bans)
            {
                var banNote = new ServerBanNoteRecord(
                    ban.Id,
                    MakeRoundRecord(ban.Round),
                    MakePlayerRecord(player),
                    ban.PlaytimeAtNote,
                    ban.Reason,
                    ban.Severity,
                    MakePlayerRecord(ban.CreatedBy),
                    NormalizeDatabaseTime(ban.BanTime),
                    MakePlayerRecord(ban.LastEditedBy),
                    NormalizeDatabaseTime(ban.LastEditedAt),
                    NormalizeDatabaseTime(ban.ExpirationTime),
                    ban.Hidden,
                    MakePlayerRecord(ban.Unban?.UnbanningAdmin == null
                        ? null
                        : await db.DbContext.Player.SingleOrDefaultAsync(
                            p => p.UserId == ban.Unban.UnbanningAdmin.Value)),
                    NormalizeDatabaseTime(ban.Unban?.UnbanTime));

                banNotes.Add(banNote);
            }

            return banNotes;
        }

        protected async Task<List<ServerRoleBanNoteRecord>> GetGroupedServerRoleBansAsNotesForUser(DbGuard db, Guid user)
        {
            // Server side query
            var bansQuery = await db.DbContext.RoleBan
                .Where(ban => ban.PlayerUserId == user && !ban.Hidden)
                .Include(ban => ban.Unban)
                .Include(ban => ban.Round)
                .ThenInclude(r => r!.Server)
                .Include(ban => ban.CreatedBy)
                .Include(ban => ban.LastEditedBy)
                .Include(ban => ban.Unban)
                .ToArrayAsync();

            // Client side query, as EF can't do groups yet
            var bansEnumerable = bansQuery
                    .GroupBy(ban => new { ban.BanTime, CreatedBy = (Player?)ban.CreatedBy, ban.Reason, Unbanned = ban.Unban == null })
                    .Select(banGroup => banGroup)
                    .ToArray();

            List<ServerRoleBanNoteRecord> bans = new();
            var player = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == user);
            foreach (var banGroup in bansEnumerable)
            {
                var firstBan = banGroup.First();
                Player? unbanningAdmin = null;

                if (firstBan.Unban?.UnbanningAdmin is not null)
                    unbanningAdmin = await db.DbContext.Player.SingleOrDefaultAsync(p => p.UserId == firstBan.Unban.UnbanningAdmin.Value);

                bans.Add(new ServerRoleBanNoteRecord(
                    firstBan.Id,
                    MakeRoundRecord(firstBan.Round),
                    MakePlayerRecord(player),
                    firstBan.PlaytimeAtNote,
                    firstBan.Reason,
                    firstBan.Severity,
                    MakePlayerRecord(firstBan.CreatedBy),
                    NormalizeDatabaseTime(firstBan.BanTime),
                    MakePlayerRecord(firstBan.LastEditedBy),
                    NormalizeDatabaseTime(firstBan.LastEditedAt),
                    NormalizeDatabaseTime(firstBan.ExpirationTime),
                    firstBan.Hidden,
                    banGroup.Select(ban => ban.RoleId.Replace(BanManager.JobPrefix, null)).ToArray(),
                    MakePlayerRecord(unbanningAdmin),
                    NormalizeDatabaseTime(firstBan.Unban?.UnbanTime)));
            }

            return bans;
        }

        #endregion

        #region Job Whitelists

        public async Task<bool> AddJobWhitelist(Guid player, ProtoId<JobPrototype> job)
        {
            await using var db = await GetDb();
            var exists = await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == job.Id)
                .AnyAsync();

            if (exists)
                return false;

            var whitelist = new RoleWhitelist
            {
                PlayerUserId = player,
                RoleId = job
            };
            db.DbContext.RoleWhitelists.Add(whitelist);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        public async Task<List<string>> GetJobWhitelists(Guid player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Select(w => w.RoleId)
                .ToListAsync(cancellationToken: cancel);
        }

        public async Task<bool> IsJobWhitelisted(Guid player, ProtoId<JobPrototype> job)
        {
            await using var db = await GetDb();
            return await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == job.Id)
                .AnyAsync();
        }

        public async Task<bool> RemoveJobWhitelist(Guid player, ProtoId<JobPrototype> job)
        {
            await using var db = await GetDb();
            var entry = await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == job.Id)
                .SingleOrDefaultAsync();

            if (entry == null)
                return false;

            db.DbContext.RoleWhitelists.Remove(entry);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        // Frontier: Ghost role handling
        # endregion

        # region Ghost Role Whitelists

        public async Task<bool> AddGhostRoleWhitelist(Guid player, ProtoId<GhostRolePrototype> ghostRole)
        {
            await using var db = await GetDb();
            var exists = await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == ghostRole.Id)
                .AnyAsync();

            if (exists)
                return false;

            var whitelist = new RoleWhitelist
            {
                PlayerUserId = player,
                RoleId = ghostRole
            };
            db.DbContext.RoleWhitelists.Add(whitelist);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> IsGhostRoleWhitelisted(Guid player, ProtoId<GhostRolePrototype> ghostRole)
        {
            await using var db = await GetDb();
            return await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == ghostRole.Id)
                .AnyAsync();
        }

        public async Task<bool> RemoveGhostRoleWhitelist(Guid player, ProtoId<GhostRolePrototype> ghostRole)
        {
            await using var db = await GetDb();
            var entry = await db.DbContext.RoleWhitelists
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.RoleId == ghostRole.Id)
                .SingleOrDefaultAsync();

            if (entry == null)
                return false;

            db.DbContext.RoleWhitelists.Remove(entry);
            await db.DbContext.SaveChangesAsync();
            return true;
        }
        // End Frontier: Ghost role handling

        #endregion

        # region IPIntel

        public async Task<bool> UpsertIPIntelCache(DateTime time, IPAddress ip, float score)
        {
            while (true)
            {
                try
                {
                    await using var db = await GetDb();

                    var existing = await db.DbContext.IPIntelCache
                        .Where(w => ip.Equals(w.Address))
                        .SingleOrDefaultAsync();

                    if (existing == null)
                    {
                        var newCache = new IPIntelCache
                        {
                            Time = time,
                            Address = ip,
                            Score = score,
                        };
                        db.DbContext.IPIntelCache.Add(newCache);
                    }
                    else
                    {
                        existing.Time = time;
                        existing.Score = score;
                    }

                    await Task.Delay(5000);

                    await db.DbContext.SaveChangesAsync();
                    return true;
                }
                catch (DbUpdateException)
                {
                    _opsLog.Warning("IPIntel UPSERT failed with a db exception... retrying.");
                }
            }
        }

        public async Task<IPIntelCache?> GetIPIntelCache(IPAddress ip)
        {
            await using var db = await GetDb();

            return await db.DbContext.IPIntelCache
                .SingleOrDefaultAsync(w => ip.Equals(w.Address));
        }

        public async Task<bool> CleanIPIntelCache(TimeSpan range)
        {
            await using var db = await GetDb();

            // Calculating this here cause otherwise sqlite whines.
            var cutoffTime = DateTime.UtcNow.Subtract(range);

            await db.DbContext.IPIntelCache
                .Where(w => w.Time <= cutoffTime)
                .ExecuteDeleteAsync();

            await db.DbContext.SaveChangesAsync();
            return true;
        }

        #endregion

        // WOLFGATE(Genitals) START: consent system ported from HardLight
        #region Consent Settings

        private static async Task DeletePlayerConsentSettings(ServerDbContext db, NetUserId userId)
        {
            var consentSettings = await db.ConsentSettings
                .Where(c => c.UserId == userId.UserId)
                .SingleOrDefaultAsync();

            if (consentSettings is null)
                return;

            db.ConsentSettings.Remove(consentSettings);
        }

        public async Task<int> SavePlayerConsentSettingsAsync(NetUserId userId, PlayerConsentSettings? consentSettings)
        {
            await using var db = await GetDb();

            if (consentSettings is null)
            {
                await DeletePlayerConsentSettings(db.DbContext, userId);
                await db.DbContext.SaveChangesAsync();
                return 0;
            }

            // Get current consent settings so we know if freetext needs updating and which toggles to add or remove
            var currentConsentSettings = await db.DbContext.ConsentSettings
                .Include(c => c.ConsentToggles)
                .AsSplitQuery()
                .SingleOrDefaultAsync(c => c.UserId == userId);

            if (currentConsentSettings is null)
            {
                currentConsentSettings = new ConsentSettings
                {
                    UserId = userId,
                    ConsentToggles = new(),
                    ConsentFreetext = consentSettings.Freetext,
                    ConsentFreetextUpdatedAt = DateTime.UtcNow,
                };

                db.DbContext.ConsentSettings.Add(currentConsentSettings);
            }
            else if (currentConsentSettings.ConsentFreetext != consentSettings.Freetext)
            {
                currentConsentSettings.ConsentFreetext = consentSettings.Freetext;
                currentConsentSettings.ConsentFreetextUpdatedAt = DateTime.UtcNow;
            }

            var currentConsentToggles = currentConsentSettings.ConsentToggles.ToDictionary(
                keySelector: t => new ProtoId<ConsentTogglePrototype>(t.ToggleProtoId),
                elementSelector: t => t.ToggleProtoState);

            // Remove and update toggles
            foreach (var toggle in currentConsentToggles)
            {
                if (consentSettings.Toggles.TryGetValue(toggle.Key, out var toggleState))
                    currentConsentSettings.ConsentToggles.First(t => t.ToggleProtoId == toggle.Key).ToggleProtoState = toggleState;
                else
                    currentConsentSettings.ConsentToggles.RemoveAll(t => t.ToggleProtoId == toggle.Key);
            }

            // Add new toggles
            foreach (var toggle in consentSettings.Toggles)
            {
                if (currentConsentToggles.ContainsKey(toggle.Key))
                    continue;

                currentConsentSettings.ConsentToggles.Add(new()
                {
                    ToggleProtoId = toggle.Key,
                    ToggleProtoState = toggle.Value,
                });
            }

            await db.DbContext.SaveChangesAsync();
            return currentConsentSettings.Id;
        }

        public async Task<ConsentSettings> GetPlayerConsentSettingsAsync(NetUserId userId)
        {
            await using var db = await GetDb();

            var consentSettings = await db.DbContext.ConsentSettings
                .Include(c => c.ConsentToggles)
                .Include(c => c.ReadReceipts)
                .AsSplitQuery()
                .SingleOrDefaultAsync(c => c.UserId == userId);

            // WOLFGATE(Prototypes): consent rows holding renamed toggle ids are saved under the current ids on load
            if (consentSettings != null && WFLegacyDbRows.Update(consentSettings))
                await WFLegacyDbRows.Save(db.DbContext, _opsLog);

            return consentSettings ?? new();
        }

        public async Task<ConsentFreetextReadReceipt> UpdatePlayerConsentReadReceipt(NetUserId readerUserId, int readConsentSettingsId)
        {
            await using var db = await GetDb();

            var readReceipt = await db.DbContext.ConsentFreetextReadReceipt
                .SingleOrDefaultAsync(c => c.ReaderUserId == readerUserId && c.ReadConsentSettingsId == readConsentSettingsId);

            if (readReceipt is null)
            {
                readReceipt = new ConsentFreetextReadReceipt
                {
                    ReaderUserId = readerUserId,
                    ReadConsentSettingsId = readConsentSettingsId,
                    ReadAt = DateTime.UtcNow,
                };

                // WOLFGATE(Genitals): persist the new receipt, which HardLight never adds or saves
                // Read tracking silently did nothing there.
                db.DbContext.ConsentFreetextReadReceipt.Add(readReceipt);
            }
            else
            {
                readReceipt.ReadAt = DateTime.UtcNow;
            }

            await db.DbContext.SaveChangesAsync();

            return readReceipt;
        }

        #endregion
        // WOLFGATE END

        // Mono
        #region Company

        public async Task<bool> AddCompanyMember(Guid player, ProtoId<CompanyPrototype> company, int characterSlot, string characterName)
        {
            await using var db = await GetDb();

            // One company per character.
            var slotTaken = await db.DbContext.CompanyMembers
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.CharacterSlot == characterSlot)
                .AnyAsync();
            if (slotTaken)
                return false;

            var member = new CompanyMember
            {
                PlayerUserId = player,
                CompanyId = company,
                CharacterSlot = characterSlot,
                CharacterName = characterName,
            };
            db.DbContext.CompanyMembers.Add(member);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        public async Task<List<string>> GetPlayerCompanies(Guid player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyMembers
                .Where(w => w.PlayerUserId == player)
                .Select(w => w.CompanyId)
                .Distinct()
                .ToListAsync(cancel);
        }

        public async Task<CompanyMemberRecord?> GetCompanyMemberForCharacter(Guid player, int characterSlot, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var member = await db.DbContext.CompanyMembers
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.CharacterSlot == characterSlot)
                .Include(c => c.Player)
                .FirstOrDefaultAsync(cancel);

            if (member == null)
                return null;

            return new CompanyMemberRecord
            {
                Company = member.CompanyId,
                Owner = member.Owner,
                PlayerUserId = member.PlayerUserId,
                CharacterSlot = member.CharacterSlot,
                CharacterName = member.CharacterName,
                LastSeenUserName = member.Player.LastSeenUserName,
                RoleId = member.RoleId,
            };
        }

        public async Task<IEnumerable<CompanyMemberRecord>> GetCompanyMembers(ProtoId<CompanyPrototype> company, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            var members = await db.DbContext.CompanyMembers
                .Where(w => w.CompanyId == company.Id)
                .Include(c => c.Player)
                .ToListAsync(cancel);

            return members.Select(m => new CompanyMemberRecord()
            {
                Company = company,
                Owner = m.Owner,
                PlayerUserId = m.PlayerUserId,
                CharacterSlot = m.CharacterSlot,
                CharacterName = m.CharacterName,
                LastSeenUserName = m.Player.LastSeenUserName,
                RoleId = m.RoleId,
            });
        }

        public async Task<IEnumerable<CompanyMemberRecord>> GetAllCompanyMembers(CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            var members = await db.DbContext.CompanyMembers
                .Include(c => c.Player)
                .ToListAsync(cancel);

            return members.Select(m => new CompanyMemberRecord()
            {
                Company = m.CompanyId,
                Owner = m.Owner,
                PlayerUserId = m.PlayerUserId,
                CharacterSlot = m.CharacterSlot,
                CharacterName = m.CharacterName,
                LastSeenUserName = m.Player.LastSeenUserName,
                RoleId = m.RoleId,
            });
        }

        public async Task<CompanyMemberRecord?> GetCompanyMember(ProtoId<CompanyPrototype> company, Guid player, CancellationToken cancel)
        {
            await using var db = await GetDb(cancel);
            var member = await db.DbContext.CompanyMembers
                .Where(w => w.CompanyId == company.Id)
                .Where(w => w.PlayerUserId == player)
                .Include(c => c.Player)
                .FirstOrDefaultAsync(cancel);

            if (member == null)
                return null;

            return new CompanyMemberRecord()
            {
                Company = company,
                LastSeenUserName = member.Player.LastSeenUserName,
                Owner = member.Owner,
                PlayerUserId = member.PlayerUserId,
                CharacterSlot = member.CharacterSlot,
                CharacterName = member.CharacterName,
                RoleId = member.RoleId,
            };
        }

        public async Task SetCompanyOwner(ProtoId<CompanyPrototype> company, Guid player, int characterSlot, bool owner)
        {
            await using var db = await GetDb();

            if (owner)
            {
                // Only one leader character per company; clear other owners first.
                await db.DbContext.CompanyMembers
                    .Where(w => w.CompanyId == company.Id && w.Owner)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Owner, false));
            }

            await db.DbContext.CompanyMembers
                .Where(w => w.CompanyId == company.Id)
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.CharacterSlot == characterSlot)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Owner, owner));
        }

        public async Task SetCompanyMemberRole(ProtoId<CompanyPrototype> company, Guid player, int characterSlot, Guid? roleId)
        {
            await using var db = await GetDb();
            await db.DbContext.CompanyMembers
                .Where(w => w.CompanyId == company.Id)
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.CharacterSlot == characterSlot)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.RoleId, roleId));
        }

        public async Task<bool> RemoveCompanyMember(Guid player, ProtoId<CompanyPrototype> company, int? characterSlot = null)
        {
            await using var db = await GetDb();
            var query = db.DbContext.CompanyMembers
                .Where(w => w.PlayerUserId == player)
                .Where(w => w.CompanyId == company.Id);

            if (characterSlot != null)
                query = query.Where(w => w.CharacterSlot == characterSlot);

            var entries = await query.ToListAsync();
            if (entries.Count == 0)
                return false;

            db.DbContext.CompanyMembers.RemoveRange(entries);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        // Forge: company organization
        public async Task<List<CompanyRole>> GetCompanyRoles(string companyId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyRoles
                .Where(r => r.CompanyId == companyId)
                .ToListAsync(cancel);
        }

        public async Task<CompanyRole?> GetCompanyRole(Guid roleId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyRoles.FirstOrDefaultAsync(r => r.Id == roleId, cancel);
        }

        public async Task<CompanyRole> UpsertCompanyRole(CompanyRole role)
        {
            await using var db = await GetDb();
            var existing = await db.DbContext.CompanyRoles.FirstOrDefaultAsync(r => r.Id == role.Id);
            if (existing == null)
            {
                db.DbContext.CompanyRoles.Add(role);
            }
            else
            {
                existing.Name = role.Name;
                existing.Permissions = role.Permissions;
                existing.AccessTier = role.AccessTier;
                existing.IsDefault = role.IsDefault;
            }

            await db.DbContext.SaveChangesAsync();
            return role;
        }

        public async Task<bool> DeleteCompanyRole(Guid roleId)
        {
            await using var db = await GetDb();
            var role = await db.DbContext.CompanyRoles.FirstOrDefaultAsync(r => r.Id == roleId);
            if (role == null)
                return false;

            // Clear role from members
            await db.DbContext.CompanyMembers
                .Where(m => m.RoleId == roleId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.RoleId, (Guid?)null));

            db.DbContext.CompanyRoles.Remove(role);
            await db.DbContext.SaveChangesAsync();
            return true;
        }

        public async Task EnsureDefaultCompanyRole(string companyId)
        {
            await using var db = await GetDb();
            var roles = await db.DbContext.CompanyRoles
                .Where(r => r.CompanyId == companyId)
                .ToListAsync();
            var changed = false;

            foreach (var preset in Content.Shared._Forge.Company.CompanyRolePresets.All)
            {
                var role = roles.FirstOrDefault(r => r.Name == preset.Name);
                if (role == null && preset.Leader)
                    role = roles.FirstOrDefault(r => r.Name is "Лидер" or "Leader");
                if (role == null && preset.Default)
                    role = roles.FirstOrDefault(r => r.IsDefault || r.Name == "Member");

                if (role == null)
                {
                    role = new CompanyRole
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        Name = preset.Name,
                        Permissions = (long)preset.Permissions,
                        AccessTier = (int)preset.Tier,
                        IsDefault = preset.Default,
                    };
                    db.DbContext.CompanyRoles.Add(role);
                    roles.Add(role);
                    changed = true;
                    continue;
                }

                // Adopt a legacy name once. Later edits of a preset keep their flags.
                if (role.Name != preset.Name)
                {
                    role.Name = preset.Name;
                    role.Permissions = (long)preset.Permissions;
                    role.AccessTier = (int)preset.Tier;
                    role.IsDefault = preset.Default;
                    changed = true;
                }
                else
                {
                    // A stock preset that is only missing the safe flag picks it up.
                    // A role someone already edited is left alone.
                    var updated = (long)preset.Permissions;
                    var missing = updated & ~role.Permissions;
                    var extra = role.Permissions & ~updated;
                    if (extra == 0 && missing == (long)Content.Shared._Forge.Company.CompanyPermission.Vault)
                    {
                        role.Permissions = updated;
                        changed = true;
                    }
                }
            }

            foreach (var role in roles)
            {
                if (!Content.Shared._Forge.Company.CompanyRolePresets.IsPreset(role.Name))
                    continue;

                var shouldDefault = role.Name == Content.Shared._Forge.Company.CompanyRolePresets.Member;
                if (role.IsDefault == shouldDefault)
                    continue;

                role.IsDefault = shouldDefault;
                changed = true;
            }

            if (changed)
                await db.DbContext.SaveChangesAsync();
        }

        public async Task<int> GetCompanyBankBalance(string companyId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var account = await db.DbContext.CompanyBankAccounts
                .FirstOrDefaultAsync(a => a.CompanyId == companyId, cancel);
            return account?.Balance ?? 0;
        }

        public async Task<Dictionary<string, int>> GetAllCompanyBankBalances(CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyBankAccounts
                .ToDictionaryAsync(a => a.CompanyId, a => a.Balance, cancel);
        }

        public async Task SetCompanyBankBalance(string companyId, int balance)
        {
            await using var db = await GetDb();
            var account = await db.DbContext.CompanyBankAccounts
                .FirstOrDefaultAsync(a => a.CompanyId == companyId);
            if (account == null)
            {
                db.DbContext.CompanyBankAccounts.Add(new CompanyBankAccount
                {
                    CompanyId = companyId,
                    Balance = balance,
                });
            }
            else
            {
                account.Balance = balance;
            }

            await db.DbContext.SaveChangesAsync();
        }

        public async Task EnsureCompanyBankAccount(string companyId)
        {
            await using var db = await GetDb();
            if (await db.DbContext.CompanyBankAccounts.AnyAsync(a => a.CompanyId == companyId))
                return;

            db.DbContext.CompanyBankAccounts.Add(new CompanyBankAccount
            {
                CompanyId = companyId,
                Balance = 0,
            });
            await db.DbContext.SaveChangesAsync();
        }

        public async Task<List<CompanyRelation>> GetCompanyRelations(string companyId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyRelations
                .Where(r => r.CompanyAId == companyId || r.CompanyBId == companyId)
                .ToListAsync(cancel);
        }

        public async Task SetCompanyRelation(string companyA, string companyB, int relationType)
        {
            // Canonicalize pair order
            if (string.CompareOrdinal(companyA, companyB) > 0)
                (companyA, companyB) = (companyB, companyA);

            await using var db = await GetDb();
            var existing = await db.DbContext.CompanyRelations
                .FirstOrDefaultAsync(r => r.CompanyAId == companyA && r.CompanyBId == companyB);
            if (existing == null)
            {
                db.DbContext.CompanyRelations.Add(new CompanyRelation
                {
                    CompanyAId = companyA,
                    CompanyBId = companyB,
                    RelationType = relationType,
                });
            }
            else
            {
                existing.RelationType = relationType;
            }

            await db.DbContext.SaveChangesAsync();
        }

        public async Task ClearCompanyRelation(string companyA, string companyB)
        {
            if (string.CompareOrdinal(companyA, companyB) > 0)
                (companyA, companyB) = (companyB, companyA);

            await using var db = await GetDb();
            var existing = await db.DbContext.CompanyRelations
                .FirstOrDefaultAsync(r => r.CompanyAId == companyA && r.CompanyBId == companyB);
            if (existing == null)
                return;

            db.DbContext.CompanyRelations.Remove(existing);
            await db.DbContext.SaveChangesAsync();
        }

        public async Task<CompanyInvitation> AddCompanyInvitation(CompanyInvitation invite)
        {
            await using var db = await GetDb();
            db.DbContext.CompanyInvitations.Add(invite);
            await db.DbContext.SaveChangesAsync();
            return invite;
        }

        public async Task<List<CompanyInvitation>> GetPendingInvitationsForPlayer(Guid player, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyInvitations
                .Include(i => i.FromPlayer)
                .Include(i => i.TargetPlayer)
                .Where(i => i.TargetUserId == player && i.Status == 0)
                .ToListAsync(cancel);
        }

        public async Task<List<CompanyInvitation>> GetPendingInvitationsForCompany(string companyId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyInvitations
                .Include(i => i.FromPlayer)
                .Include(i => i.TargetPlayer)
                .Where(i => i.CompanyId == companyId && i.Status == 0)
                .ToListAsync(cancel);
        }

        public async Task<CompanyInvitation?> GetCompanyInvitation(Guid inviteId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyInvitations
                .Include(i => i.FromPlayer)
                .Include(i => i.TargetPlayer)
                .FirstOrDefaultAsync(i => i.Id == inviteId, cancel);
        }

        public async Task SetCompanyInvitationStatus(Guid inviteId, int status)
        {
            await using var db = await GetDb();
            await db.DbContext.CompanyInvitations
                .Where(i => i.Id == inviteId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, status));
        }

        public async Task<bool> DeleteCompanyInvitation(Guid inviteId)
        {
            await using var db = await GetDb();
            var removed = await db.DbContext.CompanyInvitations
                .Where(i => i.Id == inviteId)
                .ExecuteDeleteAsync();
            return removed > 0;
        }

        /// <summary>
        /// Removes non-pending invitation rows left from older builds.
        /// </summary>
        public async Task<int> PurgeResolvedCompanyInvitations(CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyInvitations
                .Where(i => i.Status != 0)
                .ExecuteDeleteAsync(cancel);
        }

        public async Task<bool> HasPendingCompanyInvitation(string companyId, Guid targetUserId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyInvitations.AnyAsync(
                i => i.CompanyId == companyId && i.TargetUserId == targetUserId && i.Status == 0,
                cancel);
        }

        public async Task<int> CountPendingInvitationsForCompany(string companyId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyInvitations.CountAsync(
                i => i.CompanyId == companyId && i.Status == 0,
                cancel);
        }

        public async Task AddCompanyLog(CompanyLog log, int keep = CompanyOrgLimits.MaxLogsPerCompany)
        {
            if (log.Message.Length > CompanyOrgLimits.MaxLogMessageLength)
                log.Message = log.Message[..CompanyOrgLimits.MaxLogMessageLength];

            await using var db = await GetDb();
            db.DbContext.CompanyLogs.Add(log);
            await db.DbContext.SaveChangesAsync();

            if (keep <= 0)
                return;

            var excessIds = await db.DbContext.CompanyLogs
                .Where(l => l.CompanyId == log.CompanyId)
                .OrderByDescending(l => l.Timestamp)
                .ThenByDescending(l => l.Id)
                .Skip(keep)
                .Select(l => l.Id)
                .ToListAsync();

            if (excessIds.Count == 0)
                return;

            await db.DbContext.CompanyLogs
                .Where(l => excessIds.Contains(l.Id))
                .ExecuteDeleteAsync();
        }

        public async Task<List<CompanyLog>> GetCompanyLogs(string companyId, int limit = 100, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            return await db.DbContext.CompanyLogs
                .Where(l => l.CompanyId == companyId)
                .OrderByDescending(l => l.Timestamp)
                .Take(limit)
                .ToListAsync(cancel);
        }

        public async Task<string> GetCompanyBulletin(string companyId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            var bulletin = await db.DbContext.CompanyBulletins
                .FirstOrDefaultAsync(b => b.CompanyId == companyId, cancel);
            return bulletin?.Text ?? string.Empty;
        }

        public async Task SetCompanyBulletin(string companyId, string text)
        {
            await using var db = await GetDb();
            var bulletin = await db.DbContext.CompanyBulletins
                .FirstOrDefaultAsync(b => b.CompanyId == companyId);
            if (bulletin == null)
            {
                db.DbContext.CompanyBulletins.Add(new CompanyBulletin
                {
                    CompanyId = companyId,
                    Text = text,
                });
            }
            else
            {
                bulletin.Text = text;
            }

            await db.DbContext.SaveChangesAsync();
        }

        public async Task EnsureCompanyDevelopmentTable()
        {
            await using var db = await GetDb();
            await EnsureDevelopmentTable(db);
        }

        public async Task<CompanyDevelopmentRecord> GetCompanyDevelopment(string companyId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            await EnsureDevelopmentTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(cancel);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT projects, contract_id, contract_funded, research_points FROM company_developments WHERE company_id = @id";
            AddParam(cmd, "@id", companyId);

            await using var reader = await cmd.ExecuteReaderAsync(cancel);
            if (!await reader.ReadAsync(cancel))
                return new CompanyDevelopmentRecord();

            return new CompanyDevelopmentRecord
            {
                Projects = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                ContractId = reader.IsDBNull(1) ? null : reader.GetString(1),
                ContractFunded = !reader.IsDBNull(2) && Convert.ToInt32(reader.GetValue(2)) != 0,
                ResearchPoints = reader.IsDBNull(3) ? 0 : Convert.ToInt64(reader.GetValue(3)),
            };
        }

        public async Task SetCompanyDevelopment(string companyId, string projects, string? contractId, bool contractFunded)
        {
            await using var db = await GetDb();
            await EnsureDevelopmentTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO company_developments (company_id, projects, contract_id, contract_funded)
                VALUES (@id, @projects, @contract, @funded)
                ON CONFLICT (company_id) DO UPDATE SET
                    projects = @projects,
                    contract_id = @contract,
                    contract_funded = @funded
                """;
            AddParam(cmd, "@id", companyId);
            AddParam(cmd, "@projects", projects);
            AddParam(cmd, "@contract", (object?)contractId ?? DBNull.Value);
            AddParam(cmd, "@funded", contractFunded ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task AddCompanyResearchPoints(string companyId, long amount)
        {
            if (amount <= 0)
                return;

            await using var db = await GetDb();
            await EnsureDevelopmentTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO company_developments (company_id, projects, research_points)
                VALUES (@id, '', @amount)
                ON CONFLICT (company_id) DO UPDATE SET
                    research_points = company_developments.research_points + @amount
                """;
            AddParam(cmd, "@id", companyId);
            AddParam(cmd, "@amount", amount);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task EnsureCompanyPeaceTable()
        {
            await using var db = await GetDb();
            await EnsurePeaceTable(db);
        }

        public async Task<List<CompanyPeaceTermRecord>> GetCompanyPeaceTerms(CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            await EnsurePeaceTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(cancel);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT company_id, other_id, free_offer, ransom_percent FROM company_peace_terms";

            var result = new List<CompanyPeaceTermRecord>();
            await using var reader = await cmd.ExecuteReaderAsync(cancel);
            while (await reader.ReadAsync(cancel))
            {
                result.Add(new CompanyPeaceTermRecord
                {
                    CompanyId = reader.GetString(0),
                    OtherId = reader.GetString(1),
                    FreeOffer = !reader.IsDBNull(2) && Convert.ToInt32(reader.GetValue(2)) != 0,
                    RansomPercent = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)),
                });
            }

            return result;
        }

        public async Task UpsertCompanyPeaceTerm(string companyId, string otherId, bool freeOffer, int ransomPercent)
        {
            await using var db = await GetDb();
            await EnsurePeaceTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO company_peace_terms (company_id, other_id, free_offer, ransom_percent)
                VALUES (@company, @other, @free, @percent)
                ON CONFLICT (company_id, other_id) DO UPDATE SET
                    free_offer = @free,
                    ransom_percent = @percent
                """;
            AddParam(cmd, "@company", companyId);
            AddParam(cmd, "@other", otherId);
            AddParam(cmd, "@free", freeOffer ? 1 : 0);
            AddParam(cmd, "@percent", ransomPercent);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteCompanyPeaceTerms(string companyA, string companyB)
        {
            await using var db = await GetDb();
            await EnsurePeaceTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DELETE FROM company_peace_terms
                WHERE (company_id = @a AND other_id = @b)
                   OR (company_id = @b AND other_id = @a)
                """;
            AddParam(cmd, "@a", companyA);
            AddParam(cmd, "@b", companyB);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task EnsureCompanyAllianceTable()
        {
            await using var db = await GetDb();
            await EnsureAllianceTable(db);
        }

        public async Task<List<CompanyAllianceOfferRecord>> GetCompanyAllianceOffers(CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            await EnsureAllianceTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(cancel);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT company_id, other_id FROM company_alliance_offers";

            var result = new List<CompanyAllianceOfferRecord>();
            await using var reader = await cmd.ExecuteReaderAsync(cancel);
            while (await reader.ReadAsync(cancel))
            {
                result.Add(new CompanyAllianceOfferRecord
                {
                    CompanyId = reader.GetString(0),
                    OtherId = reader.GetString(1),
                });
            }

            return result;
        }

        public async Task AddCompanyAllianceOffer(string companyId, string otherId)
        {
            await using var db = await GetDb();
            await EnsureAllianceTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO company_alliance_offers (company_id, other_id)
                VALUES (@company, @other)
                ON CONFLICT (company_id, other_id) DO NOTHING
                """;
            AddParam(cmd, "@company", companyId);
            AddParam(cmd, "@other", otherId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteCompanyAllianceOffer(string companyId, string otherId)
        {
            await using var db = await GetDb();
            await EnsureAllianceTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DELETE FROM company_alliance_offers
                WHERE company_id = @company AND other_id = @other
                """;
            AddParam(cmd, "@company", companyId);
            AddParam(cmd, "@other", otherId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteCompanyAlliancePair(string companyA, string companyB)
        {
            await using var db = await GetDb();
            await EnsureAllianceTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DELETE FROM company_alliance_offers
                WHERE (company_id = @a AND other_id = @b)
                   OR (company_id = @b AND other_id = @a)
                """;
            AddParam(cmd, "@a", companyA);
            AddParam(cmd, "@b", companyB);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task EnsureCompanyAllyWarTable()
        {
            await using var db = await GetDb();
            await EnsureAllyWarTable(db);
        }

        public async Task<List<CompanyAllyWarRecord>> GetCompanyAllyWars(CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            await EnsureAllyWarTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(cancel);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT company_id, auto_join FROM company_ally_wars";

            var result = new List<CompanyAllyWarRecord>();
            await using var reader = await cmd.ExecuteReaderAsync(cancel);
            while (await reader.ReadAsync(cancel))
            {
                result.Add(new CompanyAllyWarRecord
                {
                    CompanyId = reader.GetString(0),
                    AutoJoin = !reader.IsDBNull(1) && Convert.ToInt32(reader.GetValue(1)) != 0,
                });
            }

            return result;
        }

        public async Task SetCompanyAllyWar(string companyId, bool autoJoin)
        {
            await using var db = await GetDb();
            await EnsureAllyWarTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO company_ally_wars (company_id, auto_join)
                VALUES (@id, @join)
                ON CONFLICT (company_id) DO UPDATE SET
                    auto_join = @join
                """;
            AddParam(cmd, "@id", companyId);
            AddParam(cmd, "@join", autoJoin ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task EnsureCompanyVaultTable()
        {
            await using var db = await GetDb();
            await EnsureVaultTable(db);
        }

        public async Task<CompanyVaultRecord> GetCompanyVault(string companyId, CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            await EnsureVaultTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(cancel);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT grid_width, grid_height, items FROM company_vaults WHERE company_id = @id";
            AddParam(cmd, "@id", companyId);

            await using var reader = await cmd.ExecuteReaderAsync(cancel);
            if (!await reader.ReadAsync(cancel))
                return new CompanyVaultRecord();

            var width = reader.IsDBNull(0) ? CompanyOrgLimits.VaultGrid : Convert.ToInt32(reader.GetValue(0));
            var height = reader.IsDBNull(1) ? CompanyOrgLimits.VaultGrid : Convert.ToInt32(reader.GetValue(1));
            var items = reader.IsDBNull(2) ? "[]" : reader.GetString(2);
            return new CompanyVaultRecord
            {
                Width = Math.Clamp(width, 1, CompanyOrgLimits.VaultGridMax),
                Height = Math.Clamp(height, 1, CompanyOrgLimits.VaultGridMax),
                Items = ReadVaultItems(items),
            };
        }

        public async Task SetCompanyVault(string companyId, int width, int height, IReadOnlyList<string> items)
        {
            await using var db = await GetDb();
            await EnsureVaultTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO company_vaults (company_id, grid_width, grid_height, items)
                VALUES (@id, @width, @height, @items)
                ON CONFLICT (company_id) DO UPDATE SET
                    grid_width = @width,
                    grid_height = @height,
                    items = @items
                """;
            AddParam(cmd, "@id", companyId);
            AddParam(cmd, "@width", width);
            AddParam(cmd, "@height", height);
            AddParam(cmd, "@items", System.Text.Json.JsonSerializer.Serialize(items));
            await cmd.ExecuteNonQueryAsync();
        }

        private static List<string> ReadVaultItems(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new();

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new();
            }
            catch (System.Text.Json.JsonException)
            {
                return new();
            }
        }

        private static async Task EnsureVaultTable(DbGuard db)
        {
            await db.DbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS company_vaults (
                    company_id TEXT PRIMARY KEY NOT NULL,
                    grid_width INTEGER NOT NULL DEFAULT 3,
                    grid_height INTEGER NOT NULL DEFAULT 3,
                    items TEXT NOT NULL DEFAULT '[]'
                )
                """);
        }

        private static async Task EnsurePeaceTable(DbGuard db)
        {
            await db.DbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS company_peace_terms (
                    company_id TEXT NOT NULL,
                    other_id TEXT NOT NULL,
                    free_offer INTEGER NOT NULL DEFAULT 0,
                    ransom_percent INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (company_id, other_id)
                )
                """);
        }

        private static async Task EnsureAllianceTable(DbGuard db)
        {
            await db.DbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS company_alliance_offers (
                    company_id TEXT NOT NULL,
                    other_id TEXT NOT NULL,
                    PRIMARY KEY (company_id, other_id)
                )
                """);
        }

        private static async Task EnsureAllyWarTable(DbGuard db)
        {
            await db.DbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS company_ally_wars (
                    company_id TEXT PRIMARY KEY NOT NULL,
                    auto_join INTEGER NOT NULL
                )
                """);
        }

        private static async Task EnsureDevelopmentTable(DbGuard db)
        {
            await db.DbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS company_developments (
                    company_id TEXT PRIMARY KEY NOT NULL,
                    projects TEXT NOT NULL DEFAULT '',
                    contract_id TEXT NULL,
                    contract_funded INTEGER NOT NULL DEFAULT 0,
                    research_points INTEGER NOT NULL DEFAULT 0
                )
                """);
            if (_researchColumnReady || await HasColumn(db, "company_developments", "research_points"))
            {
                _researchColumnReady = true;
                return;
            }

            await db.DbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE company_developments ADD COLUMN research_points INTEGER NOT NULL DEFAULT 0");
            _researchColumnReady = true;
        }

        private static bool _researchColumnReady;

        private static async Task<bool> HasColumn(DbGuard db, string table, string column)
        {
            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            var sqlite = conn.GetType().Name.Contains("Sqlite", StringComparison.Ordinal);
            // The table name is one of our own identifiers, not player input.
            cmd.CommandText = sqlite
                ? $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = @column"
                : """
                  SELECT 1 FROM information_schema.columns
                  WHERE table_name = @table AND column_name = @column
                  """;
            if (!sqlite)
                AddParam(cmd, "@table", table);
            AddParam(cmd, "@column", column);
            var found = await cmd.ExecuteScalarAsync();
            return found != null && found != DBNull.Value;
        }

        public async Task EnsureCompanyJobTable()
        {
            await using var db = await GetDb();
            await EnsureJobTable(db);
        }

        public async Task<List<CompanyJobRecord>> GetCompanyJobs(CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);
            await EnsureJobTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(cancel);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT job_id, poster_id, taker_id, description, reward_kind, reward_amount, reward_proto
                FROM company_jobs
                """;

            var list = new List<CompanyJobRecord>();
            await using var reader = await cmd.ExecuteReaderAsync(cancel);
            while (await reader.ReadAsync(cancel))
            {
                list.Add(new CompanyJobRecord
                {
                    Id = reader.GetString(0),
                    PosterId = reader.GetString(1),
                    TakerId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    RewardKind = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4)),
                    RewardAmount = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5)),
                    RewardProto = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                });
            }

            return list;
        }

        public async Task UpsertCompanyJob(CompanyJobRecord job)
        {
            await using var db = await GetDb();
            await EnsureJobTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO company_jobs
                    (job_id, poster_id, taker_id, description, reward_kind, reward_amount, reward_proto)
                VALUES (@id, @poster, @taker, @description, @kind, @amount, @proto)
                ON CONFLICT (job_id) DO UPDATE SET
                    taker_id = EXCLUDED.taker_id,
                    description = EXCLUDED.description,
                    reward_kind = EXCLUDED.reward_kind,
                    reward_amount = EXCLUDED.reward_amount,
                    reward_proto = EXCLUDED.reward_proto
                """;
            AddParam(cmd, "@id", job.Id);
            AddParam(cmd, "@poster", job.PosterId);
            AddParam(cmd, "@taker", (object?)job.TakerId ?? DBNull.Value);
            AddParam(cmd, "@description", job.Description);
            AddParam(cmd, "@kind", job.RewardKind);
            AddParam(cmd, "@amount", job.RewardAmount);
            AddParam(cmd, "@proto", job.RewardProto ?? string.Empty);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteCompanyJob(string jobId)
        {
            await using var db = await GetDb();
            await EnsureJobTable(db);

            var conn = db.DbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM company_jobs WHERE job_id = @id";
            AddParam(cmd, "@id", jobId);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task EnsureJobTable(DbGuard db)
        {
            await db.DbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS company_jobs (
                    job_id TEXT PRIMARY KEY NOT NULL,
                    poster_id TEXT NOT NULL,
                    taker_id TEXT NULL,
                    description TEXT NOT NULL,
                    reward_kind INTEGER NOT NULL,
                    reward_amount INTEGER NOT NULL,
                    reward_proto TEXT NOT NULL DEFAULT ''
                )
                """);
        }

        private static void AddParam(DbCommand cmd, string name, object value)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = name;
            param.Value = value;
            cmd.Parameters.Add(param);
        }

        #endregion

        public abstract Task SendNotification(DatabaseNotification notification);

        // SQLite returns DateTime as Kind=Unspecified, Npgsql actually knows for sure it's Kind=Utc.
        // Normalize DateTimes here so they're always Utc. Thanks.
        protected abstract DateTime NormalizeDatabaseTime(DateTime time);

        [return: NotNullIfNotNull(nameof(time))]
        protected DateTime? NormalizeDatabaseTime(DateTime? time)
        {
            return time != null ? NormalizeDatabaseTime(time.Value) : time;
        }

        public async Task<bool> HasPendingModelChanges()
        {
            await using var db = await GetDb();
            return db.DbContext.Database.HasPendingModelChanges();
        }

        protected abstract Task<DbGuard> GetDb(
            CancellationToken cancel = default,
            [CallerMemberName] string? name = null);

        protected void LogDbOp(string? name)
        {
            _opsLog.Verbose($"Running DB operation: {name ?? "unknown"}");
        }

        protected abstract class DbGuard : IAsyncDisposable
        {
            public abstract ServerDbContext DbContext { get; }

            public abstract ValueTask DisposeAsync();
        }

        protected void NotificationReceived(DatabaseNotification notification)
        {
            OnNotificationReceived?.Invoke(notification);
        }

        public virtual void Shutdown()
        {

        }

        #region Wayfarer Safety Deposit Box

        public async Task<WayfarerSafetyDepositBox> PurchaseSafetyDepositBox(
            Guid ownerUserId,
            int characterIndex,
            string ownerName,
            EntProtoId protoId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var box = new WayfarerSafetyDepositBox
            {
                BoxId = Guid.NewGuid(),
                OwnerUserId = ownerUserId,
                CharacterIndex = characterIndex,
                OwnerName = ownerName,
                ProtoId = protoId,
                PurchaseDate = DateTime.UtcNow
            };

            db.DbContext.WayfarerSafetyDepositBox.Add(box);
            await db.DbContext.SaveChangesAsync(cancel);

            return box;
        }

        public async Task<List<WayfarerSafetyDepositBox>> GetPlayerSafetyDepositBoxes(
            Guid ownerUserId,
            int characterIndex,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            return await db.DbContext.WayfarerSafetyDepositBox
                .Include(b => b.Items)
                .Where(b => b.OwnerUserId == ownerUserId && b.CharacterIndex == characterIndex)
                .ToListAsync(cancel);
        }

        public async Task<WayfarerSafetyDepositBox?> GetSafetyDepositBox(
            Guid boxId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            return await db.DbContext.WayfarerSafetyDepositBox
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.BoxId == boxId, cancel);
        }

        public async Task DepositSafetyDepositBoxItems(
            Guid boxId,
            List<string> entityDataList,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var box = await db.DbContext.WayfarerSafetyDepositBox
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.BoxId == boxId, cancel);

            if (box == null)
                return;

            // Clear existing items
            db.DbContext.WayfarerSafetyDepositBoxItem.RemoveRange(box.Items);

            // Add new items
            foreach (var entityData in entityDataList)
            {
                box.Items.Add(new WayfarerSafetyDepositBoxItem
                {
                    BoxId = box.Id,
                    EntityData = entityData,
                    DepositDate = DateTime.UtcNow
                });
            }

            // Clear LastWithdrawn since the box is now safely stored
            box.LastWithdrawn = null;
            box.LastWithdrawnRoundId = null;

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task UpdateSafetyDepositBoxNickname(
            Guid boxId,
            string? nickname,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var box = await db.DbContext.WayfarerSafetyDepositBox
                .FirstOrDefaultAsync(b => b.BoxId == boxId, cancel);

            if (box == null)
                return;

            box.Nickname = nickname;
            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task ClearSafetyDepositBoxItems(
            Guid boxId,
            int roundId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var box = await db.DbContext.WayfarerSafetyDepositBox
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.BoxId == boxId, cancel);

            if (box == null)
                return;

            db.DbContext.WayfarerSafetyDepositBoxItem.RemoveRange(box.Items);

            // Set LastWithdrawn to indicate the box is now in the world
            box.LastWithdrawn = DateTime.UtcNow;
            box.LastWithdrawnRoundId = roundId;

            await db.DbContext.SaveChangesAsync(cancel);
        }

        public async Task<int> DeleteStaleSafetyDepositBoxes(
            int daysStale,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var cutoffDate = DateTime.UtcNow.AddDays(-daysStale);

            // Find boxes that have been withdrawn and have no items for longer than the cutoff period
            var staleBoxes = await db.DbContext.WayfarerSafetyDepositBox
                .Include(b => b.Items)
                .Where(b => b.LastWithdrawn != null &&
                            b.LastWithdrawn < cutoffDate &&
                            b.Items.Count == 0)
                .ToListAsync(cancel);

            var count = staleBoxes.Count;
            db.DbContext.WayfarerSafetyDepositBox.RemoveRange(staleBoxes);
            await db.DbContext.SaveChangesAsync(cancel);

            return count;
        }

        public async Task DeleteSafetyDepositBox(
            Guid boxId,
            CancellationToken cancel = default)
        {
            await using var db = await GetDb(cancel);

            var box = await db.DbContext.WayfarerSafetyDepositBox
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.BoxId == boxId, cancel);

            if (box == null)
                return;

            db.DbContext.WayfarerSafetyDepositBox.Remove(box);
            await db.DbContext.SaveChangesAsync(cancel);
        }

        #endregion
    }
}

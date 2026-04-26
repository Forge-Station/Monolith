using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Collections.Concurrent; // Forge-Change
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Players.RateLimiting; // Forge-Change
using Content.Shared.CCVar;
using Content.Shared.Chat; // Forge-Change
using Content.Shared.Preferences;
using Content.Shared.Players.RateLimiting; // Forge-Change
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Preferences.Managers
{
    /// <summary>
    /// Sends <see cref="MsgPreferencesAndSettings"/> before the client joins the lobby.
    /// Receives <see cref="MsgSelectCharacter"/> and <see cref="MsgUpdateCharacter"/> at any time.
    /// </summary>
    public sealed class ServerPreferencesManager : IServerPreferencesManager, IPostInjectInit
    {
        [Dependency] private readonly IServerNetManager _netManager = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IServerDbManager _db = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IDependencyCollection _dependencies = default!;
        [Dependency] private readonly IPrototypeManager _protos = default!;
        [Dependency] private readonly ILogManager _log = default!;
        [Dependency] private readonly UserDbDataManager _userDb = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly PlayerRateLimitManager _rateLimit = default!; // Forge-Change
        [Dependency] private readonly ISharedChatManager _chat = default!; // Forge-Change

        // Cache player prefs on the server so we don't need as much async hell related to them.
        private readonly Dictionary<NetUserId, PlayerPrefData> _cachedPlayerPrefs =
            new();
        private readonly ConcurrentDictionary<(NetUserId UserId, int Slot), SemaphoreSlim> _profileSlotLocks = new(); // Forge-Change

        private ISawmill _sawmill = default!;
        private const string ProfileUpdateRateLimitKey = "ProfileUpdate"; // Forge-Change

        private int MaxCharacterSlots => _cfg.GetCVar(CCVars.GameMaxCharacterSlots);

        public void Init()
        {
            _netManager.RegisterNetMessage<MsgPreferencesAndSettings>();
            _netManager.RegisterNetMessage<MsgSelectCharacter>(HandleSelectCharacterMessage);
            _netManager.RegisterNetMessage<MsgUpdateCharacter>(HandleUpdateCharacterMessage);
            _netManager.RegisterNetMessage<MsgDeleteCharacter>(HandleDeleteCharacterMessage);
            _sawmill = _log.GetSawmill("prefs");
            // Forge-Change-start
            _rateLimit.Register(ProfileUpdateRateLimitKey,
                new RateLimitRegistration(CCVars.ProfileUpdateRateLimitPeriod,
                    CCVars.ProfileUpdateRateLimitCount,
                    ProfileUpdateRateLimited,
                    CCVars.ProfileUpdateRateLimitAnnounceAdminsDelay,
                    ProfileUpdateRateLimitAlertAdmins)
            );
            // Forge-Change-end
        }

        private async void HandleSelectCharacterMessage(MsgSelectCharacter message)
        {
            var index = message.SelectedCharacterIndex;
            var userId = message.MsgChannel.UserId;

            if (!_cachedPlayerPrefs.TryGetValue(userId, out var prefsData) || !prefsData.PrefsLoaded)
            {
                Logger.WarningS("prefs", $"User {userId} tried to modify preferences before they loaded.");
                return;
            }

            if (index < 0 || index >= MaxCharacterSlots)
            {
                return;
            }

            var curPrefs = prefsData.Prefs!;

            if (!curPrefs.Characters.ContainsKey(index))
            {
                // Non-existent slot.
                return;
            }

            prefsData.Prefs = new PlayerPreferences(curPrefs.Characters, index, curPrefs.AdminOOCColor);

            if (ShouldStorePrefs(message.MsgChannel.AuthType))
            {
                await _db.SaveSelectedCharacterIndexAsync(message.MsgChannel.UserId, message.SelectedCharacterIndex);
            }
        }

        private async void HandleUpdateCharacterMessage(MsgUpdateCharacter message)
        {
            var userId = message.MsgChannel.UserId;
            var session = _playerManager.GetSessionById(userId); // Forge-Change

            if (_rateLimit.CountAction(session, ProfileUpdateRateLimitKey) != RateLimitStatus.Allowed) // Forge-Change
                return; // Forge-Change

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (message.Profile == null)
                _sawmill.Error($"User {userId} sent a {nameof(MsgUpdateCharacter)} with a null profile in slot {message.Slot}.");
            else
                await SetProfile(userId, message.Slot, message.Profile, false);
        }

        public async Task SetProfile(NetUserId userId, int slot, ICharacterProfile profile,
            bool authoritative = true) // Mono
        {
            await RunProfileSlotLocked(userId, slot, async () => // Forge-Change
            {
                if (!_cachedPlayerPrefs.TryGetValue(userId, out var prefsData) || !prefsData.PrefsLoaded)
                {
                    _sawmill.Error($"Tried to modify user {userId} preferences before they loaded.");
                    return;
                }

                if (slot < 0 || slot >= MaxCharacterSlots)
                    return;

                var curPrefs = prefsData.Prefs!;
                var session = _playerManager.GetSessionById(userId);
                profile.EnsureValid(session, _dependencies);
                // Mono
                if (!authoritative && profile is HumanoidCharacterProfile humanoid)
                {
                    if (curPrefs.Characters.TryGetValue(slot, out var oldProfile) && oldProfile is HumanoidCharacterProfile oldHumanoid)
                        profile = humanoid.WithBankBalance(oldHumanoid.BankBalance);
                    else
                        profile = humanoid.WithBankBalance(HumanoidCharacterProfile.DefaultBalance);
                }

                // Forge-Change-Start: set increased starting bank balance for new globally whitelisted characters
                if (profile is HumanoidCharacterProfile humanoidProfile &&
                    !curPrefs.Characters.ContainsKey(slot) &&
                    humanoidProfile.BankBalance == HumanoidCharacterProfile.DefaultBalance)
                {
                    var whitelisted = await _db.GetWhitelistStatusAsync(userId);
                    if (whitelisted)
                    {
                        var startingBalance = _cfg.GetCVar(CCVars.GameWhitelistedStartingBalance);
                        profile = humanoidProfile.WithBankBalance(startingBalance);
                    }
                }


                var profiles = new Dictionary<int, ICharacterProfile>(curPrefs.Characters)
                {
                    [slot] = profile
                };

                prefsData.Prefs = new PlayerPreferences(profiles, slot, curPrefs.AdminOOCColor);

                if (ShouldStorePrefs(session.Channel.AuthType))
                    await _db.SaveCharacterSlotAsync(userId, profile, slot);
            });
             // Forge-Change-End
        }

        private async void HandleDeleteCharacterMessage(MsgDeleteCharacter message)
        {
            var slot = message.Slot;
            var userId = message.MsgChannel.UserId;
            // Forge-Change-start
            await RunProfileSlotLocked(userId, slot, async () =>
            {
                if (!_cachedPlayerPrefs.TryGetValue(userId, out var prefsData) || !prefsData.PrefsLoaded)
                {
                    Logger.WarningS("prefs", $"User {userId} tried to modify preferences before they loaded.");
                    return;
                }

                if (slot < 0 || slot >= MaxCharacterSlots)
                {
                    return;
                }

                var curPrefs = prefsData.Prefs!;

                // If they try to delete the slot they have selected then we switch to another one.
                // Of course, that's only if they HAVE another slot.
                int? nextSlot = null;
                if (curPrefs.SelectedCharacterIndex == slot)
                {
                    // That ! on the end is because Rider doesn't like .NET 5.
                    var (ns, profile) = curPrefs.Characters.FirstOrDefault(p => p.Key != message.Slot)!;
                    if (profile == null)
                    {
                        // Only slot left, can't delete.
                        return;
                    }

                    nextSlot = ns;
                }

                var arr = new Dictionary<int, ICharacterProfile>(curPrefs.Characters);
                arr.Remove(slot);

                prefsData.Prefs = new PlayerPreferences(arr, nextSlot ?? curPrefs.SelectedCharacterIndex, curPrefs.AdminOOCColor);

                if (ShouldStorePrefs(message.MsgChannel.AuthType))
                {
                    if (nextSlot != null)
                    {
                        await _db.DeleteSlotAndSetSelectedIndex(userId, slot, nextSlot.Value);
                    }
                    else
                    {
                        await _db.SaveCharacterSlotAsync(userId, null, slot);
                    }
                }
            });
            // Forge-Change-end
        }

        // Should only be called via UserDbDataManager.
        public async Task LoadData(ICommonSession session, CancellationToken cancel)
        {
            if (!ShouldStorePrefs(session.Channel.AuthType))
            {
                // Don't store data for guests.
                var prefsData = new PlayerPrefData
                {
                    PrefsLoaded = true,
                    Prefs = new PlayerPreferences(
                        new[] {new KeyValuePair<int, ICharacterProfile>(0, HumanoidCharacterProfile.Random())},
                        0, Color.Transparent)
                };

                _cachedPlayerPrefs[session.UserId] = prefsData;
            }
            else
            {
                var prefsData = new PlayerPrefData();
                var loadTask = LoadPrefs();
                _cachedPlayerPrefs[session.UserId] = prefsData;

                await loadTask;

                async Task LoadPrefs()
                {
                    var prefs = await GetOrCreatePreferencesAsync(session.UserId, cancel);
                    prefsData.Prefs = prefs;
                }
            }
        }

        public void FinishLoad(ICommonSession session)
        {
            // This is a separate step from the actual database load.
            // Sanitizing preferences requires play time info due to loadouts.
            // And play time info is loaded concurrently from the DB with preferences.
            var prefsData = _cachedPlayerPrefs[session.UserId];
            DebugTools.Assert(prefsData.Prefs != null);
            prefsData.Prefs = SanitizePreferences(session, prefsData.Prefs, _dependencies);

            prefsData.PrefsLoaded = true;

            var msg = new MsgPreferencesAndSettings();
            msg.Preferences = prefsData.Prefs;
            msg.Settings = new GameSettings
            {
                MaxCharacterSlots = MaxCharacterSlots
            };
            _netManager.ServerSendMessage(msg, session.Channel);

            // Frontier: notify other entities that your player data is loaded.
            if (session.AttachedEntity != null)
                _entityManager.EventBus.RaiseLocalEvent(session.AttachedEntity.Value, new PreferencesLoadedEvent(session, prefsData.Prefs));
        }

        public void OnClientDisconnected(ICommonSession session)
        {
            _cachedPlayerPrefs.Remove(session.UserId);
        }

        public bool HavePreferencesLoaded(ICommonSession session)
        {
            return _cachedPlayerPrefs.ContainsKey(session.UserId);
        }


        /// <summary>
        /// Tries to get the preferences from the cache
        /// </summary>
        /// <param name="userId">User Id to get preferences for</param>
        /// <param name="playerPreferences">The user preferences if true, otherwise null</param>
        /// <returns>If preferences are not null</returns>
        public bool TryGetCachedPreferences(NetUserId userId,
            [NotNullWhen(true)] out PlayerPreferences? playerPreferences)
        {
            if (_cachedPlayerPrefs.TryGetValue(userId, out var prefs))
            {
                playerPreferences = prefs.Prefs;
                return prefs.Prefs != null;
            }

            playerPreferences = null;
            return false;
        }

        /// <summary>
        /// Retrieves preferences for the given username from storage.
        /// </summary>
        public PlayerPreferences GetPreferences(NetUserId userId)
        {
            var prefs = _cachedPlayerPrefs[userId].Prefs;
            if (prefs == null)
            {
                throw new InvalidOperationException("Preferences for this player have not loaded yet.");
            }

            return prefs;
        }

        /// <summary>
        /// Retrieves preferences for the given username from storage or returns null.
        /// </summary>
        public PlayerPreferences? GetPreferencesOrNull(NetUserId? userId)
        {
            if (userId == null)
                return null;

            if (_cachedPlayerPrefs.TryGetValue(userId.Value, out var pref))
                return pref.Prefs;
            return null;
        }

        private async Task<PlayerPreferences> GetOrCreatePreferencesAsync(NetUserId userId, CancellationToken cancel)
        {
            var prefs = await _db.GetPlayerPreferencesAsync(userId, cancel);
            if (prefs is null)
            {
                var profile = HumanoidCharacterProfile.Random();

                // Forge-Change-Start: give higher initial balance to first character of globally whitelisted players
                var whitelisted = await _db.GetWhitelistStatusAsync(userId);
                if (whitelisted)
                {
                    var startingBalance = _cfg.GetCVar(CCVars.GameWhitelistedStartingBalance);
                    profile = profile.WithBankBalance(startingBalance);
                }
                // Forge-Change-End

                return await _db.InitPrefsAsync(userId, profile, cancel);
            }

            return prefs;
        }

        public async Task RefreshPreferencesAsync(ICommonSession session, CancellationToken cancel)
        {
            if (!_cachedPlayerPrefs.TryGetValue(session.UserId, out var prefsData))
                return;

            var loadTask = LoadPrefs();
            _cachedPlayerPrefs[session.UserId] = prefsData;

            await loadTask;
            return;

            async Task LoadPrefs()
            {
                var prefs = await _db.GetPlayerPreferencesAsync(session.UserId, cancel);

                if (prefs != null)
                {
                    prefsData.Prefs = prefs;
                    prefsData.PrefsLoaded = true;

                    var msg = new MsgPreferencesAndSettings
                    {
                        Preferences = prefs,
                        Settings = new GameSettings
                        {
                            MaxCharacterSlots = MaxCharacterSlots
                        }
                    };

                    _netManager.ServerSendMessage(msg, session.Channel);
                }
            }
        }


        private PlayerPreferences SanitizePreferences(ICommonSession session, PlayerPreferences prefs, IDependencyCollection collection)
        {
            // Clean up preferences in case of changes to the game,
            // such as removed jobs still being selected.

            return new PlayerPreferences(prefs.Characters.Select(p =>
            {
                return new KeyValuePair<int, ICharacterProfile>(p.Key, p.Value.Validated(session, collection));
            }), prefs.SelectedCharacterIndex, prefs.AdminOOCColor);
        }

        public IEnumerable<KeyValuePair<NetUserId, ICharacterProfile>> GetSelectedProfilesForPlayers(
            List<NetUserId> usernames)
        {
            return usernames
                .Select(p => (_cachedPlayerPrefs[p].Prefs, p))
                .Where(p => p.Prefs != null)
                .Select(p => new KeyValuePair<NetUserId, ICharacterProfile>(p.p, p.Prefs!.SelectedCharacter));
        }

        internal static bool ShouldStorePrefs(LoginType loginType)
        {
            return loginType.HasStaticUserId();
        }

        // Forge-Change-start
        private async Task RunProfileSlotLocked(NetUserId userId, int slot, Func<Task> action)
        {
            var gate = _profileSlotLocks.GetOrAdd((userId, slot), _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                await action();
            }
            finally
            {
                gate.Release();
            }
        }

        private void ProfileUpdateRateLimited(ICommonSession session)
        {
            if (_cfg.GetCVar(CCVars.ProfileUpdateRateLimitDisconnect))
                session.Channel.Disconnect("Too many character profile update requests.");
        }

        private void ProfileUpdateRateLimitAlertAdmins(ICommonSession session)
        {
            _chat.SendAdminAlert($"Player {session.Name} is spamming character profile updates.");
        }
        // Forge-Change-end
        private sealed class PlayerPrefData
        {
            public bool PrefsLoaded;
            public PlayerPreferences? Prefs;
        }

        void IPostInjectInit.PostInject()
        {
            _userDb.AddOnLoadPlayer(LoadData);
            _userDb.AddOnFinishLoad(FinishLoad);
            _userDb.AddOnPlayerDisconnect(OnClientDisconnected);
        }
    }

    // Frontier: event for notifying that preferences for a particular player have loaded in.
    public sealed class PreferencesLoadedEvent : EntityEventArgs
    {
        public readonly ICommonSession Session;
        public readonly PlayerPreferences Prefs;

        public PreferencesLoadedEvent(ICommonSession session, PlayerPreferences prefs)
        {
            Session = session;
            Prefs = prefs;
        }
    }
    // End Frontier
}

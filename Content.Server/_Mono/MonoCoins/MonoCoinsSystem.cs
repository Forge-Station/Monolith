using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared._Mono.MonoCoins;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.StationRecords;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Mono.MonoCoins;

/// <summary>
/// System that handles MonoCoins balance for players.
/// </summary>
public sealed class MonoCoinsSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly StationRecordsSystem _stationRecords = default!;

    private const int RoundEndReward = 10;

    public override void Initialize()
    {
        base.Initialize();

        // Subscribe to network messages

        // Subscribe to player events
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);

        // Subscribe to round end events
        SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEnd);
    }


    /// <summary>
    /// Called when a player attaches. Database will handle initialization.
    /// </summary>
    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        // Database will handle initialization of MonoCoins balance
        // No action needed here
    }

    /// <summary>
    /// Called when a player detaches. Database persists the balance.
    /// </summary>
    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        // Database persists the balance automatically
        // No action needed here
    }


    /// <summary>
    /// Sets the MonoCoins balance for a player in the database.
    /// </summary>
    /// <param name="userId">The player's UserId</param>
    /// <param name="balance">The new balance</param>
    public async Task SetMonoCoinsBalanceAsync(NetUserId userId, int balance)
    {
        await _db.SetMonoCoinsAsync(userId, balance);
    }

    /// <summary>
    /// Called when a round ends. Awards MonoCoins to players who appear in the station manifest.
    /// </summary>
    private async void OnRoundEnd(RoundEndMessageEvent args)
    {
        // Forge-Change: MonoCoins disabled.
        return;

        // Award MonoCoins to players who appear in the station manifest
        var tasks = new List<Task>();

        foreach (var session in _playerManager.Sessions)
        {
            // Check if the player appears in any station manifest
            if (PlayerInManifest(session))
            {
                tasks.Add(AwardRoundEndMonoCoins(session));
            }
            else
            {
                Logger.Debug($"Player {session.Name} ({session.UserId}) not found in station manifest, skipping MonoCoins reward");
            }
        }

        // Wait for all database operations to complete
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Checks if a player appears in any station manifest by looking for their station record.
    /// </summary>
    /// <param name="session">The player session to check</param>
    /// <returns>True if the player appears in a station manifest, false otherwise</returns>
    private bool PlayerInManifest(ICommonSession session)
    {
        // Get all stations
        var stations = _stationSystem.GetStations();

        foreach (var station in stations)
        {
            // Check if this station has records
            if (!TryComp<StationRecordsComponent>(station, out var stationRecords))
                continue;

            // Look for a general station record with the player's character name
            var records = _stationRecords.GetRecordsOfType<GeneralStationRecord>(station);

            foreach (var (_, record) in records)
            {
                // Check if the record name matches the player's character name
                if (session.AttachedEntity != null &&
                    TryComp<MetaDataComponent>(session.AttachedEntity.Value, out var metaData) &&
                    record.Name.Equals(metaData.EntityName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Awards round end MonoCoins to a specific player.
    /// </summary>
    private async Task AwardRoundEndMonoCoins(ICommonSession session)
    {
        try
        {
            var newBalance = await _db.AddMonoCoinsAsync(session.UserId, RoundEndReward);
            Logger.Info($"Awarded {RoundEndReward} MonoCoins to player {session.Name} ({session.UserId}). New balance: {newBalance}");

            // Notify the player via chat
//            var notificationMessage = $"Round ended! You earned {RoundEndReward} MonoCoins. Your new balance: {newBalance}";
//            _chatManager.ChatMessageToOne(
//                ChatChannel.Notifications,
//                notificationMessage,
//                notificationMessage,
//                EntityUid.Invalid,
//                false,
//                session.Channel); // Forge-Change
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to award round end MonoCoins to player {session.Name} ({session.UserId}): {ex.Message}");
        }
    }
}

// SPDX-FileCopyrightText: Copyright (c) 2024-2025 Space Wizards Federation
// SPDX-License-Identifier: MIT
// Wolfgate: ported from HardLight.

using Content.Server.Database;
using Content.Shared._Common.Consent;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Server._Common.Consent;

public sealed class ServerConsentManager : IServerConsentManager
{
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IServerNetManager _netManager = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly UserDbDataManager _userDb = default!; // WOLFGATE

    public event Action<ICommonSession, PlayerConsentSettings>? OnConsentUpdated;

    /// <summary>
    /// Stores consent settigns for all connected players, including guests.
    /// </summary>
    private readonly Dictionary<NetUserId, ConsentSettings> _consent = new();

    public void Initialize()
    {
        _netManager.RegisterNetMessage<MsgUpdateConsent>(HandleUpdateConsentMessage);
        // WOLFGATE - HardLight calls LoadData from UserDbDataManager directly; Wolfgate's copy
        // exposes a registration hook instead, so use that rather than editing it.
        _userDb.AddOnLoadPlayer(LoadData);
    }

    private async void HandleUpdateConsentMessage(MsgUpdateConsent message)
    {
        var userId = message.MsgChannel.UserId;

        if (!_consent.TryGetValue(userId, out var consentSettings))
        {
            return;
        }

        message.Consent.EnsureValid(_configManager, _prototypeManager);

        // Update consent settings
        consentSettings.ConsentToggles = message.Consent.ToDbObject().ConsentToggles;
        if (message.Consent.Freetext != consentSettings.ConsentFreetext)
        {
            consentSettings.ConsentFreetext = message.Consent.Freetext;
            consentSettings.ConsentFreetextUpdatedAt = DateTime.UtcNow; // WOLFGATE: receipts are UTC; local time skewed the unread marker
        }

        // Log the change
        var session = _playerManager.GetSessionByChannel(message.MsgChannel);
        var togglesPretty = String.Join(", ", message.Consent.Toggles.Select(t => $"[{t.Key}: {t.Value}]"));
        _adminLogger.Add(LogType.Consent, LogImpact.Medium,
            $"{session:Player} updated consent setting to: '{message.Consent.Freetext}' with toggles {togglesPretty}");

        // Persistence
        if (ShouldStoreInDb(message.MsgChannel.AuthType))
        {
            // WOLFGATE: keep the row id in the cache; read receipts reference it and it is 0 until the first save.
            consentSettings.Id = await _db.SavePlayerConsentSettingsAsync(userId, message.Consent);
        }

        // Sent it back to the client.
        _netManager.ServerSendMessage(message, message.MsgChannel);

        // Invoke invent to let the game sim know consent was updated.
        OnConsentUpdated?.Invoke(session, message.Consent);
    }

    public async Task LoadData(ICommonSession session, CancellationToken cancel)
    {
        var consent = new ConsentSettings();
        if (ShouldStoreInDb(session.AuthType))
        {
            consent = await _db.GetPlayerConsentSettingsAsync(session.UserId);
        }

        consent.ToPlayerConsentSettings().EnsureValid(_configManager, _prototypeManager);
        _consent[session.UserId] = consent;

        var message = new MsgUpdateConsent() { Consent = consent.ToPlayerConsentSettings() };
        _netManager.ServerSendMessage(message, session.Channel);
    }

    /// <inheritdoc />
    public PlayerConsentSettings GetPlayerConsentSettings(NetUserId userId)
    {
        if (_consent.TryGetValue(userId, out var consent))
        {
            return consent.ToPlayerConsentSettings();
        }

        // A player that has disconnected does not consent to anything.
        return new PlayerConsentSettings();
    }

    public bool ConsentTextUpdatedSinceLastRead(NetUserId readerUserId, NetUserId targetUserId)
    {
        return _consent.TryGetValue(targetUserId, out var consentSettings)
            && consentSettings.ReadReceipts is not null
            && consentSettings.ReadReceipts.Any(x => x.ReaderUserId == readerUserId && x.ReadAt < consentSettings.ConsentFreetextUpdatedAt);
    }

    public async Task UpdateReadReceipt(NetUserId readerUserId, NetUserId targetUserId)
    {
        if (!_consent.TryGetValue(targetUserId, out var consentSettings))
        {
            // Somehow there exists a NetUserId of a player that never connected/ran LoadData?
            // I guess this could happen after saving and loading a round since that would wipe _consent.

            consentSettings = await _db.GetPlayerConsentSettingsAsync(targetUserId);
            _consent[targetUserId] = consentSettings;
        }

        // WOLFGATE: settings that were never saved have no row to reference, so that receipt stays in memory.
        var readRecipe = consentSettings.Id == 0
            ? new ConsentFreetextReadReceipt { ReaderUserId = readerUserId, ReadAt = DateTime.UtcNow }
            : await _db.UpdatePlayerConsentReadReceipt(readerUserId, consentSettings.Id);
        consentSettings.ReadReceipts ??= new();
        consentSettings.ReadReceipts.RemoveAll(x => x.ReaderUserId == readerUserId);
        consentSettings.ReadReceipts.Add(readRecipe);
    }

    private static bool ShouldStoreInDb(LoginType loginType)
    {
        return loginType.HasStaticUserId();
    }
}

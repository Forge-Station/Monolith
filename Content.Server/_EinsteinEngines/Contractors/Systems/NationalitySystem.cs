using Content.Server.Players.PlayTimeTracking;
using Content.Shared._EE.Contractors.Prototypes;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Utility;

namespace Content.Server._EE.Contractors.Systems;

public sealed class NationalitySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTimeTracking = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    // When the player is spawned in, add the nationality components selected during character creation
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args) =>
        ApplyNationality(args.Mob, args.JobId, args.Profile);

    /// <summary>
    ///     Adds the nationality selected by a player to an entity.
    /// </summary>
    public void ApplyNationality(EntityUid uid, ProtoId<JobPrototype>? jobId, HumanoidCharacterProfile profile)
    {
        if (jobId == null || !_prototype.TryIndex(jobId, out var jobPrototypeToUse))
            return;

        var nationalityId = !string.IsNullOrEmpty(profile.Nationality)
            ? profile.Nationality
            : SharedHumanoidAppearanceSystem.DefaultNationality;

        if (!_prototype.TryIndex<NationalityPrototype>(nationalityId, out var nationalityPrototype))
        {
            Logger.Warning($"Nationality '{nationalityId}' not found!");
            return;
        }

        AddNationality(uid, nationalityPrototype);
    }

    /// <summary>
    ///     Adds a single Nationality Prototype to an Entity.
    /// </summary>
    public void AddNationality(EntityUid uid, NationalityPrototype nationalityPrototype)
    {
        if (!_configuration.GetCVar(CCVars.ContractorsEnabled))
        {
            return;
        }

        foreach (var special in nationalityPrototype.Special)
        {
            special.AfterEquip(uid);
        }
    }
}

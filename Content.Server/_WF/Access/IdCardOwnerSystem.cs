using Content.Shared._WF.Access;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Robust.Shared.Player;

namespace Content.Server._WF.Access;

/// <summary>
/// Stamps a spawning player's ID card with who it belongs to.
/// </summary>
public sealed class IdCardOwnerSystem : EntitySystem
{
    [Dependency] private SharedIdCardSystem _idCard = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!_idCard.TryFindIdCard(ev.Mob, out var idCard))
            return;

        var owner = EnsureComp<IdCardOwnerComponent>(idCard);
        owner.UserId = ev.Player.UserId;
        owner.CharacterName = ev.Profile.Name;
    }

    /// <summary>
    /// True when the ID card was issued to the player currently controlling <paramref name="mob"/>.
    /// </summary>
    public bool IsOwnedBy(EntityUid idCard, EntityUid mob)
    {
        if (!TryComp<IdCardOwnerComponent>(idCard, out var owner))
            return false;

        if (!TryComp<ActorComponent>(mob, out var actor))
            return false;

        return actor.PlayerSession.UserId == owner.UserId;
    }
}

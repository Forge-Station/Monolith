using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Components;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// What NPC crews can't see: a mob with its stealth field up (a stealth suit) and a ship with its IFF hidden (a
/// cloak). Either is seen again for a while after a hostile act: hurting, robbing or shooting, or firing on a ship.
/// </summary>
public sealed partial class WFCrewStealthSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How long a stealthed mob stays seen after its last hostile act.</summary>
    public static readonly TimeSpan MobRevealTime = TimeSpan.FromSeconds(60);

    /// <summary>How long a cloaked ship stays seen after its last hostile act.</summary>
    public static readonly TimeSpan ShipRevealTime = TimeSpan.FromSeconds(120);

    private readonly Dictionary<EntityUid, TimeSpan> _revealed = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StealthComponent, StealthShotRevealedEvent>(OnShotAttempted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _revealed.Clear());
    }

    private void OnShotAttempted(Entity<StealthComponent> ent, ref StealthShotRevealedEvent args)
    {
        Reveal(ent);
    }

    /// <summary>Whether NPC crews can't see a mob: its stealth field is up and it has done nothing hostile lately.</summary>
    public bool Unseen(EntityUid mob)
    {
        return TryComp<StealthComponent>(mob, out var stealth) && stealth.Enabled && !Revealed(mob);
    }

    /// <summary>Whether NPC crews can't see a ship: its IFF is hidden and it has done nothing hostile lately.</summary>
    public bool ShipUnseen(EntityUid grid)
    {
        return TryComp<IFFComponent>(grid, out var iff) && (iff.Flags & IFFFlags.Hide) != 0 && !Revealed(grid);
    }

    /// <summary>Shows a mob to every crew for <see cref="MobRevealTime"/>.</summary>
    public void Reveal(EntityUid mob) => _revealed[mob] = _timing.CurTime + MobRevealTime;

    /// <summary>Shows a ship to every crew for <see cref="ShipRevealTime"/>.</summary>
    public void RevealShip(EntityUid grid) => _revealed[grid] = _timing.CurTime + ShipRevealTime;

    private bool Revealed(EntityUid uid)
    {
        if (!_revealed.TryGetValue(uid, out var until))
            return false;

        if (_timing.CurTime < until)
            return true;

        _revealed.Remove(uid);
        return false;
    }
}

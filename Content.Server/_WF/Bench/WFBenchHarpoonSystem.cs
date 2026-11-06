using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Bench;

/// <summary>Draws a fresh dart for a Bench harpoon some time after it fires, so judges' ships never run dry.</summary>
[RegisterComponent]
public sealed partial class WFBenchHarpoonComponent : Component
{
    /// <summary>Time to draw one dart.</summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromMinutes(2);

    [ViewVariables]
    public TimeSpan? NextDart;
}

/// <summary>Refills Bench harpoons one dart at a time up to capacity.</summary>
public sealed class WFBenchHarpoonSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GunSystem _gun = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;
        var query = EntityQueryEnumerator<WFBenchHarpoonComponent, BallisticAmmoProviderComponent>();
        while (query.MoveNext(out var uid, out var harpoon, out var ammo))
        {
            if (ammo.Count >= ammo.Capacity)
            {
                harpoon.NextDart = null;
                continue;
            }

            if (harpoon.NextDart is not { } next)
            {
                harpoon.NextDart = now + harpoon.Interval;
                continue;
            }

            if (now < next)
                continue;

            _gun.SetBallisticUnspawned((uid, ammo), ammo.UnspawnedCount + 1);
            harpoon.NextDart = null;
        }
    }
}

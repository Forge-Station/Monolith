using Content.Shared._HL.Traits.Physical;
using Content.Shared._Starlight;
using Content.Shared._Starlight.Shadekin;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._HL.Traits.Physical;

/// <summary>
/// Applies passive per-type healing to shadekins with ShadekinRegenerationComponent, only in complete darkness.
/// Used for both the default shadekin base healing and the optional regen traits.
/// </summary>
public sealed class ShadekinRegenerationSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ShadekinRegenerationComponent, ShadekinComponent, DamageableComponent>();

        while (query.MoveNext(out var uid, out var regen, out var shadekin, out var damageable))
        {
            if (shadekin.CurrentState != ShadekinState.Dark)
                continue;

            if (regen.NextUpdate > curTime)
                continue;

            regen.NextUpdate = curTime + TimeSpan.FromSeconds(Math.Max(0.1f, regen.IntervalSeconds));

            if (damageable.TotalDamage <= 0)
                continue;

            var healSpec = new DamageSpecifier();
            var critMult = _mobState.IsCritical(uid) ? regen.CritMultiplier : 1f;
            var amountPerTick = regen.HealPerSecond * regen.IntervalSeconds * critMult;

            foreach (var healType in regen.HealTypes)
            {
                if (!damageable.Damage.DamageDict.TryGetValue(healType, out var existing) || existing <= 0)
                    continue;

                healSpec.DamageDict[healType] = FixedPoint2.New(-Math.Min((float) existing, amountPerTick));
            }

            if (healSpec.DamageDict.Count == 0)
                continue;

            _damageable.TryChangeDamage(uid, healSpec, true, false, damageable);
        }
    }
}

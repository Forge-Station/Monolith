using System.Linq;
using Content.Server._WF.Remade.Limbs;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Elites;

/// <summary>Heals the hunter and the Undying and grows their limbs back while they're up, showing the limb grow.</summary>
public sealed partial class WFRegenSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFEliteSystem _elite = default!;
    [Dependency] private WFRemadeLimbsSystem _limbs = default!;

    private static readonly TimeSpan HealStep = TimeSpan.FromSeconds(1);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFRegenComponent>();
        while (query.MoveNext(out var uid, out var regen))
        {
            if (_mobState.IsDead(uid))
            {
                Stop(uid, regen);
                continue;
            }

            if (now >= regen.NextHeal)
            {
                regen.NextHeal = now + HealStep;
                _elite.Heal(uid, regen.HealPerSecond, regen.HeatFactor);
            }

            // The clock for the next limb starts when one comes off.
            if (!TryComp<WFRemadeLimbsComponent>(uid, out var limbs) || limbs.Severed.Count == 0)
            {
                regen.NextLimb = now + TimeSpan.FromSeconds(regen.LimbInterval);
                regen.Sprouting = null;
                continue;
            }

            var grow = TimeSpan.FromSeconds(regen.GrowSeconds);
            if (regen.Sprouting == null)
            {
                // The limb starts showing as it grows a little before it is due, so it is whole when the interval is up.
                if (now + grow < regen.NextLimb || _limbs.GrowOrder(limbs).FirstOrDefault() is not { } next)
                    continue;

                if (regen.GrowSeconds > 0f)
                {
                    regen.Sprouting = next;
                    _limbs.SetGrowing(uid, new[] { next }, limbs);
                }
            }

            if (now < regen.NextLimb)
                continue;

            regen.NextLimb = now + TimeSpan.FromSeconds(regen.LimbInterval);
            var grown = regen.Sprouting is { } id ? _limbs.Regrow(uid, id, limbs) : _limbs.Regrow(uid, 1, limbs) > 0;
            regen.Sprouting = null;
            if (!grown)
            {
                _limbs.SetGrowing(uid, Array.Empty<string>(), limbs);
                continue;
            }

            regen.Grown++;
            _audio.PlayPvs(regen.Sound, uid);
            _popup.PopupEntity(Loc.GetString("wf-remade-elite-regrows", ("mob", uid)), uid, PopupType.MediumCaution);
        }
    }

    /// <summary>A mob that dies mid-growth stops growing; a rising mob shows its own regrowth.</summary>
    private void Stop(EntityUid uid, WFRegenComponent regen)
    {
        if (regen.Sprouting == null)
            return;

        regen.Sprouting = null;
        _limbs.SetGrowing(uid, Array.Empty<string>());
    }
}

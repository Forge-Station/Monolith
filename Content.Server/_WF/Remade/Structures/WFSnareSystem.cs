using Content.Server._WF.Remade.Conversion;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Structures;

/// <summary>A snare trips whoever steps on it and slows them. It rides on step triggers, so walking carefully gets past it.</summary>
public sealed partial class WFSnareSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private WFGrowthSystem _growth = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSnareComponent, StepTriggerAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WFSnareComponent, StepTriggeredOnEvent>(OnTriggered);
    }

    private void OnAttempt(Entity<WFSnareComponent> ent, ref StepTriggerAttemptEvent args)
    {
        args.Continue = _growth.IsActive(ent)
                        && _timing.CurTime >= ent.Comp.ReadyAt
                        && HasComp<MobStateComponent>(args.Tripper)
                        && !HasComp<WFRemadeComponent>(args.Tripper)
                        && !_mobState.IsDead(args.Tripper);
    }

    private void OnTriggered(Entity<WFSnareComponent> ent, ref StepTriggeredOnEvent args)
    {
        var snare = ent.Comp;
        snare.ReadyAt = _timing.CurTime + snare.Reload;
        snare.Trips++;

        _stun.TryKnockdown(args.Tripper, snare.Knockdown, true);
        _stun.TrySlowdown(args.Tripper, snare.Slow, true, snare.SlowMultiplier, snare.SlowMultiplier);
        _audio.PlayPvs(snare.TripSound, ent);
        _popup.PopupEntity(Loc.GetString("wf-remade-snare-trip"), args.Tripper, args.Tripper, PopupType.MediumCaution);
    }
}

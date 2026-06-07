using Content.Shared._Forge.Weapons.ChainsawShield;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Weapons.ChainsawShield;

public sealed partial class ToggleActiveSoundRepeaterSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ToggleActiveSoundRepeaterComponent, ItemToggledEvent>(OnToggled);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveToggleSoundRepeaterComponent, ToggleActiveSoundRepeaterComponent, ItemToggleComponent>();
        while (query.MoveNext(out var uid, out _, out var repeater, out var toggle))
        {
            if (!toggle.Activated || _timing.CurTime < repeater.NextSound)
                continue;

            _audio.PlayPvs(repeater.Sound, uid);
            ScheduleNext(repeater, GetInterval(repeater));
        }
    }

    private void OnToggled(EntityUid uid, ToggleActiveSoundRepeaterComponent component, ref ItemToggledEvent args)
    {
        if (!args.Activated)
        {
            RemCompDeferred<ActiveToggleSoundRepeaterComponent>(uid);
            return;
        }

        EnsureComp<ActiveToggleSoundRepeaterComponent>(uid);
        ScheduleNext(component, GetInitialDelay(component));
    }

    private void ScheduleNext(ToggleActiveSoundRepeaterComponent component, TimeSpan delay)
    {
        component.NextSound = _timing.CurTime + delay;
    }

    private static TimeSpan GetInitialDelay(ToggleActiveSoundRepeaterComponent component)
    {
        return TimeSpan.FromSeconds(MathF.Max(component.InitialDelay, 0f));
    }

    private static TimeSpan GetInterval(ToggleActiveSoundRepeaterComponent component)
    {
        return TimeSpan.FromSeconds(MathF.Max(component.Interval, 0.1f));
    }
}

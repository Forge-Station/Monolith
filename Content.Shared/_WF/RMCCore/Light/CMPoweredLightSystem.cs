using Content.Shared.Interaction.Events;

namespace Content.Shared._WF.RMCCore.Light;

public sealed partial class CMPoweredLightSystem : EntitySystem
{
    [Dependency] private SharedPointLightSystem _pointLight = default!;

    public override void Initialize()
    {
        // WOLFGATE: RMC's no-burn light hook (LightBurnHandAttemptEvent) isn't ported; Wolfgate keeps upstream light burns
        SubscribeLocalEvent<PreventAttackLightOffComponent, GettingAttackedAttemptEvent>(OnPreventAttackLightOffAttackedAttempt);
    }

    private void OnPreventAttackLightOffAttackedAttempt(Entity<PreventAttackLightOffComponent> ent, ref GettingAttackedAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (IsOff(ent))
            args.Cancelled = true;
    }

    public bool IsOff(EntityUid light)
    {
        return !_pointLight.TryGetLight(light, out var pointLight) ||
               !pointLight.Enabled;
    }
}

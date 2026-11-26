using Content.Shared._WF.Barricades;
using Content.Shared._WF.Xenonids.Acid;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Xenonids.Acid;

public sealed partial class XenoAcidSystem : SharedXenoAcidSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _config = default!;

    private int CorrosiveAcidDamageTimeSeconds;

    public override void Initialize()
    {
        base.Initialize();

        // WOLFGATE: flares burn out faster under acid in RMC; Wolfgate's expendable light has no acid hooks, so that is cut
        SubscribeLocalEvent<BarricadeComponent, CorrodingEvent>(OnBarricadeCorrodingEvent);

        Subs.CVar(_config, WFXenoAcidCVars.RMCCorrosiveAcidDamageTimeSeconds, obj => CorrosiveAcidDamageTimeSeconds = obj, true);
    }

    private void OnBarricadeCorrodingEvent(Entity<BarricadeComponent> target, ref CorrodingEvent args)
    {
        AddComp(target, new DamageableCorrodingComponent
        {
            Acid = args.Acid,
            Dps = args.Dps,
            Damage = new(PrototypeManager.Index<DamageTypePrototype>(CorrosiveAcidDamageTypeStr), args.Dps * CorrosiveAcidTickDelaySeconds),
            Strength = args.AcidStrength,
            AcidExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(CorrosiveAcidDamageTimeSeconds),
        });

        args.Cancelled = true;
    }
}


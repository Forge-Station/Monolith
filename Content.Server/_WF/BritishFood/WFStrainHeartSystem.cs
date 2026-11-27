using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.BritishFood;

namespace Content.Server._WF.BritishFood;

/// <summary>Applies <see cref="WFStrainHeart"/> to a wound host's heart organ through Wolfmed's organ damage.</summary>
public sealed partial class WFStrainHeartSystem : EntitySystem
{
    [Dependency] private WolfmedElectricalBurnSystem _organs = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundHostComponent, WFStrainHeartEvent>(OnStrainHeart);
    }

    private void OnStrainHeart(Entity<WoundHostComponent> ent, ref WFStrainHeartEvent args)
    {
        _organs.TryShockOrgan(ent, WolfmedElectricalBurnSystem.HeartSlot, args.Amount);
    }
}

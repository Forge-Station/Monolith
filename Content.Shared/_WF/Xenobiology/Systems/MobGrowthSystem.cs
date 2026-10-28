using Content.Shared._WF.Xenobiology.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Xenobiology.Systems;

/// <summary>
/// Grows mobs to their next stage once they have eaten enough.
/// </summary>
public sealed partial class MobGrowthSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobGrowthComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<MobGrowthComponent> ent, ref ComponentInit args)
    {
        ent.Comp.NextGrowthTime = _timing.CurTime + ent.Comp.GrowthInterval;
        if (string.IsNullOrEmpty(ent.Comp.BaseEntityName))
            ent.Comp.BaseEntityName = Name(ent);

        if (!ent.Comp.Stages.ContainsKey(ent.Comp.CurrentStage))
        {
            Log.Error($"Invalid initial stage {ent.Comp.CurrentStage} for {ToPrettyString(ent)}");
            ent.Comp.CurrentStage = ent.Comp.FirstStage;
        }

        UpdateAppearance(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MobGrowthComponent, HungerComponent>();
        while (query.MoveNext(out var uid, out var growth, out var hunger))
        {
            if (now < growth.NextGrowthTime)
                continue;

            growth.NextGrowthTime = now + growth.GrowthInterval;
            if (_mobState.IsDead(uid)
                || _hunger.GetHunger(hunger) < growth.HungerRequired
                || !growth.Stages.TryGetValue(growth.CurrentStage, out var stage)
                || string.IsNullOrEmpty(stage.NextStage)
                || !growth.Stages.ContainsKey(stage.NextStage))
                continue;

            _hunger.ModifyHunger(uid, growth.GrowthCost, hunger);
            growth.CurrentStage = stage.NextStage;
            Dirty(uid, growth);
            UpdateAppearance((uid, growth));
        }
    }

    /// <summary>
    /// Applies the current stage's sprite and name.
    /// </summary>
    public void UpdateAppearance(Entity<MobGrowthComponent> ent)
    {
        if (!ent.Comp.Stages.TryGetValue(ent.Comp.CurrentStage, out var stage))
            return;

        if (stage.Sprite is { } sprite && TryComp<AppearanceComponent>(ent, out var appearance))
            _appearance.SetData(ent, GrowthStateVisuals.Sprite, sprite, appearance);

        if (_net.IsServer && !string.IsNullOrEmpty(stage.DisplayName))
            _metaData.SetEntityName(ent, Loc.GetString("mob-growth-stage-name",
                ("stage", stage.DisplayName), ("name", ent.Comp.BaseEntityName)));
    }
}

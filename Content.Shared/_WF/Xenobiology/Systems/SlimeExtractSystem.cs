using Content.Shared._WF.Xenobiology.Components;
using Content.Shared.Examine;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenobiology.Systems;

/// <summary>
/// A handheld scanner that reads slimes and slime extracts.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlimeScannerComponent : Component;

/// <summary>
/// Tells players when a slime extract is spent.
/// </summary>
public sealed partial class SlimeExtractSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeExtractComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<SlimeExtractComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Spent)
            args.PushMarkup(Loc.GetString("xeno-extract-reaction-unreactive"));
    }
}

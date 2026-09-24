using Content.Shared._Forge.Genetics.Mutations;
using Content.Shared.Humanoid;

namespace Content.Server._Forge.Genetics.Mutations;

public sealed class GeneticHulkSystem : EntitySystem
{
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoid = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticHulkComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GeneticHulkComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<GeneticHulkComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<HumanoidAppearanceComponent>(ent, out var humanoid))
            return;

        if (!ent.Comp.SkinCaptured)
        {
            ent.Comp.OriginalSkin = humanoid.SkinColor;
            ent.Comp.SkinCaptured = true;
        }

        _humanoid.SetSkinColor(ent, ent.Comp.Tint, verify: false, humanoid: humanoid);
    }

    private void OnShutdown(Entity<GeneticHulkComponent> ent, ref ComponentShutdown args)
    {
        if (!ent.Comp.SkinCaptured || ent.Comp.OriginalSkin is not { } original)
            return;

        if (!TryComp<HumanoidAppearanceComponent>(ent, out var humanoid))
            return;

        _humanoid.SetSkinColor(ent, original, verify: false, humanoid: humanoid);
    }
}

using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Robust.Shared.Network;

namespace Content.Shared._WF.Xenonids.Parasite.Surgery;

/// <summary>Wires the larva removal surgery's condition and steps to the parasite system.</summary>
public sealed class XenoLarvaSurgerySystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedXenoParasiteSystem _parasite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoLarvaSurgeryConditionComponent, SurgeryValidEvent>(OnValid);
        SubscribeLocalEvent<XenoLarvaCutRootsStepComponent, SurgeryStepEvent>(OnCutRoots);
        SubscribeLocalEvent<XenoLarvaCutRootsStepComponent, SurgeryStepCompleteCheckEvent>(OnCutRootsCheck);
        SubscribeLocalEvent<XenoLarvaRemoveStepComponent, SurgeryStepEvent>(OnRemove);
        SubscribeLocalEvent<XenoLarvaRemoveStepComponent, SurgeryStepCompleteCheckEvent>(OnRemoveCheck);
    }

    private void OnValid(Entity<XenoLarvaSurgeryConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!TryComp(args.Body, out VictimInfectedComponent? infected) || infected.IsBursting)
            args.Cancelled = true;
    }

    private void OnCutRoots(Entity<XenoLarvaCutRootsStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (_net.IsServer)
            _parasite.TryCutLarvaRoots(args.Body);
    }

    private void OnCutRootsCheck(Entity<XenoLarvaCutRootsStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryComp(args.Body, out VictimInfectedComponent? infected) || !infected.RootsCut)
            args.Cancelled = true;
    }

    private void OnRemove(Entity<XenoLarvaRemoveStepComponent> ent, ref SurgeryStepEvent args)
    {
        if (_net.IsServer)
            _parasite.TryRemoveLarva(args.Body, args.User);
    }

    private void OnRemoveCheck(Entity<XenoLarvaRemoveStepComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (HasComp<VictimInfectedComponent>(args.Body))
            args.Cancelled = true;
    }
}

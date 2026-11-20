using Content.Shared._WF.Lattice;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;

namespace Content.Server._WF.Lattice;

/// <summary>Relay anchors run on exotic matter: shards are loaded by hand and burn down while the relay holds.</summary>
public sealed partial class WFLatticeSystem
{
    [Dependency] private SharedStackSystem _stacks = default!;

    private const string ExoticStack = "WFExoticMatter";

    private void InitializeRelayFuel()
    {
        SubscribeLocalEvent<WFLatticeRelayComponent, InteractUsingEvent>(OnRelayInteractUsing);
        SubscribeLocalEvent<WFLatticeRelayComponent, ExaminedEvent>(OnRelayExamined);
    }

    private void OnRelayInteractUsing(Entity<WFLatticeRelayComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !ent.Comp.NeedsFuel || !TryComp<StackComponent>(args.Used, out var stack) || stack.StackTypeId != ExoticStack)
            return;

        args.Handled = true;
        var room = (int) ((ent.Comp.MaxShards * ent.Comp.FuelPerShard - ent.Comp.Fuel) / ent.Comp.FuelPerShard);
        var loaded = Math.Min(room, stack.Count);
        if (loaded <= 0)
        {
            _popup.PopupEntity(Loc.GetString("wf-lattice-relay-full"), ent, args.User);
            return;
        }

        _stacks.Use(args.Used, loaded, stack);
        ent.Comp.Fuel += loaded * ent.Comp.FuelPerShard;
        _popup.PopupEntity(Loc.GetString("wf-lattice-relay-loaded", ("count", loaded)), ent, args.User);
    }

    private void OnRelayExamined(Entity<WFLatticeRelayComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.NeedsFuel)
            return;

        var minutes = (int) MathF.Ceiling(ent.Comp.Fuel / 60f);
        args.PushMarkup(minutes > 0
            ? Loc.GetString("wf-lattice-relay-fuel", ("minutes", minutes))
            : Loc.GetString("wf-lattice-relay-no-fuel"));
    }
}

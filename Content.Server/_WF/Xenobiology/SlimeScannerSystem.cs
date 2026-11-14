using System.Linq;
using System.Text;
using Content.Server.NPC.HTN;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared._WF.Xenobiology.Systems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Robust.Shared.Utility;

namespace Content.Server._WF.Xenobiology;

/// <summary>
/// The slime scanner's tooltips: a slime's breed, mutations and yield, or what an extract reacts to.
/// </summary>
public sealed partial class SlimeScannerSystem : EntitySystem
{
    [Dependency] private ExamineSystemShared _examine = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeComponent, AfterInteractUsingEvent>(OnSlimeInteractUsing);
        SubscribeLocalEvent<SlimeExtractComponent, AfterInteractUsingEvent>(OnExtractInteractUsing);
    }

    private void OnSlimeInteractUsing(Entity<SlimeComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (!CanScan(args))
            return;

        args.Handled = true;
        SendTooltip(args.User, ent, SlimeMarkup(ent));
    }

    private void OnExtractInteractUsing(Entity<SlimeExtractComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (!CanScan(args))
            return;

        args.Handled = true;
        SendTooltip(args.User, ent, Loc.GetString("slime-scanner-examine-extract", ("reagents", ExtractMarkup(ent))));
    }

    private bool CanScan(AfterInteractUsingEvent args)
    {
        return !args.Handled && args.Target != null && args.CanReach && HasComp<SlimeScannerComponent>(args.Used);
    }

    private void SendTooltip(EntityUid user, EntityUid target, string markup)
    {
        _examine.SendExamineTooltip(user, target, FormattedMessage.FromMarkupPermissive(markup), false, true);
    }

    private string SlimeMarkup(Entity<SlimeComponent> ent)
    {
        var sb = new StringBuilder();
        var breedName = ProtoMan.TryIndex(ent.Comp.Breed, out var breed) ? breed.BreedName : ent.Comp.Breed.Id;
        sb.AppendLine(Loc.GetString("slime-scanner-examine-slime-description",
            ("color", ent.Comp.SlimeColor.ToHex()), ("name", breedName)));

        var mutations = ent.Comp.PotentialMutations
            .Select(id => ProtoMan.TryIndex(id, out var proto) ? proto : null)
            .Where(proto => proto != null)
            .Select(proto =>
            {
                var color = proto!.Components.TryGetComponent("Slime", out var comp) && comp is SlimeComponent slime
                    ? slime.SlimeColor.ToHex()
                    : "white";
                return $"[color={color}]{proto.BreedName}[/color]";
            })
            .ToList();

        sb.Append(Loc.GetString("slime-scanner-examine-slime-mutations", ("chance", MathF.Floor(ent.Comp.MutationChance * 100f))));
        sb.Append(' ');
        sb.AppendLine(mutations.Count > 0 ? string.Join(", ", mutations) + "." : "-");
        sb.Append(Loc.GetString("slime-scanner-examine-slime-extracts", ("num", ent.Comp.ExtractsProduced)));
        return sb.ToString();
    }

    private string ExtractMarkup(EntityUid ent)
    {
        if (!TryComp<ReactiveComponent>(ent, out var reactive) || reactive.Reactions == null)
            return Loc.GetString("slime-scanner-examine-extract-unreactive");

        var reagents = new List<string>();
        foreach (var reaction in reactive.Reactions)
        {
            if (reaction.Reagents == null)
                continue;

            foreach (var reagent in reaction.Reagents)
            {
                if (ProtoMan.TryIndex<ReagentPrototype>(reagent, out var proto))
                    reagents.Add($"[color={proto.SubstanceColor.ToHex()}]{proto.LocalizedName}[/color]");
            }
        }

        return reagents.Count > 0 ? string.Join(", ", reagents.Distinct()) + "." : Loc.GetString("slime-scanner-examine-extract-unreactive");
    }
}

/// <summary>
/// Pauses a mob's NPC while it sits in a xenovac tank.
/// </summary>
public sealed partial class XenoVacuumSystem : SharedXenoVacuumSystem
{
    [Dependency] private HTNSystem _htn = default!;

    protected override void SetHTNEnabled(EntityUid uid, bool enabled, float planCooldown = 0f)
    {
        if (TryComp<HTNComponent>(uid, out var htn))
            _htn.SetHTNEnabled((uid, htn), enabled, planCooldown);
    }
}

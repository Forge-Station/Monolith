using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.Planets.Flight;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Landing thruster refits (<see cref="WFLandingThrusterRefit"/>): every linear thruster on a ship converted for
/// atmospheric flight, keeping its parts and ratings. Each thruster is a unit.
/// </summary>
public sealed partial class WFLandingThrusterRefitSystem : EntitySystem
{
    [Dependency] private ThrusterSystem _thrusters = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFRefitSurveyEvent<WFLandingThrusterRefit>>(OnSurvey);
        SubscribeLocalEvent<WFRefitProspectEvent<WFLandingThrusterRefit>>(OnProspect);
        SubscribeLocalEvent<WFRefitApplyEvent<WFLandingThrusterRefit>>(OnApply);
    }

    /// <summary>Counts the ship's converted linear thrusters, and lists the rest.</summary>
    private void OnSurvey(WFRefitSurveyEvent<WFLandingThrusterRefit> args)
    {
        var survey = args.Survey;
        var children = Transform(survey.Ship).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            var xform = Transform(uid);
            if (!xform.Anchored || !IsLinear(uid, out _))
                continue;

            if (HasComp<WFLandingThrusterComponent>(uid))
            {
                survey.UnitsAtTier++;
                continue;
            }

            var target = new WFRefitTarget(uid, xform.LocalPosition, survey.Tier);
            target.Lacking[0] = 1;
            survey.Below.Add(target);
        }
    }

    /// <summary>A rebuilt linear thruster comes back unconverted.</summary>
    private void OnProspect(WFRefitProspectEvent<WFLandingThrusterRefit> args)
    {
        var prospect = args.Prospect;
        if (_prototype.TryIndex(prospect.Prototype, out var proto) &&
            proto.TryComp<ThrusterComponent>(out var thruster, Factory) &&
            thruster.Type == ThrusterType.Linear &&
            !proto.TryComp<WFLandingThrusterComponent>(out _, Factory))
        {
            prospect.Lacking[0]++;
        }
    }

    private void OnApply(WFRefitApplyEvent<WFLandingThrusterRefit> args)
    {
        if (!IsLinear(args.Target, out var thruster) || HasComp<WFLandingThrusterComponent>(args.Target))
            return;

        EnsureComp<WFLandingThrusterComponent>(args.Target);
        _thrusters.WfRefreshAtmosphereThruster(args.Target, thruster);
    }

    private bool IsLinear(EntityUid uid, out ThrusterComponent thruster)
    {
        return TryComp(uid, out thruster!) && thruster.Type == ThrusterType.Linear;
    }
}

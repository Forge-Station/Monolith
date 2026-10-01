using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Emp;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Emp;

/// <summary>
/// An EMP burns out machine tissue: cybernetic limbs on anyone, and every part of an IPC or other chassis.
/// </summary>
/// <remarks>
/// The pulse reaches body parts one at a time (the EMP lookup includes contained entities), so parts are collected
/// per body and resolved together at the end of the tick. That is what lets one pulse share a per-body budget: a
/// whole chassis takes a hard hit rather than ten separate ones, and a single cybernetic arm takes it all.
/// </remarks>
public sealed class WolfmedEmpSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundDamageRoutingSystem _routing = default!;
    [Dependency] private Content.Server._WF.Wolfmed.Gore.WolfmedMachineSparkSystem _sparks = default!;

    private static readonly ProtoId<DamageTypePrototype> Shock = "Shock";

    private readonly Dictionary<EntityUid, List<EntityUid>> _pending = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundableComponent, EmpPulseEvent>(OnEmpPulse);
    }

    private void OnEmpPulse(Entity<WoundableComponent> part, ref EmpPulseEvent args)
    {
        if (_config.GetCVar(WolfmedCVars.EmpPartDamage) <= 0f ||
            _traits.IsOrganic(part.AsNullable()) ||
            CompOrNull<BodyPartComponent>(part)?.Body is not { } body ||
            !HasComp<WoundHostComponent>(body))
            return;

        args.Affected = true;
        if (!_pending.TryGetValue(body, out var parts))
            _pending[body] = parts = new List<EntityUid>();

        if (!parts.Contains(part))
            parts.Add(part);
    }

    public override void Update(float frameTime)
    {
        if (_pending.Count == 0)
            return;

        var perPart = _config.GetCVar(WolfmedCVars.EmpPartDamage);
        var perBody = _config.GetCVar(WolfmedCVars.EmpBodyDamage);
        var shock = _prototypes.Index(Shock);

        // Snapshot: the damage below can detach or destroy parts, and nothing may add to the map mid-walk.
        var bodies = new List<KeyValuePair<EntityUid, List<EntityUid>>>(_pending);
        _pending.Clear();

        foreach (var (body, parts) in bodies)
        {
            if (TerminatingOrDeleted(body) || parts.Count == 0)
                continue;

            var amount = perBody > 0f ? MathF.Min(perPart, perBody / parts.Count) : perPart;
            if (amount <= 0f)
                continue;

            foreach (var part in parts)
            {
                if (TerminatingOrDeleted(part))
                    continue;

                _routing.TryApplyPartDamage(body, part, new DamageSpecifier(shock, FixedPoint2.New(amount)),
                    ignoreResistances: true);
            }

            // A pulse also switches cybernetics off without wounding them; the spark system has to look either way.
            _sparks.QueueRefresh(body);
        }
    }
}

using Content.Shared._WF.RMCCore.Melee;
using Content.Shared._WF.Xenonids.Charge;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Doors.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.ClawSharpness;

public sealed partial class XenoClawsSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _protoManager = default!;
    [Dependency] private DamageableSystem _damageable = default!;

    private EntityQuery<MeleeWeaponComponent> _meleeWeaponQuery;
    private EntityQuery<XenoClawsComponent> _xenoClawsQuery;
    private EntityQuery<XenoComponent> _xenoQuery;
    private readonly ProtoId<DamageGroupPrototype> _clawsDamageGroup = "Brute";

    public override void Initialize()
    {
        _meleeWeaponQuery = GetEntityQuery<MeleeWeaponComponent>();
        _xenoClawsQuery = GetEntityQuery<XenoClawsComponent>();
        _xenoQuery = GetEntityQuery<XenoComponent>();

        SubscribeLocalEvent<ReceiverXenoClawsComponent, DamageModifyEvent>(OnReceiverDamageModify);
        SubscribeLocalEvent<AirlockReceiverXenoClawsComponent, DamageModifyEvent>(OnAirlockReceiverDamageModify);
        SubscribeLocalEvent<XenoSlashableComponent, BeforeDamageChangedEvent>(OnSlashableBeforeDamage);
    }

    /// <summary>
    /// Re-applies a xeno claw or tail hit without the structure's flat reductions, which split brute damage never
    /// gets past. Coefficients still apply.
    /// </summary>
    private void OnSlashableBeforeDamage(Entity<XenoSlashableComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled ||
            args.IgnoreResistances ||
            args.Tool is not { } tool ||
            !_xenoClawsQuery.HasComp(tool) ||
            !_meleeWeaponQuery.HasComp(tool))
        {
            return;
        }

        // these replace the damage themselves after resistances
        if (HasComp<ReceiverXenoClawsComponent>(ent) ||
            HasComp<AirlockReceiverXenoClawsComponent>(ent) ||
            HasComp<MeleeReceivedMultiplierComponent>(ent))
        {
            return;
        }

        if (!TryComp(ent, out DamageableComponent? damageable) ||
            damageable.DamageModifierSetId is not { } setId ||
            !_protoManager.TryIndex(setId, out var set) ||
            set.FlatReduction.Count == 0)
        {
            return;
        }

        var damage = new DamageSpecifier(args.Damage);
        foreach (var (type, coefficient) in set.Coefficients)
        {
            if (damage.DamageDict.TryGetValue(type, out var value) && value > 0)
                damage.DamageDict[type] = value * coefficient;
        }

        args.Cancelled = true;
        args.Applied = _damageable.TryChangeDamage(ent,
            damage,
            ignoreResistances: true,
            interruptsDoAfters: args.InterruptsDoAfters,
            damageable: damageable,
            origin: args.Origin,
            tool: tool);
    }

    /*
    REFERENCES:

    // Determines how xenos interact with walls, normal nothing, sharp can destroy normal walls and window frame, very sharp reinforced ones.
    #define CLAW_TYPE_NORMAL 1
    #define CLAW_TYPE_SHARP 2
    #define CLAW_TYPE_VERY_SHARP 3

    #define XENO_HITS_TO_DESTROY_WALL 20
    #define XENO_HITS_TO_DESTROY_WINDOW_FRAME 3
    #define XENO_HITS_TO_DESTROY_R_WINDOW_FRAME 5
    #define XENO_HITS_TO_DESTROY_BOLTED_DOOR 10
    #define XENO_HITS_TO_DESTROY_WELDED_DOOR 15
    #define XENO_HITS_TO_EXPOSE_WIRES_MIN 3
    #define XENO_HITS_TO_EXPOSE_WIRES_MAX 4
    #define XENO_HITS_TO_CUT_WIRES 10

    FROM CM-SS13
    */

    private void OnReceiverDamageModify(Entity<ReceiverXenoClawsComponent> ent, ref DamageModifyEvent args)
    {
        var xeno = args.Tool;
        var receiver = ent.Comp;

        // WOLFGATE: no ShouldIgnoreClawLogic upstream; callers that skip claws pass no tool

        if (!_meleeWeaponQuery.HasComp(xeno) || !_xenoClawsQuery.TryComp(xeno, out var claws))
            return;

        var hasRequiredClaws = claws.ClawType.CompareTo(receiver.MinimumClawStrength) >= 0;
        bool hasRequiredTier = false;

        if (receiver.MinimumXenoTier != null)
        {
            hasRequiredTier = _xenoQuery.TryComp(xeno, out var xenoComp) &&
                              xenoComp.Tier >= receiver.MinimumXenoTier;
        }

        if (hasRequiredClaws || hasRequiredTier)
        {
            args.Damage = new DamageSpecifier(_protoManager.Index(_clawsDamageGroup),
                                              receiver.MaxHealth / receiver.HitsToDestroy);
        }
        else
        {
            args.Damage = new DamageSpecifier(_protoManager.Index(_clawsDamageGroup), 0);
        }
    }

    public bool HasClawStrength(EntityUid uid, XenoClawType minimum)
    {
        return _xenoClawsQuery.TryComp(uid, out var claws) &&
               claws.ClawType.CompareTo(minimum) >= 0;
    }

    private void OnAirlockReceiverDamageModify(Entity<AirlockReceiverXenoClawsComponent> ent, ref DamageModifyEvent args)
    {
        var xeno = args.Tool;
        var receiver = ent.Comp;

        // WOLFGATE: no ShouldIgnoreClawLogic upstream; callers that skip claws pass no tool

        // WOLFGATE: only xeno hits; Wolfgate puts this on every airlock, and other melee keeps its normal damage
        if (!_meleeWeaponQuery.HasComp(xeno) || !_xenoClawsQuery.HasComp(xeno))
            return;

        if (!TryComp<DoorComponent>(ent, out var door))
            return;


        var damage = new DamageSpecifier(_protoManager.Index(_clawsDamageGroup), 0);

        if (_xenoClawsQuery.TryComp(xeno, out var claws))
        {
            if (claws.ClawType.CompareTo(receiver.MinimumClawStrength) >= 0)
            {
                // WOLFGATE: RMC only lets claws hurt bolted or welded airlocks; here they break any airlock
                var hits = receiver.HitsToDestroyBolted;
                if (door.State == DoorState.Welded)
                    hits = receiver.HitsToDestroyWelded;

                if (claws.ClawType.CompareTo(XenoClawType.VerySharp) >= 0)
                    hits = Math.Min(hits, receiver.HitsToDestroyVerySharp);

                damage = new DamageSpecifier(_protoManager.Index(_clawsDamageGroup), receiver.MaxHealth / hits);
            }
        }

        args.Damage = damage;
    }
}

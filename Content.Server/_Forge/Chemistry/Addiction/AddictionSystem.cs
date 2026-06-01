using Content.Shared._Forge.Chemistry.Addiction;
using Content.Shared._Forge.CCVar;
using Content.Shared.EntityEffects;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Chemistry.Addiction;

public sealed partial class AddictionSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _protoManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private Dictionary<string, AddictionPrototype> _addictions = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        ValidateAndCachePrototypes();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<AddictionPrototype>())
        {
            _addictions.Clear();
            ValidateAndCachePrototypes();
        }
    }

    private void ValidateAndCachePrototypes()
    {
        foreach (var proto in _protoManager.EnumeratePrototypes<AddictionPrototype>())
        {
            for (var i = 1; i < proto.Stages.Count; i++)
            {
                if (proto.Stages[i].MinTolerance < proto.Stages[i - 1].MinTolerance)
                {
                    Log.Error(
                        $"AddictionPrototype '{proto.ID}': stage {i} has MinTolerance " +
                        $"{proto.Stages[i].MinTolerance} which is less than stage {i - 1} " +
                        $"({proto.Stages[i - 1].MinTolerance}). Stages must be ascending.");
                }
            }

            _addictions[proto.ID] = proto;
        }
    }

    public override void Update(float frameTime)
    {
        if (_gameTiming.Paused)
            return;

        if (!_cfg.GetCVar(ForgeCVars.AddictionSystemEnabled))
            return;

        base.Update(frameTime);

        var query = EntityQueryEnumerator<AddictionComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            comp.TickTimer -= frameTime;
            if (comp.TickTimer > 0f)
                continue;

            comp.TickTimer = comp.TickInterval;
            TickAddictions(uid, comp, comp.TickInterval);
        }
    }

    // Called by AddictiveEffect : EntityEffect
    public void ApplyDose(EntityUid mob, string addictionProtoId, float toleranceGain)
    {
        if (!_addictions.TryGetValue(addictionProtoId, out _))
            return;

        if (!TryComp<AddictionComponent>(mob, out var comp) || !comp.Enabled)
            return;

        if (!comp.Addictions.TryGetValue(addictionProtoId, out var data))
        {
            data = new AddictionData
            {
                PrototypeId = addictionProtoId,
                LastDoseTime = _gameTiming.CurTime,
            };
            comp.Addictions[addictionProtoId] = data;
        }

        if (data.IsNew && data.Tolerance + toleranceGain > 0.05f)
        {
            var locKey = $"addiction-{addictionProtoId}-onset";
            if (Loc.TryGetString(locKey, out var msg))
                _popup.PopupEntity(msg, mob, mob, PopupType.MediumCaution);
            data.IsNew = false;
        }

        data.Tolerance = Math.Clamp(data.Tolerance + toleranceGain, 0f, 1f);
        data.LastDoseTime = _gameTiming.CurTime;
        data.WithdrawalActive = false;
    }

    public void ReduceTolerance(EntityUid mob, string addictionProtoId, float amount)
    {
        if (!TryComp<AddictionComponent>(mob, out var comp))
            return;

        if (!comp.Addictions.TryGetValue(addictionProtoId, out var data))
            return;

        data.Tolerance = Math.Clamp(data.Tolerance - amount, 0f, 1f);
    }

    public void ReduceAllTolerances(EntityUid mob, float amount)
    {
        if (!TryComp<AddictionComponent>(mob, out var comp))
            return;

        foreach (var data in comp.Addictions.Values)
            data.Tolerance = Math.Clamp(data.Tolerance - amount, 0f, 1f);
    }

    private void TickAddictions(EntityUid uid, AddictionComponent comp, float delta)
    {
        var toRemove = new List<string>();
        var now = _gameTiming.CurTime;

        foreach (var (protoId, data) in comp.Addictions)
        {
            if (!_addictions.TryGetValue(protoId, out var proto))
                continue;

            if ((now - data.LastDoseTime).TotalSeconds >= proto.WithdrawalDelay)
                data.WithdrawalActive = true;

            var decayRate = data.WithdrawalActive
                ? proto.WithdrawalDecayRate
                : proto.ToleranceDecayRate;

            data.Tolerance = Math.Clamp(data.Tolerance - decayRate * delta, 0f, 1f);

            if (data.Tolerance <= AddictionData.RemoveThreshold)
            {
                toRemove.Add(protoId);
                if (data.WithdrawalActive)
                {
                    var locKey = $"addiction-{protoId}-recovery";
                    if (Loc.TryGetString(locKey, out var msg))
                        _popup.PopupEntity(msg, uid, uid, PopupType.Medium);
                }
                continue;
            }

            var stage = GetActiveStage(proto, data);
            if (stage == null)
                continue;

            data.NextEffectTimer -= delta;
            if (data.NextEffectTimer > 0f)
                continue;

            data.NextEffectTimer = stage.EffectInterval;

            if (stage.WithdrawalOnly && !data.WithdrawalActive)
                continue;

            if (!ApplyEffects(uid, stage.Effects))
                return;

            if (data.WithdrawalActive)
                ApplyEffects(uid, stage.WithdrawalEffects);
        }

        foreach (var id in toRemove)
            comp.Addictions.Remove(id);
    }

    private bool ApplyEffects(EntityUid uid, List<EntityEffect> effects)
    {
        if (effects.Count == 0)
            return true;

        var args = new EntityEffectBaseArgs(uid, EntityManager);
        foreach (var effect in effects)
        {
            if (TerminatingOrDeleted(uid))
                return false;

            if (effect.ShouldApply(args, _random))
                effect.Effect(args);
        }

        return !TerminatingOrDeleted(uid);
    }

    private static AddictionStageData? GetActiveStage(AddictionPrototype proto, AddictionData data)
    {
        AddictionStageData? result = null;
        foreach (var stage in proto.Stages)
        {
            if (data.Tolerance > stage.MinTolerance)
                result = stage;
            else
                break;
        }
        return result;
    }
}

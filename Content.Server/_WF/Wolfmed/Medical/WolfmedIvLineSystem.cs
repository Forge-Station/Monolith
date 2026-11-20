using System.Numerics;
using Content.Shared.Alert;
using Content.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.Wolfmed.Medical;

/// <summary>
/// The visible IV line, as tgstation draws it: a silver line from whatever feeds the patient (a drip stand, a medic
/// suit's plasma reserve) to the patient, and the IV Connected alert on the patient while any line is in.
/// </summary>
public sealed class WolfmedIvLineSystem : EntitySystem
{
    public static readonly ProtoId<AlertPrototype> Alert = "WFWolfmedIvConnected";

    private static readonly SpriteSpecifier LineSprite =
        new SpriteSpecifier.Rsi(new ResPath("/Textures/_WF/Wolfmed/Effects/iv_line.rsi"), "line");

    private static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AlertsSystem _alerts = default!;

    private TimeSpan _nextPrune;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedIvLinedComponent, ComponentShutdown>(OnLinedShutdown);
    }

    /// <summary>Draws the line from <paramref name="source"/>, offset by <paramref name="offset"/>, to the patient.</summary>
    public void Show(EntityUid source, EntityUid patient, Vector2 offset = default)
    {
        var visuals = EnsureComp<JointVisualsComponent>(source);
        visuals.Sprite = LineSprite;
        visuals.Target = GetNetEntity(patient);
        visuals.OffsetA = offset;
        Dirty(source, visuals);

        EnsureComp<WolfmedIvLinedComponent>(patient).Sources.Add(source);
        _alerts.ShowAlert(patient, Alert);
    }

    /// <summary>Takes the line from <paramref name="source"/> out of the patient; the alert goes with the last one.</summary>
    public void Hide(EntityUid source, EntityUid patient)
    {
        // The target no longer resolves once the patient is deleted, and the line goes then too.
        if (TryComp(source, out JointVisualsComponent? visuals) &&
            (!TryGetEntity(visuals.Target, out var target) || target == patient))
            RemComp<JointVisualsComponent>(source);

        if (TerminatingOrDeleted(patient) || !TryComp(patient, out WolfmedIvLinedComponent? lined))
            return;

        lined.Sources.Remove(source);
        if (lined.Sources.Count == 0)
            RemComp<WolfmedIvLinedComponent>(patient);
    }

    /// <summary>A stand or suit deleted mid-line never says so, so its line is dropped here.</summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextPrune)
            return;
        _nextPrune = now + PruneInterval;

        var query = EntityQueryEnumerator<WolfmedIvLinedComponent>();
        var emptied = new List<EntityUid>();
        while (query.MoveNext(out var uid, out var lined))
        {
            lined.Sources.RemoveWhere(source => TerminatingOrDeleted(source));
            if (lined.Sources.Count == 0)
                emptied.Add(uid);
        }

        foreach (var uid in emptied)
            RemComp<WolfmedIvLinedComponent>(uid);
    }

    private void OnLinedShutdown(Entity<WolfmedIvLinedComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            _alerts.ClearAlert(ent, Alert);
    }
}

/// <summary>On a patient with an IV or suit line in: what feeds them, so the alert stays until the last is out.</summary>
[RegisterComponent]
public sealed partial class WolfmedIvLinedComponent : Component
{
    [ViewVariables]
    public HashSet<EntityUid> Sources = new();
}

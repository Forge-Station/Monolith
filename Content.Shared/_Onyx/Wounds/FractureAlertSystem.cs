using Content.Shared.Alert;
using Content.Shared.Body;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared._WF.Wolfmed.Body; // WOLFGATE(Wolfmed): D8 keeps Onyx's part fields on WolfmedBodyPartComponent.
using Robust.Shared.Prototypes;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class FractureAlertSystem : EntitySystem
{
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WoundFractureSystem _fractures = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WolfmedBodyPartSystem _wfPart = default!; // WOLFGATE(Wolfmed): D8, Onyx's extra part fields.

    public void Refresh(EntityUid? body)
    {
        if (body is not { } uid)
            return;

        var alerts = new Dictionary<ProtoId<AlertPrototype>, bool>();
        foreach (var (part, bodyPart) in _body.GetBodyChildren(uid))
        {
            // WOLFGATE(Wolfmed): D8, FractureProfile lives on WolfmedBodyPartComponent, not Shitmed's BodyPartComponent.
            if (_wfPart.Get(part).FractureProfile is not { } profileId ||
                !_prototypes.TryIndex(profileId, out FractureProfilePrototype? profile) ||
                profile.Alert is not { } alert)
                continue;

            alerts.TryAdd(alert, false);
            if (_fractures.GetFracture(part) is { } fracture &&
                fracture.Comp2.Grade >= profile.AlertMinimumGrade &&
                !profile.AlertHiddenTreatments.Contains(fracture.Comp2.Treatment))
                alerts[alert] = true;
        }

        foreach (var (alert, active) in alerts)
            if (active)
                _alerts.ShowAlert(uid, alert);
            else
                _alerts.ClearAlert(uid, alert);

        foreach (var profile in _prototypes.EnumeratePrototypes<FractureProfilePrototype>())
            if (profile.Alert is { } alert && !alerts.ContainsKey(alert))
                _alerts.ClearAlert(uid, alert);
    }
}

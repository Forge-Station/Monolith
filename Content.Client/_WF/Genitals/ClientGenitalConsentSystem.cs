using Content.Client._Common.Consent;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Prototypes;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Genitals;

/// <summary>Client side of the anatomy consent checks: the local viewer gate for sprites and the surgery list filter.</summary>
public sealed partial class ClientGenitalConsentSystem : GenitalConsentSystem
{
    [Dependency] private IClientConsentManager _consentManager = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedGenitalsSystem _genitals = default!;

    /// <summary>The local player's saved master switch. False until the server has sent consent settings, so it fails closed.</summary>
    public bool ViewerHasMaster()
    {
        return _consentManager.HasLoaded
               && _consentManager.GetConsentSettings().Toggles.TryGetValue(_genitals.Settings.MasterConsent, out var state)
               && state == "on";
    }

    /// <summary>
    /// The local viewer's character is adult, or is not a humanoid (a ghost or observer), the rule examiners follow. True
    /// without an attached entity (the lobby).
    /// </summary>
    public bool ViewerIsAdult()
    {
        return _player.LocalEntity is not { } viewer
               || !HasComp<HumanoidAppearanceComponent>(viewer)
               || _genitals.IsAdult(viewer);
    }

    /// <summary>
    /// Kill switch, the viewer's master switch and an adult or non-humanoid viewer first; preview dolls and the own body
    /// then pass, other bodies need TargetConsents.
    /// </summary>
    public override bool CanViewerSee(EntityUid target)
    {
        if (!AnatomyEnabled || !ViewerHasMaster() || !ViewerIsAdult())
            return false;

        // Lobby dolls only ever show the local player's own profiles.
        if (IsClientSide(target) && TryComp<GenitalsComponent>(target, out var genitals) && genitals.IsPreview)
            return true;

        if (_player.LocalEntity == target)
            return true;

        return TargetConsents(target);
    }

    /// <summary>
    /// Removes anatomy surgeries unless the local player passes the surgeon half of CanOperate: kill switch, master switch
    /// and an adult humanoid body. The server refuses the others anyway.
    /// </summary>
    public override Dictionary<NetEntity, List<EntProtoId>> FilterSurgeryChoices(Dictionary<NetEntity, List<EntProtoId>> choices)
    {
        if (AnatomyEnabled && ViewerHasMaster() && _player.LocalEntity is { } surgeon && _genitals.IsAdult(surgeon))
            return choices;

        var filtered = new Dictionary<NetEntity, List<EntProtoId>>(choices.Count);
        foreach (var (part, surgeries) in choices)
        {
            var kept = new List<EntProtoId>(surgeries.Count);
            foreach (var surgery in surgeries)
            {
                if (!IsAnatomySurgery(surgery))
                    kept.Add(surgery);
            }

            // Drop a part only when filtering emptied it.
            if (kept.Count > 0 || surgeries.Count == 0)
                filtered[part] = kept;
        }

        return filtered;
    }

    /// <summary>Whether the surgery prototype has GenitalSurgeryComponent. Reads the prototype; spawns nothing.</summary>
    public bool IsAnatomySurgery(EntProtoId surgery)
    {
        return _proto.TryIndex(surgery, out var proto) && proto.HasComponent<GenitalSurgeryComponent>(_factory);
    }
}

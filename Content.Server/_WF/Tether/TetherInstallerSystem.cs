using Content.Shared._WF.Tether;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Content.Shared.Materials;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Tether;

/// <summary>
/// Spends stored steel to bolt a <see cref="RopeAttachPointComponent"/> eye to a tile at range,
/// refusing space and tiles that already hold one.
/// </summary>
public sealed class TetherInstallerSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMaterialStorageSystem _materials = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TetherInstallerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<TetherInstallerComponent, TetherInstallDoAfterEvent>(OnInstallDoAfter);
        SubscribeLocalEvent<TetherInstallerComponent, ExaminedEvent>(OnExamined);
    }

    private void OnAfterInteract(Entity<TetherInstallerComponent> ent, ref AfterInteractEvent args)
    {
        // CanReach is the stock 1.5 m; the installer has its own longer range, checked below.
        if (args.Handled)
            return;

        var location = args.ClickLocation;
        if (!location.IsValid(EntityManager))
            return;

        args.Handled = true;
        var user = args.User;

        if (!_interaction.InRangeUnobstructed(user, location, ent.Comp.Range))
        {
            _popup.PopupEntity(Loc.GetString("tether-installer-popup-too-far"), user, user);
            return;
        }

        if (!_turf.TryGetTileRef(location, out var tileRef))
            return;

        if (_turf.IsSpace(tileRef.Value))
        {
            _popup.PopupEntity(Loc.GetString("tether-installer-popup-space"), user, user);
            return;
        }

        foreach (var occupant in _turf.GetEntitiesInTile(location))
        {
            if (MetaData(occupant).EntityPrototype?.ID == ent.Comp.AnchorEyePrototype.Id)
            {
                _popup.PopupEntity(Loc.GetString("tether-installer-popup-occupied"), user, user);
                return;
            }
        }

        if (_materials.GetMaterialAmount(ent.Owner, ent.Comp.Material) < ent.Comp.MaterialPerInstall)
        {
            _popup.PopupEntity(Loc.GetString("tether-installer-popup-need-steel"), user, user);
            return;
        }

        var coordinates = _turf.GetTileCenter(tileRef.Value);
        _audio.PlayPvs(ent.Comp.StartSound, ent.Owner);

        var ev = new TetherInstallDoAfterEvent(GetNetCoordinates(coordinates));
        var doAfterArgs = new DoAfterArgs(EntityManager, user, ent.Comp.InstallDelay, ev, ent.Owner, used: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            RequireCanInteract = true,
            BlockDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnInstallDoAfter(Entity<TetherInstallerComponent> ent, ref TetherInstallDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var coordinates = GetCoordinates(args.Coordinates);
        if (!coordinates.IsValid(EntityManager) || !_turf.TryGetTileRef(coordinates, out var tileRef))
            return;

        if (_turf.IsSpace(tileRef.Value))
            return;

        // The tile may have moved with its grid during the delay.
        if (!_interaction.InRangeUnobstructed(args.User, coordinates, ent.Comp.Range))
            return;

        foreach (var occupant in _turf.GetEntitiesInTile(coordinates))
        {
            if (MetaData(occupant).EntityPrototype?.ID == ent.Comp.AnchorEyePrototype.Id)
                return;
        }

        if (!_materials.TryChangeMaterialAmount(ent.Owner, ent.Comp.Material, -ent.Comp.MaterialPerInstall))
            return;

        args.Handled = true;
        var eyeCoordinates = _turf.GetTileCenter(tileRef.Value);
        Spawn(ent.Comp.AnchorEyePrototype, eyeCoordinates);
        _audio.PlayPvs(ent.Comp.InstallSound, eyeCoordinates);
        _popup.PopupEntity(Loc.GetString("tether-installer-popup-installed"), ent.Owner, args.User);
    }

    private void OnExamined(Entity<TetherInstallerComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var remaining = _materials.GetMaterialAmount(ent.Owner, ent.Comp.Material) / Math.Max(ent.Comp.MaterialPerInstall, 1);
        args.PushMarkup(Loc.GetString("tether-installer-examine-remaining", ("count", remaining)));
    }
}

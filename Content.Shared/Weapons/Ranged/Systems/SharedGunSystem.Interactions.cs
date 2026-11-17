using Content.Shared.Actions;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Tools.Systems;
using Content.Shared.UserInterface;
using Content.Shared.DoAfter;
using Content.Shared._NF.Species.Components;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    private void OnExamine(EntityUid uid, GunComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !component.ShowExamineText)
            return;

        using (args.PushGroup(nameof(GunComponent)))
        {
            args.PushMarkup(Loc.GetString("gun-selected-mode-examine", ("color", ModeExamineColor),
                ("mode", GetLocSelector(component.SelectedMode))));

            if (component.DamageModifier != 1f)
                args.PushMarkup(Loc.GetString("gun-damage-modifier-examine", ("color", FireRateExamineColor),
                    ("damage", $"{component.DamageModifier.ToString("#.##")}")));

            //args.PushMarkup(Loc.GetString("gun-fire-rate-examine", ("color", FireRateExamineColor), // Emberfall
            //    ("fireRate", $"{component.FireRateModified:0.0}"))); // Emberfall
            /// Forge-Change-Start
            if (component.DeleteOnShoot)
            {
                if (!string.IsNullOrEmpty(component.ExamineTextSabotaged))
                    args.PushMarkup(Loc.GetString(component.ExamineTextSabotaged));
            }
            /// Forge-Change-End
        }
    }

    private string GetLocSelector(SelectiveFire mode)
    {
        return Loc.GetString($"gun-{mode.ToString()}");
    }

    private void OnAltVerb(EntityUid uid, GunComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || component.SelectedMode == component.AvailableModes)
            return;

        var nextMode = GetNextMode(component);

        AlternativeVerb verb = new()
        {
            Act = () => SelectFire(uid, component, nextMode, args.User),
            Text = Loc.GetString("gun-selector-verb", ("mode", GetLocSelector(nextMode))),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/fold.svg.192dpi.png")),
        };

        args.Verbs.Add(verb);
    }

    private SelectiveFire GetNextMode(GunComponent component)
    {
        var modes = new List<SelectiveFire>();

        foreach (var mode in Enum.GetValues<SelectiveFire>())
        {
            if ((mode & component.AvailableModes) == 0x0)
                continue;

            modes.Add(mode);
        }

        var index = modes.IndexOf(component.SelectedMode);
        return modes[(index + 1) % modes.Count];
    }

    private void SelectFire(EntityUid uid, GunComponent component, SelectiveFire fire, EntityUid? user = null)
    {
        if (component.SelectedMode == fire)
            return;

        DebugTools.Assert((component.AvailableModes  & fire) != 0x0);
        component.SelectedMode = fire;

        if (!Paused(uid))
        {
            var curTime = Timing.CurTime;
            var cooldown = TimeSpan.FromSeconds(InteractNextFire);

            if (component.NextFire < curTime)
                component.NextFire = curTime + cooldown;
            else
                component.NextFire += cooldown;
        }

        Audio.PlayPredicted(component.SoundMode, uid, user);
        Popup(Loc.GetString("gun-selected-mode", ("mode", GetLocSelector(fire))), uid, user);
        Dirty(uid, component);
        RefreshModifiers((uid, component), user); // Forge-Change: update spread and other modifiers per fire mode
    }

    /// <summary>
    /// Cycles the gun's <see cref="SelectiveFire"/> to the next available one.
    /// </summary>
    public void CycleFire(EntityUid uid, GunComponent component, EntityUid? user = null)
    {
        // Noop
        if (component.SelectedMode == component.AvailableModes)
            return;

        DebugTools.Assert((component.AvailableModes & component.SelectedMode) == component.SelectedMode);
        var nextMode = GetNextMode(component);
        SelectFire(uid, component, nextMode, user);
    }

    // TODO: Actions need doing for guns anyway.
    private sealed partial class CycleModeEvent : InstantActionEvent
    {
        public SelectiveFire Mode = default;
    }

    private void OnCycleMode(EntityUid uid, GunComponent component, CycleModeEvent args)
    {
        SelectFire(uid, component, args.Mode, args.Performer);
    }

    private void OnGunSelected(EntityUid uid, GunComponent component, HandSelectedEvent args)
    {
        if (Timing.ApplyingState)
             return;

        if (component.FireRateModified <= 0)
            return;

        var fireDelay = 1f / component.FireRateModified;
        if (fireDelay.Equals(0f))
            return;

        if (!component.ResetOnHandSelected)
            return;

        if (Paused(uid))
            return;

        // If someone swaps to this weapon then reset its cd.
        var curTime = Timing.CurTime;
        var minimum = curTime + TimeSpan.FromSeconds(fireDelay);

        if (minimum < component.NextFire)
            return;

        component.NextFire = minimum;
        Dirty(uid, component);
    }
    /// Forge-Change-Start

    private void OnScrewdriverSabotage(EntityUid uid, GunComponent gun, SabotageDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        if (!SabotageGun(uid, gun, !gun.DeleteOnShoot, args.User))
            return;

        Logs.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(args.User):user} screwed {ToPrettyString(uid):target}'s gun to {(gun.DeleteOnShoot ? "sabotage" : "defuse")}");

        Audio.PlayPredicted(gun.SoundSabotage, uid, args.User);
        args.Handled = true;
    }

    private void OnInteractUsing(Entity<GunComponent> gun, ref InteractUsingEvent args)
    {
        /// <summary>
        /// Forge: screwdriver can sabotage the gun.
        /// </summary>
        if(!gun.Comp.Sabotageable)
            return;

        if (!Tool.HasQuality(args.Used, gun.Comp.SabotageTool))
            return;

        if (!CanSabotaged(gun, args.User))
            return;

        if (!(HasComp<GoblinComponent>(args.User) && Tool.UseTool(
                args.Used,
                args.User,
                gun,
                (float) gun.Comp.SabotageDelay.TotalSeconds,
                gun.Comp.SabotageTool,
                new SabotageDoAfterEvent())))
        {
            return;
        }

        Logs.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.User):user} is screwing {ToPrettyString(gun):target}'s gun that was {(gun.Comp.DeleteOnShoot ? "sabotaged" : "defused")} at {Transform(gun).Coordinates:targetlocation}");
        args.Handled = true;
    }

    public bool SabotageGun(EntityUid uid, GunComponent gun, bool sabotaged, EntityUid? user = null)
    {
        if (!CanSabotaged((uid, gun), user))
            return false;

        gun.DeleteOnShoot = sabotaged;
        Dirty(uid, gun);

        return true;
    }

    public bool CanSabotaged(Entity<GunComponent> ent, EntityUid? user)
    {
        var attempt = new AttemptChangeDeleteOnShootEvent(ent.Comp.DeleteOnShoot, user);
        RaiseLocalEvent(ent, ref attempt);
        return !attempt.Cancelled;
    }
    /// Forge-Change-End
}

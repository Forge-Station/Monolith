using Content.Shared._WF.Xenonids.Evolution;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.Parasite;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Shared._WF.Xenonids.Announce;

public abstract partial class SharedXenoAnnounceSystem : EntitySystem
{
    [Dependency] private XenoEvolutionSystem _xenoEvolution = default!;

    // WOLFGATE: RMC's bioscan sound, kept here since bioscans are cut
    private static readonly SoundSpecifier QueenSound = new SoundCollectionSpecifier("XenoQueenCommand", AudioParams.Default.WithVolume(-6));

    [Dependency] protected SharedXenoHiveSystem Hive = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<XenoAnnounceDeathComponent, MobStateChangedEvent>(OnAnnounceDeathMobStateChanged);
    }

    private void OnAnnounceDeathMobStateChanged(Entity<XenoAnnounceDeathComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        // WOLFGATE: areas are cut; the grid's name stands in for the location
        var locationName = Loc.GetString("wf-xeno-announce-unknown-location");
        if (Transform(ent).GridUid is { } grid)
            locationName = Name(grid);

        if (HasComp<ParasiteSpentComponent>(ent))
            AnnounceSameHive(ent.Owner, Loc.GetString("rmc-xeno-parasite-announce-infect", ("xeno", ent.Owner), ("location", locationName)), color: ent.Comp.Color, useHiveAsSource: true);
        else
        {
            if (HasComp<XenoEvolutionGranterComponent>(ent) || _xenoEvolution.HasLiving<XenoEvolutionGranterComponent>(1))
                AnnounceSameHive(ent.Owner, Loc.GetString(ent.Comp.Message, ("xeno", ent.Owner), ("location", locationName)), color: ent.Comp.Color, useHiveAsSource: true);
        }
    }

    public string WrapHive(string message, Color? color = null)
    {
        color ??= Color.FromHex("#921992");
        return $"[color={color.Value.ToHex()}][font size=16][bold]{message}[/bold][/font][/color]\n";
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="source"></param>
    /// <param name="filter"></param>
    /// <param name="message">Message to send into chat</param>
    /// <param name="wrapped"></param>
    /// <param name="sound"></param>
    /// <param name="popup"></param>
    /// <param name="needsQueen">Whether the message can only be sent if the hive has an active queen</param>
    public virtual void Announce(EntityUid source,
        Filter filter,
        string message,
        string wrapped,
        SoundSpecifier? sound = null,
        PopupType? popup = null,
        bool needsQueen = false)
    {
    }

    public void AnnounceToHive(EntityUid source,
        EntityUid hive,
        string message,
        SoundSpecifier? sound = null,
        PopupType? popup = null,
        Color? color = null,
        bool needsQueen = false)
    {
        var filter = Filter.Empty().AddWhereAttachedEntity(e => Hive.IsMember(e, hive));
        Announce(source, filter, message, WrapHive(message, color), sound, popup, needsQueen);
    }

    public void AnnounceSameHive(Entity<HiveMemberComponent?> xeno,
        string message,
        SoundSpecifier? sound = null,
        PopupType? popup = null,
        Color? color = null,
        bool needsQueen = false,
        bool useHiveAsSource = false)
    {
        if (Hive.GetHive(xeno) is not { } hive)
            return;

        AnnounceToHive(useHiveAsSource ? hive : xeno, hive, message, sound, popup, color, needsQueen);
    }

    public void AnnounceSameHiveDefaultSound(Entity<HiveMemberComponent?> xeno,
        string message,
        PopupType? popup = null,
        Color? color = null,
        bool needsQueen = false)
    {
        var sound = QueenSound;
        AnnounceSameHive(xeno, message, sound, popup, color, needsQueen);
    }

    public void AnnounceAll(EntityUid source,
        string message,
        SoundSpecifier? sound = null,
        PopupType? popup = null,
        bool needsQueen = false)
    {
        Announce(
            source,
            Filter.Empty().AddWhereAttachedEntity(HasComp<XenoComponent>),
            message,
            message,
            sound,
            popup,
            needsQueen
        );
    }

    public void AnnounceQueenMother(string message)
    {
        var sound = QueenSound;
        AnnounceAll(default, FormatQueenMother(message), sound);
    }

    public string FormatQueenMother(string message)
    {
        return $"\n[bold][color=#7575F3][font size=24]Queen Mother Psychic Directive[/font][/color][/bold]\n\n[color=red][font size=14]{message}[/font][/color]\n\n";
    }
}

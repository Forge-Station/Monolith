using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Administration;
using Content.Shared.Administration.Logs;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Genitals;

/// <summary>
/// Anatomy admin verbs: reapply the owner's profile, clear runtime state and a private summary. Admins see them only with
/// their own Adult content on; other admins see one locked verb and no anatomy data.
/// </summary>
/// <remarks>Admin logs are exempt from the viewer gate: they are a moderation tool.</remarks>
public sealed partial class GenitalAdminVerbSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;
    [Dependency] private GenitalsSystem _genitals = default!;
    [Dependency] private GenitalOrganSystem _organs = default!;
    [Dependency] private GenitalConsentSystem _consent = default!;
    [Dependency] private SharedArousalSystem _arousal = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Broadcast: SharedUndergarmentStripSystem owns the directed (GenitalsComponent, GetVerbsEvent<Verb>) pair.
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    /// <summary>The three anatomy verbs for admins with their own Adult content on; one locked, neutral verb for other admins.</summary>
    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!HasComp<GenitalsComponent>(args.Target)
            || !TryComp<ActorComponent>(args.User, out var actor)
            || !_admin.HasAdminFlag(actor.PlayerSession, AdminFlags.Admin))
            return;

        var admin = actor.PlayerSession;
        if (!_genitals.PlayerHasMaster(admin.UserId))
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("wf-anatomy-admin-verb-locked"),
                Message = Loc.GetString("wf-anatomy-admin-verb-locked-message"),
                Category = VerbCategory.Admin,
                Disabled = true,
            });
            return;
        }

        var user = args.User;
        var target = args.Target;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wf-anatomy-admin-verb-reapply"),
            Message = Loc.GetString("wf-anatomy-admin-verb-reapply-message"),
            Category = VerbCategory.Admin,
            Impact = LogImpact.Medium,
            ConfirmationPopup = true,
            Act = () => Reapply(admin, user, target),
        });
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wf-anatomy-admin-verb-clear"),
            Message = Loc.GetString("wf-anatomy-admin-verb-clear-message"),
            Category = VerbCategory.Admin,
            Impact = LogImpact.Medium,
            ConfirmationPopup = true,
            Act = () => ClearRuntime(admin, user, target),
        });
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wf-anatomy-admin-verb-dump"),
            Message = Loc.GetString("wf-anatomy-admin-verb-dump-message"),
            Category = VerbCategory.Admin,
            Impact = LogImpact.Medium,
            Act = () => Dump(admin, user, target),
        });
    }

    /// <summary>
    /// Stores the controlling player's selected profile as the body's template and resets runtime state to it. Organs are
    /// rebuilt now only while the owner consents; otherwise the consent reaction builds them later.
    /// </summary>
    public void Reapply(ICommonSession admin, EntityUid user, EntityUid target)
    {
        if (!TryComp<GenitalsComponent>(target, out var genitals)
            || !TryComp<HumanoidAppearanceComponent>(target, out var humanoid))
            return;

        if (GetOwnerProfile(target) is not { } profile)
        {
            Reply(admin, Loc.GetString("wf-anatomy-admin-no-profile", ("target", target)));
            return;
        }

        // Anatomy follows the body, as on a profile load: a body that is no longer eligible loses it.
        if (!_genitals.IsEligible(target))
        {
            _organs.RemoveAnatomy(target);
            _adminLog.Add(LogType.WFAnatomy,
                LogImpact.Medium,
                $"{ToPrettyString(user):actor} reapplied the anatomy profile of {ToPrettyString(target):entity}, which is no longer eligible: anatomy removed");
            Reply(admin, Loc.GetString("wf-anatomy-admin-ineligible", ("target", target)));
            return;
        }

        var config = GenitalProfileValidator.EnsureValid(profile.Genitals, humanoid.Age, humanoid.Species.Id, _proto);
        genitals.SourceProfile = config;
        genitals.OrgansBuilt = false;
        ResetRuntime((target, genitals));

        var build = _genitals.AnatomyEnabled && _consent.HasMaster(target);
        if (build)
            _organs.BuildOrgans(target, config);

        _organs.Recompute(target);

        _adminLog.Add(LogType.WFAnatomy,
            LogImpact.Medium,
            $"{ToPrettyString(user):actor} reapplied the anatomy profile of {ToPrettyString(target):entity}");
        Reply(admin, Loc.GetString(build ? "wf-anatomy-admin-reapplied" : "wf-anatomy-admin-reapplied-pending", ("target", target)));
    }

    /// <summary>Resets arousal, undergarments, reveal mode and visibility to the template's defaults; organs and template stay.</summary>
    public void ClearRuntime(ICommonSession admin, EntityUid user, EntityUid target)
    {
        if (!TryComp<GenitalsComponent>(target, out var genitals))
            return;

        ResetRuntime((target, genitals));

        _adminLog.Add(LogType.WFAnatomy,
            LogImpact.Medium,
            $"{ToPrettyString(user):actor} cleared the anatomy runtime state of {ToPrettyString(target):entity}");
        Reply(admin, Loc.GetString("wf-anatomy-admin-cleared", ("target", target)));
    }

    /// <summary>Sends the summary to this admin only. Not an examine line: ExaminedEvent belongs to GenitalExamineSystem.</summary>
    public void Dump(ICommonSession admin, EntityUid user, EntityUid target)
    {
        if (!TryComp<GenitalsComponent>(target, out var genitals))
            return;

        Reply(admin, BuildSummary((target, genitals)));

        _adminLog.Add(LogType.WFAnatomy,
            LogImpact.Medium,
            $"{ToPrettyString(user):actor} viewed the anatomy summary of {ToPrettyString(target):entity}");
    }

    /// <summary>Clinical one-screen summary: body, organs, runtime state, template and consent toggles, one line each.</summary>
    public string BuildSummary(Entity<GenitalsComponent> ent)
    {
        var (body, genitals) = ent;
        var settings = _genitals.Settings;
        var lines = new List<string>
        {
            Loc.GetString("wf-anatomy-admin-dump-header", ("target", ToPrettyString(body).ToString())),
        };

        if (TryComp<HumanoidAppearanceComponent>(body, out var humanoid))
        {
            lines.Add(Loc.GetString("wf-anatomy-admin-dump-body",
                ("species", humanoid.Species.Id),
                ("age", humanoid.Age),
                ("eligible", YesNo(_genitals.IsEligible(body)))));
        }

        // The organs themselves, not the mirror: the mirror is empty while the owner's switch is off.
        var organCount = 0;
        if (TryComp<BodyComponent>(body, out var bodyComp))
        {
            var organs = _body.GetBodyOrganEntityComps<GenitalOrganComponent>((body, bodyComp));
            organs.Sort((a, b) => a.Comp1.Slot.CompareTo(b.Comp1.Slot));
            foreach (var organ in organs)
            {
                lines.Add(OrganLine(organ.Comp1.Slot, organ.Comp1.State));
            }

            organCount = organs.Count;
        }

        if (organCount == 0)
            lines.Add(Loc.GetString("wf-anatomy-admin-dump-no-organs"));

        var cooldown = Math.Max(0, (int) Math.Ceiling((genitals.StripCooldownUntil - _timing.CurTime).TotalSeconds));
        lines.Add(Loc.GetString("wf-anatomy-admin-dump-runtime",
            ("arousal", (int) genitals.Arousal),
            ("state", SharedArousalSystem.ToState(genitals.Arousal, settings).ToString()),
            ("reveal", genitals.RevealMode.ToString()),
            ("undergarments", genitals.Undergarments.ToString()),
            ("cooldown", cooldown)));

        var visibility = genitals.Visibility;
        lines.Add(Loc.GetString("wf-anatomy-admin-dump-visibility",
            ("penis", visibility.Penis.ToString()),
            ("testicles", visibility.Testicles.ToString()),
            ("vagina", visibility.Vagina.ToString()),
            ("breasts", visibility.Breasts.ToString())));

        lines.Add(Loc.GetString("wf-anatomy-admin-dump-template",
            ("template", TemplateText(genitals.SourceProfile)),
            ("built", YesNo(genitals.OrgansBuilt)),
            ("mirror", YesNo(genitals.HasAnyOrgan))));

        lines.Add(Loc.GetString("wf-anatomy-admin-dump-consent",
            ("master", YesNo(_consent.HasMaster(body))),
            ("strip", YesNo(settings.StripConsent is { } strip && _consent.HasStrictConsent(body, strip))),
            ("surgery", YesNo(settings.SurgeryConsent is { } surgery && _consent.HasStrictConsent(body, surgery))),
            ("present", YesNo(_consent.IsActivelyPresent(body)))));

        return string.Join("\n", lines);
    }

    /// <summary>Arousal 0, undergarments worn, reveal mode and visibility from the template (defaults without one).</summary>
    private void ResetRuntime(Entity<GenitalsComponent> ent)
    {
        var (body, genitals) = ent;
        var (reveal, visibility) = GenitalStateBuilder.RuntimeDefaults(genitals.SourceProfile);
        var changed = false;

        if (genitals.RevealMode != reveal)
        {
            genitals.RevealMode = reveal;
            DirtyField(body, genitals, nameof(GenitalsComponent.RevealMode));
            changed = true;
        }

        if (genitals.Visibility != visibility)
        {
            genitals.Visibility = visibility;
            DirtyField(body, genitals, nameof(GenitalsComponent.Visibility));
            changed = true;
        }

        if (genitals.Undergarments != UndergarmentFlags.None)
        {
            genitals.Undergarments = UndergarmentFlags.None;
            DirtyField(body, genitals, nameof(GenitalsComponent.Undergarments));
            changed = true;
        }

        if (changed)
        {
            var ev = new GenitalsVisualsChangedEvent();
            RaiseLocalEvent(body, ref ev);
        }

        _arousal.ResetArousal(ent);
    }

    /// <summary>The selected character profile of the player whose mind is in the body; null without a connected player.</summary>
    private HumanoidCharacterProfile? GetOwnerProfile(EntityUid body)
    {
        if (!_mind.TryGetMind(body, out _, out var mind) || mind.UserId is not { } userId)
            return null;

        return _prefs.GetPreferencesOrNull(userId)?.SelectedCharacter as HumanoidCharacterProfile;
    }

    private string OrganLine(GenitalSlot slot, GenitalOrganState state)
    {
        var colour = state.Color.ToHex();
        return slot switch
        {
            GenitalSlot.Penis => Loc.GetString("wf-anatomy-admin-dump-penis",
                ("shape", ShapeName(state.Shape)),
                ("length", (int) state.LengthCm),
                ("step", (int) state.Step),
                ("sheath", state.Sheath.ToString()),
                ("colour", colour)),
            GenitalSlot.Testicles => Loc.GetString("wf-anatomy-admin-dump-testicles",
                ("type", state.Testicles.ToString()),
                ("step", (int) state.Step),
                ("colour", colour)),
            GenitalSlot.Vagina => Loc.GetString("wf-anatomy-admin-dump-vagina",
                ("shape", ShapeName(state.Shape)),
                ("colour", colour)),
            GenitalSlot.Womb => Loc.GetString("wf-anatomy-admin-dump-womb"),
            GenitalSlot.Breasts => Loc.GetString("wf-anatomy-admin-dump-breasts",
                ("shape", ShapeName(state.Shape)),
                ("cup", (int) state.Step),
                ("lactation", YesNo(state.Lactation)),
                ("colour", colour)),
            _ => slot.ToString(),
        };
    }

    private string ShapeName(ProtoId<GenitalShapePrototype>? shape)
    {
        if (shape is not { } id)
            return Loc.GetString("wf-anatomy-admin-none");

        return _proto.TryIndex(id, out var proto) ? Loc.GetString(proto.Name) : id.Id;
    }

    /// <summary>The organs the template configures, or that it is unset.</summary>
    private string TemplateText(GenitalProfile? template)
    {
        if (template == null)
            return Loc.GetString("wf-anatomy-admin-template-unset");

        var slots = new List<string>();
        if (template.Penis != null)
            slots.Add(nameof(GenitalSlot.Penis));

        if (template.Testicles != null)
            slots.Add(nameof(GenitalSlot.Testicles));

        if (template.Vagina != null)
            slots.Add(nameof(GenitalSlot.Vagina));

        if (template.Womb)
            slots.Add(nameof(GenitalSlot.Womb));

        if (template.Breasts != null)
            slots.Add(nameof(GenitalSlot.Breasts));

        return slots.Count == 0 ? Loc.GetString("wf-anatomy-admin-none") : string.Join(", ", slots);
    }

    private string YesNo(bool value)
    {
        return Loc.GetString(value ? "wf-anatomy-admin-yes" : "wf-anatomy-admin-no");
    }

    /// <summary>Private to this admin. The chat log is skipped; the action is logged under WFAnatomy instead.</summary>
    private void Reply(ICommonSession admin, string message)
    {
        _chat.DispatchServerMessage(admin, message, suppressLog: true);
    }
}

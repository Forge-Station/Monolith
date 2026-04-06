using System.Linq;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server._Forge.Access.Systems;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared._Forge.Access.Components;
using Robust.Shared.GameObjects;

namespace Content.Server._Forge.Access;

public sealed class JobReassignmentInjectorSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly JobReassignmentSystem _reassignment = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JobReassignmentInjectorComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<JobReassignmentInjectorComponent, JobReassignmentInjectorDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteract(EntityUid uid, JobReassignmentInjectorComponent component, AfterInteractEvent args)
    {
        if (args.Handled
            || !args.CanReach
            || args.Target is not { Valid: true } target
            || component.JobPrototype is not { } jobId
            || !_mind.TryGetMind(target, out _, out _))
        {
            return;
        }

        var userMessage = Loc.GetString("job-reassignment-injector-popup-start-user",
            ("target", Identity.Entity(target, EntityManager)));
        _popup.PopupEntity(userMessage, target, args.User);

        if (args.User != target)
        {
            var targetMessage = Loc.GetString("job-reassignment-injector-popup-start-target",
                ("user", Identity.Entity(args.User, EntityManager)));
            _popup.PopupEntity(targetMessage, args.User, target, PopupType.LargeCaution);
        }

        var doAfter = new DoAfterArgs(EntityManager, args.User, component.InjectDelay,
            new JobReassignmentInjectorDoAfterEvent(), uid, target: target, used: uid)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = component.NeedHand,
            BreakOnHandChange = component.BreakOnHandChange,
            MovementThreshold = component.MovementThreshold,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            args.Handled = true;
    }

    private void OnDoAfter(EntityUid uid, JobReassignmentInjectorComponent component, ref JobReassignmentInjectorDoAfterEvent args)
    {
        if (args.Cancelled
            || args.Handled
            || args.Target is not { Valid: true } target
            || component.JobPrototype is not { } jobId)
        {
            return;
        }

        var authorizedTags = component.AuthorizedAccess.Count > 0
            ? component.AuthorizedAccess.ToHashSet()
            : null;
        var bodyImplants = component.BodyImplants.Count > 0
            ? component.BodyImplants
            : null;

        if (!_reassignment.TryApplyToEntity(target, jobId, authorizedTags, args.User, extraImplants: bodyImplants))
        {
            _popup.PopupEntity(Loc.GetString("job-reassignment-injector-popup-fail"), target, args.User);
            QueueDel(uid);
            args.Handled = true;
            return;
        }

        if (_reassignment.TryResolveJobData(jobId, out var data))
        {
            var successMessage = Loc.GetString("job-reassignment-injector-popup-success-user",
                ("target", Identity.Entity(target, EntityManager)),
                ("job", data.Job.LocalizedName));
            _popup.PopupEntity(successMessage, target, args.User);

            var targetMessage = Loc.GetString("job-reassignment-injector-popup-success-target",
                ("job", data.Job.LocalizedName));
            _popup.PopupEntity(targetMessage, target, target);
        }

        QueueDel(uid);
        args.Handled = true;
    }
}

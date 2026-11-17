using Content.Shared.Verbs;
using Robust.Shared.Player;

namespace Content.Server._Forge.Company;

public sealed class CompanyContractBoardSystem : EntitySystem
{
    [Dependency] private CompanyManagementCartridgeSystem _companyUi = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CompanyContractBoardComponent, GetVerbsEvent<AlternativeVerb>>(OnVerb);
    }

    private void OnVerb(EntityUid uid, CompanyContractBoardComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !HasComp<ActorComponent>(args.User))
            return;

        var user = args.User;
        var board = uid;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("company-job-board-verb"),
            Act = () => { _ = _companyUi.TurnInAtBoard(user, board); },
        });
    }
}

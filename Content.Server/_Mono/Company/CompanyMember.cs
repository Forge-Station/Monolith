using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

public struct CompanyMemberRecord
{
    public Guid PlayerUserId;
    public int CharacterSlot;
    public string CharacterName;
    public string LastSeenUserName;
    public bool Owner;
    public ProtoId<CompanyPrototype> Company;
    public Guid? RoleId;
}

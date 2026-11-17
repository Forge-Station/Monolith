namespace Content.Shared._Forge.Company;

public enum CompanyRelationKind : byte
{
    Neutral = 0,
    War = 1,
    Alliance = 2,
}

public enum CompanyInvitationStatus : byte
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Cancelled = 3,
}

public enum CompanyLogType : byte
{
    Joined = 0,
    Left = 1,
    Kicked = 2,
    Invited = 3,
    WarDeclared = 4,
    AllianceDeclared = 5,
    RelationCleared = 6,
    BankDeposit = 7,
    BankWithdraw = 8,
    RoleCreated = 9,
    RoleUpdated = 10,
    RoleDeleted = 11,
    RoleAssigned = 12,
    BulletinUpdated = 13,
    OwnerChanged = 14,
}

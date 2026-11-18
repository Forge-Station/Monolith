namespace Content.Server.Database;

public sealed class CompanyJobRecord
{
    public string Id = string.Empty;
    public string PosterId = string.Empty;
    public string? TakerId;
    public string Description = string.Empty;
    public int RewardKind;
    public int RewardAmount;
    public string RewardProto = string.Empty;
}

using Robust.Shared.Serialization;

namespace Content.Shared.Research.Components;

[Serializable, NetSerializable]
public enum ResearchServerLinkUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ResearchServerLinkEntry
{
    public NetEntity Client;
    public string Name = string.Empty;
    public bool Authorized;
    public bool Connected;

    public ResearchServerLinkEntry(NetEntity client, string name, bool authorized, bool connected)
    {
        Client = client;
        Name = name;
        Authorized = authorized;
        Connected = connected;
    }
}

[Serializable, NetSerializable]
public sealed class ResearchServerLinkBoundInterfaceState : BoundUserInterfaceState
{
    public List<ResearchServerLinkEntry> Clients;

    public ResearchServerLinkBoundInterfaceState(List<ResearchServerLinkEntry> clients)
    {
        Clients = clients;
    }
}

[Serializable, NetSerializable]
public sealed class ResearchServerAuthorizeClientMessage : BoundUserInterfaceMessage
{
    public NetEntity Client;

    public ResearchServerAuthorizeClientMessage(NetEntity client)
    {
        Client = client;
    }
}

[Serializable, NetSerializable]
public sealed class ResearchServerRevokeClientMessage : BoundUserInterfaceMessage
{
    public NetEntity Client;

    public ResearchServerRevokeClientMessage(NetEntity client)
    {
        Client = client;
    }
}

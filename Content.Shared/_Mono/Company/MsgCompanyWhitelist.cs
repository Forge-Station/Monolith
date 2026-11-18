using System.Collections.Generic;
using Content.Shared._Forge.Company;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Mono.Company;

public sealed class MsgCompanyWhitelist : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.EntityEvent;

    public HashSet<ProtoId<CompanyPrototype>> Whitelist = new();
    public HashSet<ProtoId<CompanyPrototype>> RecruitingOpen = new();
    /// <summary>Public diplomacy snapshot for the lobby company catalog.</summary>
    public List<(string CompanyA, string CompanyB, CompanyRelationKind Kind)> Relations = new();

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        var count = buffer.ReadVariableInt32();
        Whitelist = new HashSet<ProtoId<CompanyPrototype>>(count);
        for (var i = 0; i < count; i++)
            Whitelist.Add(buffer.ReadString());

        var openCount = buffer.ReadVariableInt32();
        RecruitingOpen = new HashSet<ProtoId<CompanyPrototype>>(openCount);
        for (var i = 0; i < openCount; i++)
            RecruitingOpen.Add(buffer.ReadString());

        var relationCount = buffer.ReadVariableInt32();
        Relations = new List<(string, string, CompanyRelationKind)>(relationCount);
        for (var i = 0; i < relationCount; i++)
        {
            Relations.Add((buffer.ReadString(), buffer.ReadString(), (CompanyRelationKind)buffer.ReadByte()));
        }
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(Whitelist.Count);
        foreach (var company in Whitelist)
            buffer.Write(company);

        buffer.WriteVariableInt32(RecruitingOpen.Count);
        foreach (var company in RecruitingOpen)
            buffer.Write(company);

        buffer.WriteVariableInt32(Relations.Count);
        foreach (var (a, b, kind) in Relations)
        {
            buffer.Write(a);
            buffer.Write(b);
            buffer.Write((byte)kind);
        }
    }
}

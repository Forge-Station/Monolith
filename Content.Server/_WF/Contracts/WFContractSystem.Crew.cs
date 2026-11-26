using System.Linq;
using Content.Server._WF.RoundSetup;
using Content.Shared.Hands.EntitySystems;
using Content.Server.Stack;
using Content.Shared._WF.Contracts;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Contracts;

/// <summary>Crew Setup crews share contract slots and pay; a player outside a crew is a crew of one.</summary>
public sealed partial class WFContractSystem
{
    [Dependency] private WFRoundSetupSystem _crews = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    /// <summary>Most crewmates past a contract's minimum that add to its pay.</summary>
    public const int MaxBonusCrew = 4;

    /// <summary>What a contract pays a crew of a size in all, with its crew bonus.</summary>
    public static int CrewPool(int reward, WFContractPrototype proto, int crewSize)
    {
        var extra = Math.Clamp(crewSize - Math.Max(1, proto.MinCrew), 0, MaxBonusCrew);
        return reward + (int) (reward * proto.CrewBonus * extra);
    }

    /// <summary>Slots for a crew: one, plus one each time it doubles.</summary>
    public static int SlotsFor(int crewSize) => 1 + (int) Math.Floor(Math.Log2(Math.Max(1, crewSize)));

    /// <summary>Crew members needed for one more slot than <paramref name="crewSize"/> gives.</summary>
    public static int NextSlotAt(int crewSize) => 1 << SlotsFor(crewSize);

    /// <summary>Members of a player's crew in the round as its members; 1 outside a crew.</summary>
    public int CrewSize(NetUserId user) => Math.Max(1, _crews.CrewInRound(user).Count);

    /// <summary>A player's crew's size in the round, its slots and how many of them are taken.</summary>
    public (int Size, int Slots, int Held) CrewSlots(NetUserId user)
    {
        var size = CrewSize(user);
        return (size, SlotsFor(size), ActiveForCrew(user).Count());
    }

    /// <summary>Live contracts held by anyone in a player's crew, them included.</summary>
    public IEnumerable<WFContract> ActiveForCrew(NetUserId user)
    {
        var crew = _crews.CrewMembers(user).ToHashSet();
        return _contracts.Values.Where(c => c.Status == WFContractStatus.Active && c.User is { } holder && crew.Contains(holder));
    }

    /// <summary>Who shares a finished contract's pay: the holder, and every crewmate playing in the round.</summary>
    private List<WFContractHolder> PaidCrew(WFContractHolder holder)
    {
        var crew = new List<WFContractHolder> { holder };
        foreach (var (user, body) in _crews.CrewInRound(holder.User))
        {
            if (user != holder.User && HolderOf(body) is { } mate && !crew.Contains(mate))
                crew.Add(mate);
        }

        return crew;
    }

    /// <summary>Hands a stack of faction credits to a mob, or drops it at their feet.</summary>
    private void GiveCredits(EntityUid mob, EntProtoId credits, int amount)
    {
        foreach (var stack in _stacks.SpawnMultiple(credits.Id, amount, Transform(mob).Coordinates))
        {
            _hands.PickupOrDrop(mob, stack);
        }
    }
}

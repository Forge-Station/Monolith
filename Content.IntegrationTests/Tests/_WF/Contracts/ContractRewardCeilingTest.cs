#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Content.Server._WF.Contracts;
using Content.Shared._WF.Contracts;

namespace Content.IntegrationTests.Tests._WF.Contracts;

/// <summary>
/// Keeps contract pay inside the economy: every template's best possible pay (top base, most packages, the longest
/// paid run, goods at their cap), shared by its minimum crew, stays under its tier's ceiling for one player.
/// </summary>
[TestFixture]
[TestOf(typeof(WFContractSystem))]
public sealed class ContractRewardCeilingTest
{
    /// <summary>Most a contract of each tier may ever pay one player, deposit aside.</summary>
    private static readonly Dictionary<int, int> Ceilings = new() { [1] = 37500, [2] = 75000, [3] = 125000, [4] = 175000, [5] = 250000 };

    [Test]
    public async Task NoContractPaysPastItsTier()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<WFContractSystem>();
            var table = new StringBuilder();
            var over = new List<string>();
            foreach (var c in server.ProtoMan.EnumeratePrototypes<WFContractPrototype>().OrderBy(c => c.Contractor.Id).ThenBy(c => c.Tier))
            {
                var (min, max) = Range(sys, c);
                // The holder's pay, with the split's remainder, at every crew size that adds a bonus.
                var minCrew = System.Math.Max(1, c.MinCrew);
                var crew = minCrew;
                var each = 0;
                for (var n = minCrew; n <= minCrew + WFContractSystem.MaxBonusCrew; n++)
                {
                    var pool = WFContractSystem.CrewPool(max, c, n);
                    var holderPay = pool / n + pool % n;
                    if (holderPay <= each)
                        continue;

                    each = holderPay;
                    crew = n;
                }
                table.AppendLine($"{c.Contractor.Id,-30} {c.ID,-36} {c.Category,-9} T{c.Tier} {min,7}-{max,7} crew {crew} each {each,7} +{c.CrewBonus:P0} dep {c.Deposit,6} {c.Minutes,3}m cr {c.Credits}");
                if (each > Ceilings[c.Tier])
                    over.Add($"{c.ID}: up to {each} each for a crew of {crew}, tier {c.Tier} ceiling {Ceilings[c.Tier]}");
            }

            TestContext.Out.WriteLine(table.ToString());
            Assert.That(over, Is.Empty, string.Join("\n", over));
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The least and most a template can be posted for.</summary>
    private static (int Min, int Max) Range(WFContractSystem sys, WFContractPrototype c)
    {
        int min = c.Reward.X, max = c.Reward.Y;
        switch (c.Objective)
        {
            case WFContractCourier k:
                min += k.PerPackage * k.Count.X + WFContractSystem.DistancePay(k.PerKilometre, 1000f);
                max += k.PerPackage * k.Count.Y + WFContractSystem.DistancePay(k.PerKilometre, 30000f);
                break;
            case WFContractSupply s:
            {
                var values = s.Goods.Select(g => (Low: sys.UnitValue(g) * g.Count.X, High: sys.UnitValue(g) * g.Count.Y)).ToList();
                var lines = s.Pick > 0 ? s.Pick : values.Count;
                var low = values.Select(v => v.Low).OrderBy(v => v).Take(lines).Sum() * s.Premium;
                var high = values.Select(v => v.High).OrderByDescending(v => v).Take(lines).Sum() * s.Premium;
                var cap = s.GoodsCap ?? c.Reward.Y;
                min += (int) System.Math.Min(low, cap) + WFContractSystem.DistancePay(s.PerKilometre, 1000f);
                max += (int) System.Math.Min(high, cap) + WFContractSystem.DistancePay(s.PerKilometre, 30000f);
                break;
            }
            case WFContractMailRun m:
                min += m.PerStop * m.Stops.X + WFContractSystem.DistancePay(m.PerKilometre, 1000f);
                max += m.PerStop * m.Stops.Y + WFContractSystem.DistancePay(m.PerKilometre, 30000f);
                break;
            case WFContractSweep w:
                min += w.PerPoint * w.Points.X;
                max += w.PerPoint * w.Points.Y;
                break;
            case WFContractRecovery { PerUnlockedNode: > 0 } r:
                // A fully analysed artifact: allow for a large node graph.
                max += r.PerUnlockedNode * 12;
                break;
            case WFContractSurvey v:
                min += v.PerAnomaly * v.Count.X;
                max += v.PerAnomaly * v.Count.Y;
                break;
            case WFContractEscort e:
                min += WFContractSystem.DistancePay(e.PerKilometre, 1000f);
                max += WFContractSystem.DistancePay(e.PerKilometre, 30000f);
                break;
        }

        return (min, max);
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.Audio;
using Content.Server.Power.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._WF.Drydock;
using Content.Shared.Popups;
using Content.Shared.Radio.Components;
using Robust.Server.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.Server._WF.Drydock;

public sealed partial class WFDrydockSystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AmbientSoundSystem _ambient = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private RadioSystem _radio = default!;

    /// <summary>Time between checks that a worked ship still sits in its berth.</summary>
    private static readonly TimeSpan BerthCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>Pause after the cranes reach the ship before work starts.</summary>
    private const float DeploySettle = 0.75f;

    /// <summary>Shortest work time an item is paced down to, so its crane still visibly works it.</summary>
    private const float MinWorkTime = 0.5f;

    /// <summary>Time between re-aims of the cranes, so a drifting ship keeps its items under the heads.</summary>
    private static readonly TimeSpan AimInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>Jobs that ended this tick, ended after the loop so the query is not disturbed.</summary>
    private readonly List<(EntityUid Console, WFDrydockConsoleComponent Comp, WFDrydockJobComponent Job, JobEnd End)> _ends = new();

    /// <summary>How a running job leaves its console.</summary>
    private enum JobEnd
    {
        None,
        Finished,
        Aborted,
        Unpowered,
    }

    /// <summary>Crane motion targets: arm, carriage, tool head and boom lengths in tiles, boom angle in degrees.</summary>
    private readonly record struct CraneTarget(float Arm, float Position, float Trolley, float Extension, float Angle);

    /// <summary>Advances every running job one frame, then ends those that finished, left the berth or ran out of power.</summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        UpdateBookings(curTime);
        UpdateBerthLights(curTime);
        UpdateBlockers(curTime);
        RefreshOpenConsoles(curTime);

        _ends.Clear();
        var query = EntityQueryEnumerator<WFDrydockJobComponent, WFDrydockConsoleComponent>();
        while (query.MoveNext(out var uid, out var job, out var comp))
        {
            var end = TickJob(uid, comp, job, curTime, frameTime);
            if (end != JobEnd.None)
                _ends.Add((uid, comp, job, end));
        }

        foreach (var (console, comp, job, end) in _ends)
            EndJob(console, comp, job, end);
    }

    /// <summary>Advances one job by a frame and returns how it ends, or None while it keeps running.</summary>
    private JobEnd TickJob(EntityUid console, WFDrydockConsoleComponent comp, WFDrydockJobComponent job, TimeSpan curTime, float frameTime)
    {
        if (TerminatingOrDeleted(job.Ship))
            return job.HullGone ? JobEnd.Finished : JobEnd.Aborted;

        if (curTime >= job.NextBerthCheck)
        {
            job.NextBerthCheck = curTime + BerthCheckInterval;
            if (!StillBerthed(console, comp, job))
                return JobEnd.Aborted;
        }

        PlayMachinery(comp, job, curTime);

        UpdatePower(comp, job, frameTime);
        if (job.Unpowered >= comp.PowerAbortTime)
            return JobEnd.Unpowered;

        // Cranes still swing out over the ship; their travel slows with their power.
        if (curTime < job.WorkStarts)
        {
            job.WorkStarts += TimeSpan.FromSeconds((1 - job.Power) * frameTime);
            UpdateEstimate(job, curTime);
            return JobEnd.None;
        }

        if (!job.Started)
        {
            job.Started = true;
            UpdateUi(console, comp);
        }

        if (curTime >= job.NextAim)
        {
            job.NextAim = curTime + AimInterval;
            AimSlots(comp, job);
        }

        PlayAmbientSparks(comp, job, curTime);

        var changed = false;
        foreach (var slot in job.Slots)
        {
            changed |= TickSlot(console, comp, job, slot, curTime);
            // A break that lifted the last tile took the hull with it.
            if (TerminatingOrDeleted(job.Ship))
                return job.HullGone ? JobEnd.Finished : JobEnd.Aborted;
        }

        // No crane took a tile, so those left would split the hull if lifted; they stay.
        if (job.Break && job.Pending.Count > 0 && job.Slots.All(s => s.Item == null))
        {
            LeaveStanding(job);
            changed = true;
        }

        if (changed)
        {
            UpdateEstimate(job, curTime);
            UpdateUi(console, comp);
        }

        return job.Pending.Count == 0 && job.Slots.All(s => s.Item == null) ? JobEnd.Finished : JobEnd.None;
    }

    /// <summary>Plays a machinery sound on a random ship tile when its interval is up.</summary>
    private void PlayMachinery(WFDrydockConsoleComponent comp, WFDrydockJobComponent job, TimeSpan curTime)
    {
        if (curTime < job.NextIndustry || job.SparkTiles.Count == 0)
            return;

        _audio.PlayPvs(comp.IndustrySound, new EntityCoordinates(job.Ship, RandomTile(job)));
        job.NextIndustry = Jittered(curTime, comp.IndustrySoundInterval);
    }

    /// <summary>Spawns an ambient spark on a random ship tile when its interval is up.</summary>
    private void PlayAmbientSparks(WFDrydockConsoleComponent comp, WFDrydockJobComponent job, TimeSpan curTime)
    {
        if (curTime < job.NextSpark || job.SparkTiles.Count == 0)
            return;

        Spawn(comp.SparkEffect, new EntityCoordinates(job.Ship, RandomTile(job) + RandomOffset(0.4f)));
        job.NextSpark = Jittered(curTime, comp.AmbientSparkInterval);
    }

    /// <summary>
    /// Advances one crane slot: applies a finished item, gives up an unreachable one, and takes and works the next.
    /// Returns whether the job's queue changed.
    /// </summary>
    private bool TickSlot(EntityUid console, WFDrydockConsoleComponent comp, WFDrydockJobComponent job, WFDrydockJobSlot slot, TimeSpan curTime)
    {
        var changed = false;
        if (slot.Item is { } finished && curTime >= slot.Ends)
        {
            if (finished.Kind == WFDrydockWorkKind.Break)
                ApplyBreak(console, job, slot, finished);
            else
                ApplyItem(job.Ship, finished);
            job.Done++;
            slot.Item = null;
            changed = true;
        }

        // An item out of reach for too long is skipped, along with every item no crane can reach.
        if (slot.Unreachable && curTime >= slot.GiveUpAt)
        {
            GiveUpReach(console, comp, job, slot);
            changed = true;
        }

        if (slot.Item == null && job.Pending.Count > 0)
            StartSlot(comp, job, slot, curTime);

        if (slot.Item is { } item && curTime >= slot.WorkFrom && !slot.Unreachable && !slot.Stalled)
            WorkItem(comp, job.Ship, slot, item, curTime);

        return changed;
    }

    /// <summary>Plays the arrival sound and the work sparks and tool sounds for an item its crane has reached.</summary>
    private void WorkItem(WFDrydockConsoleComponent comp, EntityUid ship, WFDrydockJobSlot slot, WFDrydockWorkItem item, TimeSpan curTime)
    {
        var coords = new EntityCoordinates(ship, item.LocalPos);
        if (!slot.Arrived)
        {
            slot.Arrived = true;
            _audio.PlayPvs(comp.CraneArriveSound, coords);
        }

        if (curTime >= slot.NextSpark)
        {
            Spawn(comp.SparkEffect, new EntityCoordinates(ship, item.LocalPos + RandomOffset(0.3f)));
            slot.NextSpark = Jittered(curTime, comp.WorkSparkInterval);
        }

        if (curTime >= slot.NextSound)
        {
            _audio.PlayPvs(WorkSound(comp, item.Kind), coords);
            slot.NextSound = Jittered(curTime, comp.WorkSoundInterval);
        }
    }

    /// <summary>
    /// Refreshes each crane's power ratio, delays its slots by the power it lacks, and tracks how long it has been stalled.
    /// </summary>
    private void UpdatePower(WFDrydockConsoleComponent comp, WFDrydockJobComponent job, float frameTime)
    {
        var lowest = 1f;
        var anyCrane = false;
        var allStalled = true;
        foreach (var slot in job.Slots)
        {
            var ratio = 1f;
            var blocked = false;
            if (slot.Crane is { } craneUid)
            {
                anyCrane = true;
                ratio = CranePower(craneUid);
                if (TryComp<WFDrydockCraneComponent>(craneUid, out var crane))
                {
                    SyncPower(craneUid, crane, ratio);
                    blocked = crane.Blocked;
                }
            }

            // A crane whose arm lane is blocked stalls like an unpowered one.
            slot.Stalled = ratio < WFDrydockCraneComponent.StallRatio || blocked;
            lowest = Math.Min(lowest, ratio);
            allStalled &= slot.Stalled;

            var lag = TimeSpan.FromSeconds((1 - ratio) * frameTime);
            if (lag > TimeSpan.Zero)
                DelaySlot(slot, lag);
        }

        job.Power = lowest;
        job.Unpowered = anyCrane && allStalled ? job.Unpowered + frameTime : 0f;
        job.Deadline += TimeSpan.FromSeconds((1 - lowest) * frameTime);
    }

    /// <summary>Pushes a slot's timers back by the time its crane lost to low power.</summary>
    private static void DelaySlot(WFDrydockJobSlot slot, TimeSpan lag)
    {
        slot.WorkFrom += lag;
        if (slot.Ends != TimeSpan.MaxValue)
            slot.Ends += lag;
        slot.GiveUpAt += lag;
        slot.NextSpark += lag;
        slot.NextSound += lag;
    }

    /// <summary>Share of working power a crane receives, 0 to 1; below the stall ratio it is 0. No power consumer is 1.</summary>
    private float CranePower(EntityUid uid)
    {
        if (!TryComp<PowerConsumerComponent>(uid, out var consumer) || consumer.DrawRate <= 0f)
            return 1f;

        var ratio = Math.Clamp(consumer.ReceivedPower / consumer.DrawRate, 0f, 1f);
        return ratio < WFDrydockCraneComponent.StallRatio ? 0f : ratio;
    }

    /// <summary>Publishes a crane's power ratio when it moved by 0.05 or crossed the stall line.</summary>
    private void SyncPower(EntityUid uid, WFDrydockCraneComponent crane, float ratio)
    {
        var crossed = (crane.PowerRatio < WFDrydockCraneComponent.StallRatio) != (ratio < WFDrydockCraneComponent.StallRatio);
        if (!crossed && MathF.Abs(ratio - crane.PowerRatio) < 0.05f)
            return;

        crane.PowerRatio = ratio;
        Dirty(uid, crane);
    }

    /// <summary>Re-points the crane of every slot with a reachable item at where that item is now.</summary>
    private void AimSlots(WFDrydockConsoleComponent comp, WFDrydockJobComponent job)
    {
        foreach (var slot in job.Slots)
        {
            if (slot.Item is { } item && !slot.Unreachable)
                PointCrane(comp, job.Ship, slot, item.LocalPos, correction: true);
        }
    }

    /// <summary>Tool sounds for the kind of work being done.</summary>
    private static SoundSpecifier WorkSound(WFDrydockConsoleComponent comp, WFDrydockWorkKind kind)
    {
        return kind switch
        {
            WFDrydockWorkKind.Tile or WFDrydockWorkKind.Break => comp.HullWorkSound,
            WFDrydockWorkKind.Entity => comp.PartWorkSound,
            WFDrydockWorkKind.Heal => comp.DamageWorkSound,
            _ => comp.RefitWorkSound,
        };
    }

    /// <summary>Takes the nearest pending item into an idle slot and sends the slot's crane to it.</summary>
    private void StartSlot(WFDrydockConsoleComponent comp, WFDrydockJobComponent job, WFDrydockJobSlot slot, TimeSpan curTime)
    {
        var item = TakeNearest(comp, job, slot);
        if (item == null)
            return;

        var reachable = CanReach(comp, job.Ship, slot.Crane, item.LocalPos);
        var travel = PointCrane(comp, job.Ship, slot, item.LocalPos);

        slot.Item = item;
        slot.ToolPos = item.LocalPos;
        slot.Arrived = false;
        slot.Unreachable = !reachable;
        slot.GiveUpAt = curTime + TimeSpan.FromSeconds(comp.ReachTimeout);
        slot.WorkFrom = curTime + TimeSpan.FromSeconds(travel);
        // An unreachable item is never finished; the slot gives it up at GiveUpAt instead.
        slot.Ends = reachable ? slot.WorkFrom + TimeSpan.FromSeconds(PacedWorkTime(job, item, travel, curTime, SlotSpeed(slot))) : TimeSpan.MaxValue;
        slot.NextSpark = slot.WorkFrom;
        slot.NextSound = slot.WorkFrom;
    }

    /// <summary>Skips the item a slot gave up on and every pending item no crane can reach, refunding them to the payer.</summary>
    private void GiveUpReach(EntityUid console, WFDrydockConsoleComponent comp, WFDrydockJobComponent job, WFDrydockJobSlot slot)
    {
        var returned = new List<WFDrydockWorkItem>();
        if (slot.Item is { } stuck)
            returned.Add(stuck);

        slot.Item = null;
        slot.Unreachable = false;

        for (var i = job.Pending.Count - 1; i >= 0; i--)
        {
            var pending = job.Pending[i];
            if (ReachableByJob(comp, job, pending.LocalPos))
                continue;

            returned.Add(pending);
            job.Pending.RemoveAt(i);
        }

        job.Done += returned.Count;
        job.Skipped += returned.Count;
        if (job.PaidInMaterials)
        {
            var (materials, parts) = ReturnMaterials(console, JobCranes(job), returned);
            AddBill(job.SkippedMaterials, materials);
            AddBill(job.SkippedParts, parts);
        }
        else
        {
            var refund = returned.Sum(i => i.Cost);
            job.SkippedRefund += refund;
            Refund(job, refund);
        }
        _audio.PlayPvs(comp.DenySound, console);
    }

    /// <summary>
    /// Removes and returns the slot's next pending item: the earliest rank first (hull tiles, then parts on them, then guns
    /// on their hardpoints), then the nearest one its crane reaches, else the nearest one no crane reaches. Skips tiles
    /// another crane is working. Null when only items another crane reaches or works on remain.
    /// </summary>
    private WFDrydockWorkItem? TakeNearest(WFDrydockConsoleComponent comp, WFDrydockJobComponent job, WFDrydockJobSlot slot)
    {
        if (job.Break)
            return TakeBreakTile(comp, job, slot);

        var rank = job.Pending.Min(Rank);
        var reach = -1;
        var reachDistance = float.MaxValue;
        var stranded = -1;
        var strandedDistance = float.MaxValue;
        for (var i = 0; i < job.Pending.Count; i++)
        {
            var candidate = job.Pending[i];
            if (Rank(candidate) != rank || TileBusy(job, slot, candidate))
                continue;

            var distance = (candidate.LocalPos - slot.ToolPos).LengthSquared();
            if (CanReach(comp, job.Ship, slot.Crane, candidate.LocalPos))
            {
                if (distance < reachDistance)
                {
                    reach = i;
                    reachDistance = distance;
                }
            }
            else if (distance < strandedDistance && !ReachableByJob(comp, job, candidate.LocalPos))
            {
                stranded = i;
                strandedDistance = distance;
            }
        }

        var best = reach >= 0 ? reach : stranded;
        if (best < 0)
            return null;

        var item = job.Pending[best];
        job.Pending[best] = job.Pending[^1];
        job.Pending.RemoveAt(job.Pending.Count - 1);
        return item;
    }

    /// <summary>
    /// An item's work time at its crane's speed, shortened when crane travel has put its slot behind the job's deadline:
    /// the time left is shared between this item and the slot's share of the queue, each with a trip as long as this one.
    /// </summary>
    private static float PacedWorkTime(WFDrydockJobComponent job, WFDrydockWorkItem item, float travel, TimeSpan curTime, float speed)
    {
        var share = 1f + (float) job.Pending.Count / job.Slots.Count;
        var paced = (float) (job.Deadline - curTime).TotalSeconds / share - travel;
        return Math.Min(item.WorkTime / speed, Math.Max(MinWorkTime, paced));
    }

    /// <summary>Speed multiplier of a slot's crane; 1 for a slot without one.</summary>
    private float SlotSpeed(WFDrydockJobSlot slot)
    {
        return slot.Crane is { } crane && TryComp<WFDrydockCraneComponent>(crane, out var comp)
            ? Math.Max(0.01f, comp.SpeedMultiplier)
            : 1f;
    }

    /// <summary>Order in which work is taken: by kind, with guns that mount on hardpoints after the other parts.</summary>
    private static int Rank(WFDrydockWorkItem item)
    {
        return (int) item.Kind * 2 + (item.Mounted ? 1 : 0);
    }

    /// <summary>
    /// Whether another crane works the item's tile. One crane per tile keeps the tile's work in rank order: a part never
    /// lands before its floor, nor a gun before its hardpoint.
    /// </summary>
    private static bool TileBusy(WFDrydockJobComponent job, WFDrydockJobSlot slot, WFDrydockWorkItem item)
    {
        var tile = item.LocalPos.Floored();
        foreach (var other in job.Slots)
        {
            if (other != slot && other.Item is { } working && working.LocalPos.Floored() == tile)
                return true;
        }

        return false;
    }

    /// <summary>Whether any crane of the job reaches a ship-local point.</summary>
    private bool ReachableByJob(WFDrydockConsoleComponent comp, WFDrydockJobComponent job, Vector2 shipLocal)
    {
        foreach (var slot in job.Slots)
        {
            if (CanReach(comp, job.Ship, slot.Crane, shipLocal))
                return true;
        }

        return false;
    }

    /// <summary>Recomputes the estimated end from the deploy, the queue and the items in progress.</summary>
    private void UpdateEstimate(WFDrydockJobComponent job, TimeSpan curTime)
    {
        var remaining = 0d;
        var speed = Math.Max(WFDrydockCraneComponent.StallRatio, job.Power) * job.Slots.Average(SlotSpeed);
        foreach (var item in job.Pending)
        {
            remaining += item.WorkTime / speed;
        }

        foreach (var slot in job.Slots)
        {
            if (slot.Item != null)
            {
                var end = slot.Unreachable ? slot.GiveUpAt : slot.Ends;
                remaining += Math.Max(0d, (end - curTime).TotalSeconds);
            }
        }

        var start = job.WorkStarts > curTime ? job.WorkStarts : curTime;
        job.EstimatedEnd = start + TimeSpan.FromSeconds(remaining / Math.Max(1, job.Slots.Count));
    }

    /// <summary>Extends each crane's arm down the bay, swings its boom over the ship and spreads the cranes along its length. Returns the seconds until all are in place.</summary>
    private float DeployCranes(WFDrydockConsoleComponent comp, WFDrydockJobComponent job)
    {
        if (!TryComp<MapGridComponent>(job.Ship, out var grid))
            return 0f;

        var bounds = grid.LocalAABB;
        var shipToWorld = _transform.GetWorldMatrix(job.Ship);
        var worldToShip = _transform.GetInvWorldMatrix(job.Ship);
        var corners = new[] { bounds.BottomLeft, bounds.BottomRight, bounds.TopLeft, bounds.TopRight }
            .Select(c => Vector2.Transform(c, shipToWorld))
            .ToArray();

        var cranes = job.Slots.Where(s => s.Crane != null).ToList();
        var longest = 0f;
        for (var k = 0; k < cranes.Count; k++)
        {
            var slot = cranes[k];
            var craneUid = slot.Crane!.Value;
            if (!TryComp<WFDrydockCraneComponent>(craneUid, out var crane))
                continue;

            // The ship's footprint in the arm frame: across the bay, along the arm.
            var toCrane = _transform.GetInvWorldMatrix(craneUid);
            var footprint = corners.Select(c => crane.LocalToArm(Vector2.Transform(c, toCrane))).ToArray();
            var minAcross = footprint.Min(p => p.X);
            var maxAcross = footprint.Max(p => p.X);
            var minAlong = footprint.Min(p => p.Y);
            var maxAlong = footprint.Max(p => p.Y);

            var arm = Math.Clamp(maxAlong + 0.5f, crane.BerthGap + 1f, crane.MaxArm);
            var position = Math.Clamp(minAlong + (k + 0.5f) / cranes.Count * (maxAlong - minAlong), crane.BerthGap, arm);
            var extension = ClampBoom(craneUid, crane, position, Math.Clamp(maxAcross + 0.5f, crane.SectionLength, crane.MaxExtension));
            var trolley = Math.Clamp((Math.Max(minAcross, 0f) + Math.Min(maxAcross, extension)) / 2f, 1f, extension);

            longest = Math.Max(longest, MoveCrane(craneUid, crane, comp, new CraneTarget(arm, position, trolley, extension, 0f), active: true));

            var tool = Vector2.Transform(crane.ArmToLocal(trolley, position), _transform.GetWorldMatrix(craneUid));
            slot.ToolPos = Vector2.Transform(tool, worldToShip);
        }

        return cranes.Count == 0 ? 0f : longest + DeploySettle;
    }

    /// <summary>Whether a crane's envelope, with the reach tolerance, covers a ship-local point. A missing crane reaches everything.</summary>
    public bool CanReach(WFDrydockConsoleComponent comp, EntityUid ship, EntityUid? crane, Vector2 shipLocal)
    {
        if (crane is not { } craneUid || !TryGetCrane(craneUid, out var craneComp))
            return true;

        var arm = ArmPoint(ship, craneUid, craneComp, shipLocal);
        var tol = comp.ReachTolerance;
        // Points in the gap before the berth, or past a solid blocker on their arm row, lie beyond the boom's path.
        return arm.Y >= craneComp.BerthGap - tol && arm.Y <= craneComp.MaxArm + tol
            && arm.X >= -tol && arm.X <= craneComp.MaxExtension + tol
            && arm.X < BoomBlockAt(craneUid, craneComp, arm.Y) - 0.5f;
    }

    /// <summary>Moves the crane of a slot so its tool head sits over the target. Returns the travel time in seconds.</summary>
    /// <remarks>A correction is a re-aim at an item that drifted, so its move is quiet unless it is slow.</remarks>
    private float PointCrane(WFDrydockConsoleComponent comp, EntityUid ship, WFDrydockJobSlot slot, Vector2 shipLocal, bool correction = false)
    {
        if (slot.Crane is not { } craneUid || !TryGetCrane(craneUid, out var crane))
            return 0f;

        var target = ArmPoint(ship, craneUid, crane, shipLocal);
        var along = target.Y;
        var arm = along > crane.Arm ? Math.Clamp(along + 0.5f, crane.BerthGap + 1f, crane.MaxArm) : crane.Arm;
        // The carriage works from the berth's start, unless the arm is still shorter than the gap.
        var position = Math.Clamp(along, Math.Min(crane.BerthGap, arm), arm);
        var extension = ClampBoom(craneUid, crane, position, ClampExtension(crane, Math.Max(crane.Extension, target.X)));

        return MoveCrane(craneUid, crane, comp,
            new CraneTarget(arm, position, Math.Clamp(target.X, 1f, extension), extension, crane.Angle),
            active: true,
            correction);
    }

    /// <summary>A ship-local point in a crane's arm frame.</summary>
    private Vector2 ArmPoint(EntityUid ship, EntityUid craneUid, WFDrydockCraneComponent crane, Vector2 shipLocal)
    {
        var worldPos = _transform.ToMapCoordinates(new EntityCoordinates(ship, shipLocal)).Position;
        return crane.LocalToArm(Vector2.Transform(worldPos, _transform.GetInvWorldMatrix(craneUid)));
    }

    /// <summary>
    /// Sets a crane's targets and returns how long the client animation takes. The arm moves first, then the head, paced by its slowest axis.
    /// </summary>
    private float MoveCrane(EntityUid uid, WFDrydockCraneComponent crane, WFDrydockConsoleComponent comp, CraneTarget target, bool active, bool correction = false)
    {
        var armTime = Math.Abs(target.Arm - crane.Arm) / crane.ArmSpeed;
        var headTime = Math.Max(
            Math.Max(Math.Abs(target.Position - crane.Position) / crane.Speed, Math.Abs(target.Trolley - crane.TrolleyPosition) / crane.TrolleySpeed),
            Math.Max(Math.Abs(target.Extension - ClampExtension(crane, crane.Extension)) / crane.ExtendSpeed, Math.Abs(target.Angle - crane.Angle) / crane.SlewSpeed));
        var time = (armTime + headTime) / Math.Max(0.01f, crane.SpeedMultiplier);

        // Hydraulics at the arm root whenever the crane gets going; aim corrections only when they are slow.
        if (time > (correction ? 0.5f : 0.25f))
            _audio.PlayPvs(comp.CraneMoveSound, new EntityCoordinates(uid, crane.ArmToLocal(0f, 0f)));

        crane.Arm = target.Arm;
        crane.Position = target.Position;
        crane.TrolleyPosition = target.Trolley;
        crane.Extension = target.Extension;
        crane.Angle = target.Angle;

        if (crane.Active != active)
        {
            crane.Active = active;
            _ambient.SetAmbience(uid, active);
        }

        if (TryComp<PowerConsumerComponent>(uid, out var consumer))
            consumer.DrawRate = active ? crane.WorkingLoad : crane.IdleLoad;

        Dirty(uid, crane);
        return time;
    }

    /// <summary>Folds every crane of the console's berth to its parked pose and stops it working.</summary>
    private void ParkCranes(EntityUid console, WFDrydockConsoleComponent comp)
    {
        foreach (var craneUid in LinkedCranes(console))
        {
            if (!TryComp<WFDrydockCraneComponent>(craneUid, out var crane))
                continue;

            _boomBlock.Remove(craneUid);
            MoveCrane(craneUid, crane, comp, new CraneTarget(0f, 0f, 0f, crane.SectionLength, crane.ParkAngle), active: false);
        }
    }

    /// <summary>Sends a crane to every client for its whole life, so its arm and boom render from afar.</summary>
    private void OnCraneMapInit(Entity<WFDrydockCraneComponent> ent, ref MapInitEvent args)
    {
        _pvs.AddGlobalOverride(ent.Owner);
    }

    /// <summary>Stops tracking a crane that is removed, and lifts its global PVS override.</summary>
    private void OnCraneShutdown(Entity<WFDrydockCraneComponent> ent, ref ComponentShutdown args)
    {
        _boomBlock.Remove(ent.Owner);
        _pvs.RemoveGlobalOverride(ent.Owner);
    }

    /// <summary>Whether an entity still exists and has a crane component.</summary>
    private bool TryGetCrane(EntityUid uid, [NotNullWhen(true)] out WFDrydockCraneComponent? crane)
    {
        crane = null;
        return !TerminatingOrDeleted(uid) && TryComp<WFDrydockCraneComponent>(uid, out crane);
    }

    private static float ClampExtension(WFDrydockCraneComponent crane, float extension)
    {
        return Math.Clamp(extension, crane.SectionLength, crane.MaxExtension);
    }

    /// <summary>A random ship-local tile center from the job's spark tiles.</summary>
    private Vector2 RandomTile(WFDrydockJobComponent job)
    {
        return job.SparkTiles[_random.Next(job.SparkTiles.Count)];
    }

    /// <summary>The time an interval after <paramref name="from"/>, jittered by a random factor from 0.6 to 1.4.</summary>
    private TimeSpan Jittered(TimeSpan from, float interval)
    {
        return from + TimeSpan.FromSeconds(interval * (0.6f + 0.8f * _random.NextFloat()));
    }

    /// <summary>Random ship-local offset within a square of the given half-width.</summary>
    private Vector2 RandomOffset(float range)
    {
        return new Vector2(
            (_random.NextFloat() * 2f - 1f) * range,
            (_random.NextFloat() * 2f - 1f) * range);
    }

    /// <summary>Banks a refund to the job's payer, if the payer still exists.</summary>
    private void Refund(WFDrydockJobComponent job, int amount)
    {
        if (amount > 0 && job.Payer is { } payer && !TerminatingOrDeleted(payer))
            _bank.TryBankDeposit(payer, amount, false);
    }

    /// <summary>
    /// Ends a job. A finished one notifies and confirms; an aborted or unpowered one refunds its unfinished items and denies.
    /// Then the ship's refits are recorded, the cranes park and the UI refreshes.
    /// </summary>
    private void EndJob(EntityUid console, WFDrydockConsoleComponent comp, WFDrydockJobComponent job, JobEnd end)
    {
        if (job.Break)
            EndBreak(console, comp, job, end);
        else if (end == JobEnd.Finished)
        {
            Notify(console, comp, job, job.Skipped == 0
                ? Loc.GetString("drydock-console-finished", ("ship", Name(job.Ship)))
                : job.PaidInMaterials
                    ? Loc.GetString("drydock-console-finished-skipped-materials", ("ship", Name(job.Ship)), ("count", job.Skipped), ("refund", FormatBill(job.SkippedMaterials, job.SkippedParts)))
                    : Loc.GetString("drydock-console-finished-skipped", ("ship", Name(job.Ship)), ("count", job.Skipped), ("refund", job.SkippedRefund)));
            _audio.PlayPvs(comp.ConfirmSound, console);
        }
        else
        {
            var unfinished = UnfinishedItems(job);
            var powerLost = end == JobEnd.Unpowered;
            if (job.PaidInMaterials)
            {
                var (materials, parts) = ReturnMaterials(console, JobCranes(job), unfinished);
                Notify(console, comp, job, Loc.GetString(powerLost ? "drydock-console-aborted-power-materials" : "drydock-console-aborted-materials",
                    ("refund", FormatBill(materials, parts))));
            }
            else
            {
                var refund = unfinished.Sum(i => i.Cost);
                Refund(job, refund);
                Notify(console, comp, job, Loc.GetString(powerLost ? "drydock-console-aborted-power" : "drydock-console-aborted", ("refund", refund)));
            }
            _audio.PlayPvs(comp.DenySound, console);
        }

        if (!job.Break)
            RecordRefits(job, end);

        RemComp<WFDrydockJobComponent>(console);
        ParkCranes(console, comp);
        UpdateUi(console, comp);
    }

    /// <summary>Items not applied yet: the queue and the items in progress.</summary>
    private static List<WFDrydockWorkItem> UnfinishedItems(WFDrydockJobComponent job)
    {
        return job.Pending.Concat(job.Slots.Where(s => s.Item != null).Select(s => s.Item!)).ToList();
    }

    /// <summary>Pops a job message at the console, on a payer who ordered it from their own ship, and on the console's radio channel.</summary>
    private void Notify(EntityUid console, WFDrydockConsoleComponent comp, WFDrydockJobComponent job, string message)
    {
        _popup.PopupEntity(message, console, PopupType.Medium);

        if (job.Payer is { } payer && !TerminatingOrDeleted(payer) && Transform(payer).GridUid != Transform(console).GridUid)
            _popup.PopupEntity(message, payer, payer, PopupType.Medium);

        // Telecom exempt like an intercom, so a crew that went elsewhere still hears it.
        if (comp.NoticeChannel is { } channel)
        {
            EnsureComp<TelecomExemptComponent>(console);
            _radio.SendRadioMessage(console, message, channel, console);
        }
    }
}

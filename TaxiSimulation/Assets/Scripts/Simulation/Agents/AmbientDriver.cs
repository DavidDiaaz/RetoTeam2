using System;
using System.Collections.Generic;

public class AmbientDriver : VehicleAgent
{
    Profile profile;

    float desiredSpeed;
    float acceleration       = 5f;
    float laneChangeCooldown = 0f;

    static readonly List<VehicleAgent> _empty = new();

    public AmbientDriver()
    {
        profile = Profile.RandomProfile();
    }

    // ---------------------------------------------------------------
    protected override float ComputePriority()
    {
        float roadWeight  = RoadClassInfo.RightOfWayWeight(CurrentLane.Edge.RoadClass) * 0.5f;
        float proximity   = 1f - Math.Min(1f, DistanceToEnd / 20f);
        float urgency     = Math.Min(1f, WaitTime / 10f);
        float kindPenalty = profile.Kindness * 0.3f;
        float lawBonus    = profile.LawAbidance *
                            TrafficLaw.RightOfWayScore(this, TargetNode?.Contenders ?? _empty) * 0.5f;

        return roadWeight + proximity + urgency - kindPenalty + lawBonus;
    }

    // ---------------------------------------------------------------
    public override void Perceive(World world)
    {
        UpdatePerception(world);
    }

    /// <summary>
    /// Only pick edges reachable from the current lane.
    /// An edge is reachable if EntryLaneRequired == -1 (any lane)
    /// or EntryLaneRequired == current LaneNumber.
    /// Falls back to any edge if none match — handles disconnected test roads.
    /// </summary>
    protected override TrafficEdge SelectNextEdge(TrafficNode node)
    {
        if (node.Outgoing.Count == 0) return null;

        // Collect edges reachable from current lane
        var reachable = new List<TrafficEdge>();
        foreach (var edge in node.Outgoing)
        {
            if (edge.EntryLaneRequired < 0 || edge.EntryLaneRequired == LaneNumber)
                reachable.Add(edge);
        }

        // Pick randomly from reachable — if none match fall back to all outgoing
        var candidates = reachable.Count > 0 ? reachable : node.Outgoing;
        return candidates[rng.Next(candidates.Count)];
    }

    // ---------------------------------------------------------------
    public override void Deliberate(World world)
    {
        laneChangeCooldown -= world.DeltaTime;
        ConsiderLaneChange(world);
        ChooseSpeed(world);
    }

    void ConsiderLaneChange(World world)
    {
        if (laneChangeCooldown > 0f) return;
        if (CurrentLane.Edge.Lanes.Count < 2) return;

        // Entry lane requirement — must be in specific lane before node
        if (NeedsEntryLaneChange)
        {
            float urgency   = 1f - Math.Min(1f, DistanceToEnd / (CurrentLane.Edge.Length * 0.5f));
            float threshold = 1f - profile.LaneChangeAggression * 0.5f;
            if (urgency < threshold) return;

            float margin = profile.MinFollowingDistance * (1f - urgency * 0.5f);
            if (TryChangeLane(RequiredLane, margin))
                laneChangeCooldown = 1f;

            return;
        }

        // Must merge — lane disappears on next edge
        if (MustMerge)
        {
            float urgency   = 1f - Math.Min(1f, DistanceToEnd / (CurrentLane.Edge.Length * 0.5f));
            float threshold = 1f - profile.Kindness * 0.7f;
            if (urgency < threshold) return;

            float margin = profile.MinFollowingDistance * (1f - urgency * 0.7f);
            if (TryChangeLane(LaneNumber - 1, margin))
                laneChangeCooldown = 1f;

            return;
        }

        // Opportunistic — blocked ahead
        if (GapAhead > profile.MinFollowingDistance * 2f) return;

        float roll = (float)rng.NextDouble();
        if (roll > profile.LaneChangeAggression) return;

        int[] candidates = { LaneNumber - 1, LaneNumber + 1 };
        foreach (int target in candidates)
        {
            float margin = profile.MinFollowingDistance;

            var behind = target < LaneNumber ? BehindOnLeft : BehindOnRight;
            if (behind != null)
            {
                float closing = Math.Max(0f, behind.Speed - Speed);
                margin += closing * 1.5f;
            }

            if (TryChangeLane(target, margin))
            {
                laneChangeCooldown = 3f * (1f - profile.LaneChangeAggression) + 1f;
                break;
            }
        }
    }

    void ChooseSpeed(World world)
    {
        float speedLimit = TrafficLaw.SpeedLimitMs(this);
        desiredSpeed     = speedLimit * profile.DesiredSpeedFactor;

        if (profile.LawAbidance > 0.8f)
            desiredSpeed = Math.Min(desiredSpeed, speedLimit);

        if (GapAhead < profile.MinFollowingDistance && AheadOnLane != null)
        {
            float followFactor = Math.Max(0f, GapAhead / profile.MinFollowingDistance);
            desiredSpeed = Math.Min(desiredSpeed, AheadOnLane.Speed * followFactor);
        }

        if (IsYieldingToMerger())
            desiredSpeed *= 1f - (profile.Kindness * 0.25f);

        if (MustMerge)
        {
            float urgency = 1f - Math.Min(1f, DistanceToEnd / (CurrentLane.Edge.Length * 0.5f));
            if (urgency > 0.8f)
                desiredSpeed *= 1f - (urgency - 0.8f) * 2f;
        }

        if (NeedsEntryLaneChange)
        {
            float urgency = 1f - Math.Min(1f, DistanceToEnd / (CurrentLane.Edge.Length * 0.5f));
            if (urgency > 0.6f)
                desiredSpeed *= 1f - (urgency - 0.6f) * 1.5f;
        }

        if (profile.LawAbidance > 0.3f &&
            TrafficLaw.MustYieldToRoundabout(this) &&
            TrafficLaw.IsRoundaboutOccupied(this))
        {
            desiredSpeed = Math.Min(desiredSpeed, ComputeBrakingSpeed(DistanceToEnd));
        }

        desiredSpeed = ApplyBrakingConstraints(desiredSpeed);
        Speed        = MoveTowards(Speed, desiredSpeed, acceleration * world.DeltaTime);
    }

    bool IsYieldingToMerger()
    {
        if (profile.Kindness < 0.3f) return false;

        var leftLane  = CurrentLane.Edge.GetLeftLane(LaneNumber);
        var rightLane = CurrentLane.Edge.GetRightLane(LaneNumber);

        return CheckLaneForMerger(leftLane) || CheckLaneForMerger(rightLane);
    }

    bool CheckLaneForMerger(Lane lane)
    {
        if (lane == null) return false;
        var nearby = lane.GetVehicleAheadAt(Position - Length * 2f);
        if (nearby == null) return false;
        float dist = Math.Abs(nearby.Position - Position);
        return dist < Length * 3f && nearby.MustMerge;
    }

    public override void Act(World world)
    {
        Move(world);
    }
}
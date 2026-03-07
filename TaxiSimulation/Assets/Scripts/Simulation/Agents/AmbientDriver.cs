using System;

public class AmbientDriver : VehicleAgent
{
    Profile profile;

    float desiredSpeed;
    float acceleration       = 5f;
    float laneChangeCooldown = 0f;

    public AmbientDriver()
    {
        profile = Profile.RandomProfile();
    }

    // ---------------------------------------------------------------
    protected override float ComputePriority()
    {
        float roadWeight  = CurrentLane.Edge.RoadClass == RoadClass.Primary ? 1f : 0f;
        float proximity   = 1f - Math.Min(1f, DistanceToEnd / 20f);
        float urgency     = Math.Min(1f, WaitTime / 10f);
        float kindPenalty = profile.Kindness * 0.3f;
        return roadWeight + proximity + urgency - kindPenalty;
    }

    // ---------------------------------------------------------------
    public override void Perceive(World world)
    {
        UpdatePerception(world);
    }

    protected override TrafficEdge SelectNextEdge(TrafficNode node)
    {
        return node.Outgoing[rng.Next(node.Outgoing.Count)];
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

        // Must merge — lane won't exist on next edge
        if (MustMerge)
        {
            float urgency = 1f - Math.Min(1f, DistanceToEnd /
                            (CurrentLane.Edge.Length * 0.5f));
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
        desiredSpeed = CurrentLane.Edge.SpeedLimit * (1000f / 3600f) * profile.DesiredSpeedFactor;

        // Follow vehicle ahead
        if (GapAhead < profile.MinFollowingDistance && AheadOnLane != null)
        {
            float followFactor = Math.Max(0f, GapAhead / profile.MinFollowingDistance);
            desiredSpeed = Math.Min(desiredSpeed, AheadOnLane.Speed * followFactor);
        }

        // Yield to adjacent merger
        if (IsYieldingToMerger())
            desiredSpeed *= 1f - (profile.Kindness * 0.25f);

        // Brake when must merge and running out of road
        if (MustMerge)
        {
            float urgency = 1f - Math.Min(1f, DistanceToEnd / (CurrentLane.Edge.Length * 0.5f));
            if (urgency > 0.8f)
                desiredSpeed *= 1f - (urgency - 0.8f) * 2f;
        }

        desiredSpeed = ApplyBrakingConstraints(desiredSpeed);

        Speed = MoveTowards(Speed, desiredSpeed, acceleration * world.DeltaTime);
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

}
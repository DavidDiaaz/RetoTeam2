using System;
using System.Collections.Generic;
using System.Threading;

public abstract class VehicleAgent : Agent
{
    public readonly int Id = Interlocked.Increment(ref _nextId);
    static int _nextId = 0;

    protected static Random rng = new Random();

    // ---------------------------------------------------------------
    // Physical state
    // ---------------------------------------------------------------
    public Lane  CurrentLane;
    public int   LaneIndex;
    public int   LaneNumber;
    public float Position;
    public float Speed;
    public float Length = 4.5f;

    // ---------------------------------------------------------------
    // Intersection negotiation
    // ---------------------------------------------------------------
    public TrafficNode  TargetNode       { get; protected set; }
    public TrafficEdge  TargetEdge       { get; protected set; }
    public int          TargetLaneNumber { get; protected set; }
    public float        Priority         { get; protected set; }
    public bool         IsSignalling     { get; protected set; }
    public bool         IsYielding       { get; protected set; }
    public float        WaitTime         { get; protected set; }
    public bool         MustMerge        { get; protected set; }

    // ---------------------------------------------------------------
    // Perception results
    // ---------------------------------------------------------------
    protected VehicleAgent AheadOnLane;
    protected VehicleAgent BehindOnLane;
    protected VehicleAgent AheadOnLeft;
    protected VehicleAgent BehindOnLeft;
    protected VehicleAgent AheadOnRight;
    protected VehicleAgent BehindOnRight;
    protected float        GapAhead;
    protected float        DistanceToEnd;
    protected bool         RedLightAhead;

    // Debug
    float _logTimer = 0f;
    const float LOG_INTERVAL   = 3f;
    const float STUCK_THRESHOLD = 5f;

    // ---------------------------------------------------------------
    protected void UpdatePerception(World world)
    {
        AheadOnLane  = CurrentLane.GetVehicleAhead(this);
        BehindOnLane = CurrentLane.GetVehicleBehind(this);

        GapAhead = AheadOnLane != null
            ? Math.Max(0f, AheadOnLane.Position - Position - AheadOnLane.Length - Length)
            : float.MaxValue;

        var leftLane  = CurrentLane.Edge.GetLeftLane(LaneNumber);
        var rightLane = CurrentLane.Edge.GetRightLane(LaneNumber);

        AheadOnLeft   = leftLane?.GetVehicleAheadAt(Position);
        BehindOnLeft  = leftLane?.GetVehicleBehindAt(Position);
        AheadOnRight  = rightLane?.GetVehicleAheadAt(Position);
        BehindOnRight = rightLane?.GetVehicleBehindAt(Position);

        DistanceToEnd = CurrentLane.Edge.Length - Position;
        TargetNode    = CurrentLane.Edge.to;

        RedLightAhead = TargetNode.Light != null
            && TargetNode.Light.CurrentState == TrafficLight.State.Red
            && DistanceToEnd < 20f;

        if (TargetEdge == null && TargetNode.Outgoing.Count > 0)
        {
            TargetEdge       = SelectNextEdge(TargetNode);
            TargetLaneNumber = TargetEdge != null
                ? Math.Min(LaneNumber, TargetEdge.Lanes.Count - 1)
                : 0;
        }

        MustMerge = TargetEdge != null && LaneNumber >= TargetEdge.Lanes.Count;

        if (DistanceToEnd <= 1f && TargetNode != null)
            TargetNode.RegisterContender(this);

        Priority = ComputePriority();
    }

    // ---------------------------------------------------------------
    protected virtual float ComputePriority()
    {
        float roadWeight = CurrentLane.Edge.RoadClass == RoadClass.Primary ? 1f : 0f;
        float proximity  = 1f - Math.Min(1f, DistanceToEnd / 20f);
        float urgency    = Math.Min(1f, WaitTime / 10f);
        return roadWeight + proximity + urgency;
    }

    protected static float MoveTowards(float current, float target, float maxDelta)
    {
        if (Math.Abs(target - current) <= maxDelta) return target;
        return current + Math.Sign(target - current) * maxDelta;
    }

    protected float ComputeBrakingSpeed(float distance, float deceleration = 4f)
    {
        return (float)Math.Sqrt(2f * deceleration * Math.Max(0f, distance));
    }

    protected float ApplyBrakingConstraints(float desired)
    {
        if (RedLightAhead)
            desired = Math.Min(desired, ComputeBrakingSpeed(DistanceToEnd));

        if (TargetNode != null && (TargetNode.IsBlocked || IsYielding) && DistanceToEnd < 15f)
            desired = Math.Min(desired, ComputeBrakingSpeed(DistanceToEnd));

        return desired;
    }

    // ---------------------------------------------------------------
    public override void Act(World world)
    {
        Move(world);
    }

    public void Move(World world)
    {
        Position += Speed * world.DeltaTime;

        MaintainOrder();

        if (CurrentLane.Edge.from.OccupiedBy == this && Position > Length)
        {
            CurrentLane.Edge.from.OccupiedBy = null;
            IsSignalling = false;
        }

        if (Position >= CurrentLane.Edge.Length)
            TryMoveToNextEdge(world.DeltaTime);

        _logTimer += world.DeltaTime;
        if (_logTimer >= LOG_INTERVAL)
        {
            _logTimer = 0f;
            if (WaitTime >= STUCK_THRESHOLD)
                LogStuck();
        }
    }

    void TryMoveToNextEdge(float dt)
    {
        if (TargetNode == null || TargetNode.Outgoing.Count == 0)
        {
            Speed    = 0;
            Position = CurrentLane.Edge.Length;
            UnityEngine.Debug.LogWarning(
                $"[V{Id}] Dead end — node {TargetNode?.id} has no outgoing edges.");
            return;
        }

        // Hard block
        if (TargetNode.IsBlocked)
        {
            Speed      = 0;
            Position   = CurrentLane.Edge.Length - 0.05f;
            WaitTime  += dt;
            IsYielding = true;

            if (WaitTime >= STUCK_THRESHOLD)
                UnityEngine.Debug.Log(
                    $"[V{Id}] BLOCKED by OccupiedBy=V{TargetNode.OccupiedBy?.Id} " +
                    $"at node {TargetNode.id} | waited {WaitTime:F1}s | " +
                    $"occupant edge {TargetNode.OccupiedBy?.CurrentLane?.Edge?.from?.id}→" +
                    $"{TargetNode.OccupiedBy?.CurrentLane?.Edge?.to?.id} " +
                    $"occupant pos={TargetNode.OccupiedBy?.Position:F2} " +
                    $"occupant length={TargetNode.OccupiedBy?.Length:F2}");
            return;
        }

        // Red light
        if (RedLightAhead)
        {
            Speed    = 0;
            Position = CurrentLane.Edge.Length - 0.05f;
            return;
        }

        if (TargetEdge == null)
        {
            Speed = 0;
            UnityEngine.Debug.LogWarning(
                $"[V{Id}] TargetEdge null at node {TargetNode?.id} " +
                $"outgoing count={TargetNode?.Outgoing?.Count}");
            return;
        }

        Lane nextLane = TargetEdge.Lanes[TargetLaneNumber];

        // Physical space
        if (!nextLane.IsSegmentFree(0f, Length))
        {
            Speed     = 0;
            Position  = CurrentLane.Edge.Length - 0.05f;
            WaitTime += dt;

            if (WaitTime >= STUCK_THRESHOLD)
            {
                var blocker = nextLane.GetVehicleAheadAt(0f);
                UnityEngine.Debug.Log(
                    $"[V{Id}] TARGET LANE FULL — waited {WaitTime:F1}s | " +
                    $"target {TargetEdge.from.id}→{TargetEdge.to.id} lane {TargetLaneNumber} | " +
                    $"blocker=V{blocker?.Id} pos={blocker?.Position:F2}");
            }
            return;
        }

        // Contender priority
        foreach (var other in TargetNode.Contenders)
        {
            if (other == this) continue;

            bool otherHasPriority = other.Priority > Priority ||
                                   (other.Priority == Priority && other.Id < Id);

            if (otherHasPriority)
            {
                IsYielding = true;
                Speed      = 0;
                Position   = CurrentLane.Edge.Length - 0.05f;
                WaitTime  += dt;

                if (WaitTime >= STUCK_THRESHOLD)
                    UnityEngine.Debug.Log(
                        $"[V{Id}] YIELDING to V{other.Id} at node {TargetNode.id} | " +
                        $"waited {WaitTime:F1}s | " +
                        $"my priority={Priority:F3} their priority={other.Priority:F3} | " +
                        $"contenders={TargetNode.Contenders.Count} | " +
                        $"other edge={other.CurrentLane?.Edge?.from?.id}→{other.CurrentLane?.Edge?.to?.id} | " +
                        $"other targetEdge={other.TargetEdge?.from?.id}→{other.TargetEdge?.to?.id}");
                return;
            }
        }

        // Transition
        TargetNode.OccupiedBy = this;
        IsSignalling          = true;
        IsYielding            = false;
        WaitTime              = 0f;

        CurrentLane.Remove(this);
        CurrentLane      = nextLane;
        Position         = 0f;
        LaneNumber       = TargetLaneNumber;
        TargetEdge       = null;
        TargetLaneNumber = 0;
        nextLane.InsertSorted(this);
    }

    void LogStuck()
    {
        UnityEngine.Debug.Log(
            $"[V{Id}] STUCK {WaitTime:F1}s | " +
            $"edge={CurrentLane?.Edge?.from?.id}→{CurrentLane?.Edge?.to?.id} | " +
            $"pos={Position:F1}/{CurrentLane?.Edge?.Length:F1} | " +
            $"distToEnd={DistanceToEnd:F2} | " +
            $"node={TargetNode?.id} blocked={TargetNode?.IsBlocked} | " +
            $"occupiedBy=V{TargetNode?.OccupiedBy?.Id} | " +
            $"redLight={RedLightAhead} | " +
            $"targetEdge={TargetEdge?.from?.id}→{TargetEdge?.to?.id} | " +
            $"contenders={TargetNode?.Contenders?.Count} | " +
            $"yielding={IsYielding} | priority={Priority:F3}");
    }

    void MaintainOrder()
    {
        int i = LaneIndex;
        while (i < CurrentLane.Vehicles.Count - 1 &&
               Position > CurrentLane.Vehicles[i + 1].Position)
        {
            var other = CurrentLane.Vehicles[i + 1];
            CurrentLane.Vehicles[i + 1] = this;
            CurrentLane.Vehicles[i]     = other;
            LaneIndex++;
            other.LaneIndex--;
            i++;
        }
    }

    // ---------------------------------------------------------------
    protected abstract TrafficEdge SelectNextEdge(TrafficNode node);

    protected bool TryChangeLane(int targetLaneNumber, float safetyMargin)
    {
        var edge = CurrentLane.Edge;
        if (targetLaneNumber < 0 || targetLaneNumber >= edge.Lanes.Count) return false;

        Lane target = edge.Lanes[targetLaneNumber];
        if (!target.IsSegmentFree(Position - safetyMargin, Length + safetyMargin * 2f))
            return false;

        CurrentLane.Remove(this);
        LaneNumber  = targetLaneNumber;
        CurrentLane = target;
        target.InsertSorted(this);
        return true;
    }
}
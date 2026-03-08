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
    public TrafficNode  TargetNode           { get; protected set; }
    public TrafficEdge  TargetEdge           { get; protected set; }
    public int          TargetLaneNumber     { get; protected set; }
    public float        Priority             { get; protected set; }
    public bool         IsSignalling         { get; protected set; }
    public bool         IsYielding           { get; protected set; }
    public float        WaitTime             { get; protected set; }
    public bool         MustMerge            { get; protected set; }
    public bool         NeedsEntryLaneChange { get; protected set; }
    public int          RequiredLane         { get; protected set; }

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

    protected float DistanceToStopLine => Math.Max(0f, DistanceToEnd - Length);
    public    float DistanceToEdgeEnd  => DistanceToEnd;

    protected bool RedLightAhead;

    float _logTimer = 0f;
    const float LOG_INTERVAL    = 3f;
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
            TargetEdge = SelectNextEdge(TargetNode);

            if (TargetEdge != null)
            {
                if (TargetEdge.IsConnection)
                {
                    TargetLaneNumber = 0;
                }
                else
                {
                    TargetLaneNumber = TargetEdge.EntryLaneRequired >= 0
                        ? TargetEdge.EntryLaneRequired
                        : Math.Min(LaneNumber, TargetEdge.Lanes.Count - 1);
                }

                // Log every edge selection so we can trace the problem
                UnityEngine.Debug.Log(
                    $"[V{Id}] SELECTED edge {TargetEdge.from.id}→{TargetEdge.to.id} " +
                    $"isConn={TargetEdge.IsConnection} " +
                    $"entryReq={TargetEdge.EntryLaneRequired} " +
                    $"targetLane={TargetLaneNumber} " +
                    $"currentLane={LaneNumber} " +
                    $"edgeLanes={TargetEdge.Lanes.Count}");
            }
            else
            {
                TargetLaneNumber = 0;
            }
        }

        MustMerge = TargetEdge != null
            && !CurrentLane.Edge.IsConnection
            && LaneNumber >= TargetEdge.Lanes.Count;

        NeedsEntryLaneChange = false;
        RequiredLane         = -1;

        if (TargetEdge != null &&
            TargetEdge.EntryLaneRequired >= 0 &&
            !CurrentLane.Edge.IsConnection)
        {
            int required = Math.Min(TargetEdge.EntryLaneRequired, CurrentLane.Edge.Lanes.Count - 1);
            if (LaneNumber != required)
            {
                NeedsEntryLaneChange = true;
                RequiredLane         = required;

                UnityEngine.Debug.Log(
                    $"[V{Id}] NEEDS ENTRY CHANGE " +
                    $"currentLane={LaneNumber} required={required} " +
                    $"edge={TargetEdge.from.id}→{TargetEdge.to.id} " +
                    $"isConn={TargetEdge.IsConnection}");
            }
        }

        if (DistanceToEnd <= 1f)
            TargetNode.RegisterContender(this);

        Priority = ComputePriority();
    }

    // ---------------------------------------------------------------
    protected virtual float ComputePriority()
    {
        float roadWeight = RoadClassInfo.RightOfWayWeight(CurrentLane.Edge.RoadClass) * 0.5f;
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
        => (float)Math.Sqrt(2f * deceleration * Math.Max(0f, distance));

    protected float ApplyBrakingConstraints(float desired)
    {
        if (RedLightAhead)
            desired = Math.Min(desired, ComputeBrakingSpeed(DistanceToStopLine));

        if (TargetNode != null && (TargetNode.IsBlocked || IsYielding) && DistanceToEnd < 15f)
            desired = Math.Min(desired, ComputeBrakingSpeed(DistanceToStopLine));

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
            return;
        }

        if (TargetNode.IsBlocked)
        {
            Speed      = 0;
            Position   = CurrentLane.Edge.Length - 0.05f;
            WaitTime  += dt;
            IsYielding = true;
            if (WaitTime >= STUCK_THRESHOLD)
                UnityEngine.Debug.Log(
                    $"[V{Id}] BLOCKED by V{TargetNode.OccupiedBy?.Id} at node {TargetNode.id} " +
                    $"| waited {WaitTime:F1}s");
            return;
        }

        if (RedLightAhead)
        {
            Speed    = 0;
            Position = CurrentLane.Edge.Length - 0.05f;
            return;
        }

        if (TargetEdge == null) { Speed = 0; return; }

        if (NeedsEntryLaneChange)
        {
            Speed     = 0;
            Position  = CurrentLane.Edge.Length - 0.05f;
            WaitTime += dt;
            return;
        }

        int  clampedLane = Math.Min(TargetLaneNumber, TargetEdge.Lanes.Count - 1);
        Lane nextLane    = TargetEdge.Lanes[clampedLane];

        if (!nextLane.IsSegmentFree(0f, Length))
        {
            Speed     = 0;
            Position  = CurrentLane.Edge.Length - 0.05f;
            WaitTime += dt;
            if (WaitTime >= STUCK_THRESHOLD)
            {
                var blocker = nextLane.GetVehicleAheadAt(0f);
                UnityEngine.Debug.Log(
                    $"[V{Id}] TARGET LANE FULL waited {WaitTime:F1}s | blocker=V{blocker?.Id}");
            }
            return;
        }

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
                        $"[V{Id}] YIELDING to V{other.Id} at node {TargetNode.id} " +
                        $"| waited {WaitTime:F1}s | my={Priority:F3} their={other.Priority:F3}");
                return;
            }
        }

        // Log the transition
        UnityEngine.Debug.Log(
            $"[V{Id}] TRANSITION " +
            $"from edge {CurrentLane.Edge.from.id}→{CurrentLane.Edge.to.id} lane={LaneNumber} " +
            $"to edge {TargetEdge.from.id}→{TargetEdge.to.id} lane={clampedLane} " +
            $"isConn={TargetEdge.IsConnection} entryReq={TargetEdge.EntryLaneRequired}");

        TargetNode.OccupiedBy = this;
        IsSignalling          = true;
        IsYielding            = false;
        WaitTime              = 0f;

        CurrentLane.Remove(this);
        CurrentLane          = nextLane;
        Position             = 0f;
        LaneNumber           = clampedLane;
        TargetEdge           = null;
        TargetLaneNumber     = 0;
        NeedsEntryLaneChange = false;
        RequiredLane         = -1;
        nextLane.InsertSorted(this);
    }

    void LogStuck()
    {
        UnityEngine.Debug.Log(
            $"[V{Id}] STUCK {WaitTime:F1}s | " +
            $"edge={CurrentLane?.Edge?.from?.id}→{CurrentLane?.Edge?.to?.id} | " +
            $"pos={Position:F1}/{CurrentLane?.Edge?.Length:F1} | " +
            $"isConnection={CurrentLane?.Edge?.IsConnection} | " +
            $"node={TargetNode?.id} blocked={TargetNode?.IsBlocked} | " +
            $"redLight={RedLightAhead} | " +
            $"needsEntry={NeedsEntryLaneChange} reqLane={RequiredLane} | " +
            $"contenders={TargetNode?.Contenders?.Count} | priority={Priority:F3}");
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

    protected abstract TrafficEdge SelectNextEdge(TrafficNode node);

    protected bool TryChangeLane(int targetLaneNumber, float safetyMargin)
    {
        var edge = CurrentLane.Edge;
        if (targetLaneNumber < 0 || targetLaneNumber >= edge.Lanes.Count) return false;

        Lane target = edge.Lanes[targetLaneNumber];
        if (!target.IsSegmentFree(Position - safetyMargin, Length + safetyMargin * 2f))
            return false;

        UnityEngine.Debug.Log(
            $"[V{Id}] LANE CHANGE {LaneNumber}→{targetLaneNumber} " +
            $"edge={CurrentLane.Edge.from.id}→{CurrentLane.Edge.to.id} " +
            $"isConn={CurrentLane.Edge.IsConnection}");

        CurrentLane.Remove(this);
        LaneNumber  = targetLaneNumber;
        CurrentLane = target;
        target.InsertSorted(this);
        return true;
    }
}
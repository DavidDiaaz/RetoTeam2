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
    public int   LaneIndex  = -1;
    public int   LaneNumber;
    public float Position;
    public float Speed;
    public float Length = 1.29f;   // 4.5m / 3.5 m·per·unit ≈ 1.29 units

    // ---------------------------------------------------------------
    // Lane change state
    // ---------------------------------------------------------------
    public Lane  LaneChangeDest     { get; private set; }
    public Lane  LaneChangeOrigin   { get; private set; }
    public float LaneChangeProgress { get; private set; }
    public bool  IsChangingLane     => LaneChangeDest != null;

    public float EffectiveLengthOn(Lane lane)
    {
        if (!IsChangingLane)          return lane == CurrentLane ? Length : 0f;
        if (lane == LaneChangeDest)   return Length * LaneChangeProgress;
        if (lane == LaneChangeOrigin) return Length * (1f - LaneChangeProgress);
        return 0f;
    }

    // ---------------------------------------------------------------
    // Routing
    // ---------------------------------------------------------------
    public TrafficEdge TargetEdge   { get; protected set; }
    public LaneLink    TargetLink   { get; protected set; }
    public bool        IsSignalling { get; protected set; }
    public int         DesiredLane  { get; protected set; } = -1;

    // ---------------------------------------------------------------
    // Perception
    // ---------------------------------------------------------------
    protected VehicleAgent AheadOnLane;
    protected VehicleAgent BehindOnLane;
    protected VehicleAgent AheadOnLeft;
    protected VehicleAgent BehindOnLeft;
    protected VehicleAgent AheadOnRight;
    protected VehicleAgent BehindOnRight;
    protected float        GapAhead;
    protected float        DistanceToEnd;
    protected float        Priority;
    protected bool         RedLightAhead;

    protected float DistanceToStopLine => DistanceToEnd - Length;
    public    float DistanceToEdgeEnd  => DistanceToEnd;
    public    TrafficNode TargetNode   => CurrentLane?.Edge?.to;
    protected bool OnConnector         => CurrentLane?.Edge?.IsConnector ?? false;

    float _waitTime = 0f;
    float _logTimer = 0f;
    const float STUCK_THRESHOLD    = 5f;    // seconds — unchanged

    protected bool IsStuck => _waitTime >= STUCK_THRESHOLD;
    const float LOG_INTERVAL       = 3f;
    const float LANE_CHANGE_METERS = 4.3f;  // 15m / 3.5

    // ---------------------------------------------------------------
    protected void UpdatePerception(World world)
    {
        AheadOnLane  = CurrentLane.GetVehicleAhead(this);
        BehindOnLane = CurrentLane.GetVehicleBehind(this);

        GapAhead = AheadOnLane != null
            ? Math.Max(0f, AheadOnLane.Position - Position - AheadOnLane.Length)
            : float.MaxValue;

        if (!OnConnector)
        {
            var leftLane  = CurrentLane.Edge.GetLeftLane(LaneNumber);
            var rightLane = CurrentLane.Edge.GetRightLane(LaneNumber);

            AheadOnLeft   = leftLane?.GetVehicleAheadAt(Position);
            BehindOnLeft  = leftLane?.GetVehicleBehindAt(Position);
            AheadOnRight  = rightLane?.GetVehicleAheadAt(Position);
            BehindOnRight = rightLane?.GetVehicleBehindAt(Position);
        }
        else
        {
            AheadOnLeft = BehindOnLeft = AheadOnRight = BehindOnRight = null;
        }

        DistanceToEnd = CurrentLane.Edge.Length - Position;

        // Red light lookahead: 20m / 3.5 ≈ 5.7 units
        RedLightAhead = !OnConnector
            && TargetNode?.Light != null
            && TargetNode.Light.CurrentState == TrafficLight.State.Red
            && DistanceToEnd < 5.7f;

        if (TargetLink == null)
        {
            TargetLink = SelectNextLink(world.Navigation);
            TargetEdge = TargetLink?.DestLane?.Edge;

            if (TargetLink != null && TargetLink.SourceLane != CurrentLane)
                DesiredLane = TargetLink.SourceLane.LaneNumber;
            else
                DesiredLane = -1;
        }

        Priority = ComputePriority();
    }

    // ---------------------------------------------------------------
    protected virtual float ComputePriority()
    {
        float roadWeight = RoadClassInfo.RightOfWayWeight(CurrentLane.Edge.RoadClass) * 0.5f;
        float proximity  = 1f - Math.Min(1f, DistanceToEnd / 5.7f);  // scaled from 20m
        float urgency    = Math.Min(1f, _waitTime / 10f);
        return roadWeight + proximity + urgency;
    }

    protected abstract LaneLink SelectNextLink(NavigationGraph graph);

    protected static float MoveTowards(float current, float target, float maxDelta)
    {
        if (Math.Abs(target - current) <= maxDelta) return target;
        return current + Math.Sign(target - current) * maxDelta;
    }

    // Default decel: 4 m/s² / 3.5 ≈ 1.14 units/s²
    protected float ComputeBrakingSpeed(float distance, float deceleration = 1.14f)
        => (float)Math.Sqrt(2f * deceleration * Math.Max(0f, distance));

    protected float ApplyBrakingConstraints(float desired)
    {
        if (OnConnector) return desired;

        if (RedLightAhead)
            desired = Math.Min(desired, ComputeBrakingSpeed(DistanceToStopLine));

        return desired;
    }

    // ---------------------------------------------------------------
    // Junction entry guard
    // ---------------------------------------------------------------
    protected float JunctionEntryCap(NavigationGraph nav)
    {
        if (OnConnector)        return float.MaxValue;
        if (TargetLink == null) return float.MaxValue;
        if (!TargetLink.ToConnector) return float.MaxValue;

        float brakeTo = ComputeBrakingSpeed(Math.Max(0f, DistanceToStopLine));

        Lane  connLane   = TargetLink.DestLane;
        float connLength = connLane.Edge.Length;

        // 1. Entry zone occupied?
        foreach (var v in connLane.Vehicles)
            if (v.Position < connLength * 0.6f)
                return brakeTo;

        // 2. Dest space check
        var exitLinks = nav.GetLinksFrom(connLane);
        if (exitLinks.Count == 0) return float.MaxValue;

        LaneLink exitLink = exitLinks[0];
        Lane     destLane = exitLink.DestLane;
        float    mergePos = exitLink.MergePosition * destLane.Edge.Length;

        if (!destLane.IsSegmentFree(mergePos, Length))
            return brakeTo;

        // 3. Sibling connector conflict
        foreach (var sibling in destLane.IncomingConnectors)
        {
            if (sibling == connLane) continue;
            if (sibling.Vehicles.Count == 0) continue;

            var siblingLinks = nav.GetLinksFrom(sibling);
            if (siblingLinks.Count == 0) continue;
            float siblingMerge = siblingLinks[0].MergePosition * destLane.Edge.Length;

            if (Math.Abs(siblingMerge - mergePos) >= Length * 1.5f) continue;

            var siblingFront   = sibling.Vehicles[sibling.Vehicles.Count - 1];
            bool siblingMoving = siblingFront.Speed > 0.14f;  // 0.5 m/s / 3.5
            if (siblingMoving || siblingFront.Id < Id)
                return brakeTo;
        }

        // 4. Side-entry yield
        if (!exitLink.IsStraight)
        {
            var behind = destLane.GetVehicleBehindAt(mergePos);
            if (behind != null)
            {
                float gap    = mergePos - behind.Position;
                // (v² / 2b) + Length + 0.57  (2m / 3.5 ≈ 0.57)
                float needed = (behind.Speed * behind.Speed) / (2f * 1.14f)
                               + Length + 0.57f;
                if (gap < needed) return brakeTo;
            }

            var ahead = destLane.GetVehicleAheadAt(mergePos + Length);
            if (ahead != null)
            {
                float gap = ahead.Position - ahead.Length - mergePos;
                if (gap < 0.29f) return brakeTo;  // 1m / 3.5
            }
        }

        return float.MaxValue;
    }

    // ---------------------------------------------------------------
    // Lane change
    // ---------------------------------------------------------------
    protected bool BeginLaneChange(int targetLaneNumber)
    {
        if (IsChangingLane) return false;
        if (OnConnector)    return false;
        var edge = CurrentLane.Edge;
        if (targetLaneNumber < 0 || targetLaneNumber >= edge.Lanes.Count) return false;

        Lane dest = edge.Lanes[targetLaneNumber];
        if (!dest.IsSafeToEnter(Position, Length)) return false;

        LaneChangeOrigin   = CurrentLane;
        LaneChangeDest     = dest;
        LaneChangeProgress = 0f;
        dest.BeginEntering(this, 0f);
        return true;
    }

    protected void CancelLaneChange()
    {
        if (!IsChangingLane) return;
        LaneChangeDest.CancelEntering(this);
        LaneChangeDest     = null;
        LaneChangeOrigin   = null;
        LaneChangeProgress = 0f;
    }

    void AdvanceLaneChange(float distanceTraveled)
    {
        if (!IsChangingLane) return;
        LaneChangeProgress += distanceTraveled / LANE_CHANGE_METERS;
        LaneChangeProgress  = Math.Min(1f, LaneChangeProgress);
        LaneChangeDest.UpdateEntering(this, Length * LaneChangeProgress);
        if (LaneChangeProgress >= 1f)
            CompleteLaneChange();
    }

    void CompleteLaneChange()
    {
        var dest = LaneChangeDest;
        LaneChangeOrigin.Remove(this);
        dest.CompleteEntering(this);

        LaneNumber         = dest.LaneNumber;
        CurrentLane        = dest;
        LaneChangeDest     = null;
        LaneChangeOrigin   = null;
        LaneChangeProgress = 0f;

        if (TargetLink != null)
            DesiredLane = TargetLink.SourceLane == CurrentLane ? -1 : TargetLink.SourceLane.LaneNumber;
    }

    // ---------------------------------------------------------------
    public override void Act(World world) => Move(world);

    public void Move(World world)
    {
        float dist = Speed * world.DeltaTime;
        Position  += dist;

        if (IsChangingLane)
            AdvanceLaneChange(dist);

        MaintainOrder();

        if (Position >= CurrentLane.Edge.Length)
            TryMoveToNextEdge(world.DeltaTime);

        _logTimer += world.DeltaTime;
        if (_logTimer >= LOG_INTERVAL)
        {
            _logTimer = 0f;
            if (_waitTime >= STUCK_THRESHOLD) LogStuck();
        }
    }

    void TryMoveToNextEdge(float dt)
    {
        if (TargetLink == null)
        {
            Speed    = 0;
            Position = CurrentLane.Edge.Length;
            return;
        }

        if (RedLightAhead)
        {
            Speed    = 0;
            Position = CurrentLane.Edge.Length - 0.05f;
            return;
        }

        if (IsChangingLane) CancelLaneChange();

        Lane  destLane = TargetLink.DestLane;
        float mergePos = TargetLink.MergePosition * destLane.Edge.Length;

        if (destLane.Edge.IsConnector)
        {
            if (!IsStuck)
            {
                float connLen = destLane.Edge.Length;
                foreach (var v in destLane.Vehicles)
                {
                    if (v.Position < connLen * 0.6f)
                    {
                        Speed      = 0;
                        Position   = CurrentLane.Edge.Length - 0.05f;
                        _waitTime += dt;
                        if (_waitTime >= STUCK_THRESHOLD) LogStuck();
                        return;
                    }
                }
            }
        }
        else
        {
            if (!IsStuck)
            {
                if (!destLane.IsSegmentFree(mergePos, Length))
                {
                    Speed      = 0;
                    Position   = CurrentLane.Edge.Length - 0.05f;
                    _waitTime += dt;
                    if (_waitTime >= STUCK_THRESHOLD) LogStuck();
                    return;
                }

                var aheadInDest = destLane.GetVehicleAheadAt(mergePos + Length);
                if (aheadInDest != null)
                {
                    float gap         = aheadInDest.Position - aheadInDest.Length - mergePos;
                    float brakingDist = (Speed * Speed) / (2f * 1.14f);  // scaled decel
                    if (gap < brakingDist)
                    {
                        Speed      = 0;
                        Position   = CurrentLane.Edge.Length - 0.05f;
                        _waitTime += dt;
                        return;
                    }
                }
            }
        }

        IsSignalling = true;
        _waitTime    = 0f;

        float overshoot = Position - CurrentLane.Edge.Length;

        CurrentLane.Remove(this);
        CurrentLane = destLane;
        Position    = mergePos + Math.Max(0f, overshoot);
        LaneNumber  = destLane.LaneNumber;
        TargetLink  = null;
        TargetEdge  = null;
        DesiredLane = -1;
        destLane.InsertSorted(this);

        if (Position >= CurrentLane.Edge.Length && !CurrentLane.Edge.IsConnector)
            TryMoveToNextEdge(dt);
    }

    void LogStuck()
    {
        UnityEngine.Debug.Log(
            $"[V{Id}] STUCK {_waitTime:F1}s | " +
            $"edge={CurrentLane?.Edge?.from?.id}→{CurrentLane?.Edge?.to?.id} | " +
            $"connector={OnConnector} | " +
            $"pos={Position:F1}/{CurrentLane?.Edge?.Length:F1} | " +
            $"lane={LaneNumber} desiredLane={DesiredLane} | " +
            $"node={TargetNode?.id} | redLight={RedLightAhead} | " +
            $"targetLink={(TargetLink == null ? "none" : $"{TargetLink.SourceLane.LaneNumber}→{TargetLink.DestLane.LaneNumber}")}");
    }

    void MaintainOrder()
    {
        int i = LaneIndex;
        if (i < 0) return;
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
}
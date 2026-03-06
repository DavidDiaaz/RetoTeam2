using System;
using System.Collections.Generic;

public enum TaxiState
{
    Idle,
    EnRoute,
    Carrying
}

public class AutonomousTaxi : VehicleAgent
{
    public TaxiState  State     = TaxiState.Idle;
    public Pedestrian Passenger = null;

    Queue<TrafficEdge> plannedPath = new();

    float desiredSpeed;
    float acceleration = 5f;

    // ---------------------------------------------------------------
    // Neutral priority — no kindness penalty, urgency still builds
    protected override float ComputePriority()
    {
        float roadWeight = CurrentLane.Edge.RoadClass == RoadClass.Primary ? 1f : 0f;
        float proximity  = 1f - Math.Min(1f, DistanceToEnd / 20f);
        float urgency    = Math.Min(1f, WaitTime / 10f);
        return roadWeight + proximity + urgency;
    }

    // ---------------------------------------------------------------
    public override void Perceive(World world)
    {
        UpdatePerception(world);

        // Pickup check
        if (State == TaxiState.EnRoute && Passenger != null)
        {
            var from = CurrentLane.Edge.from;
            var to   = CurrentLane.Edge.to;
            if (from == Passenger.CurrentNode || to == Passenger.CurrentNode)
            {
                Passenger.OnPickedUp();
                State = TaxiState.Carrying;
            }
        }

        // Dropoff check
        if (State == TaxiState.Carrying && Passenger != null)
        {
            var from = CurrentLane.Edge.from;
            var to   = CurrentLane.Edge.to;
            if (from == Passenger.Destination || to == Passenger.Destination)
            {
                Passenger.OnDroppedOff();
                world.FleetManager?.OnTaxiAvailable(this);
                Passenger = null;
                State     = TaxiState.Idle;
                plannedPath.Clear();
            }
        }
    }

    protected override TrafficEdge SelectNextEdge(TrafficNode node)
    {
        if (plannedPath.Count > 0)
        {
            var next = plannedPath.Peek();
            if (next.from == node)
                return plannedPath.Dequeue();
        }

        // Idle — roam randomly
        if (node.Outgoing.Count == 0) return null;
        return node.Outgoing[rng.Next(node.Outgoing.Count)];
    }

    // ---------------------------------------------------------------
    public void AssignPath(Queue<TrafficEdge> path, Pedestrian passenger)
    {
        plannedPath = path;
        Passenger   = passenger;
        State       = TaxiState.EnRoute;
    }

    public bool        IsAvailable => State == TaxiState.Idle;
    public TrafficNode CurrentNode => CurrentLane?.Edge.from;

    // ---------------------------------------------------------------
    public override void Deliberate(World world)
    {
        ChooseSpeed(world);
    }

    void ChooseSpeed(World world)
    {
        desiredSpeed = CurrentLane.Edge.SpeedLimit * (1000f / 3600f);

        if (GapAhead < 8f && AheadOnLane != null)
        {
            float followFactor = Math.Max(0f, GapAhead / 8f);
            desiredSpeed = Math.Min(desiredSpeed, AheadOnLane.Speed * followFactor);
        }

        if (RedLightAhead)
        {
            float brakingSpeed = (float)Math.Sqrt(2f * 4f * Math.Max(0f, DistanceToEnd));
            desiredSpeed = Math.Min(desiredSpeed, brakingSpeed);
        }

        if (TargetNode != null && (TargetNode.IsBlocked || IsYielding) && DistanceToEnd < 15f)
        {
            float brakingSpeed = (float)Math.Sqrt(2f * 4f * Math.Max(0f, DistanceToEnd));
            desiredSpeed = Math.Min(desiredSpeed, brakingSpeed);
        }

        Speed = MoveTowards(Speed, desiredSpeed, acceleration * world.DeltaTime);
    }

    public override void Act(World world)
    {
        Move(world);
    }

    float MoveTowards(float current, float target, float maxDelta)
    {
        if (Math.Abs(target - current) <= maxDelta) return target;
        return current + Math.Sign(target - current) * maxDelta;
    }
}
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
    public override void Perceive(World world)
    {
        UpdatePerception(world);

        // Passenger cancellation check
        if (Passenger != null && Passenger.State == PedestrianState.Cancelled)
        {
            Passenger = null;
            State     = TaxiState.Idle;
            plannedPath.Clear();
        }

        // Pickup check — fires when taxi has just crossed the passenger's node (leg 2 starts here)
        if (State == TaxiState.EnRoute && Passenger != null)
        {
            if (CurrentLane.Edge.from == Passenger.CurrentNode)
            {
                Passenger.OnPickedUp();
                State = TaxiState.Carrying;
            }
        }

        // Dropoff check — fires when taxi is on the edge leading into the destination
        if (State == TaxiState.Carrying && Passenger != null)
        {
            if (CurrentLane.Edge.to == Passenger.Destination)
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

            // Path desynced — taxi is at a node that doesn't match the next planned edge
            UnityEngine.Debug.LogWarning(
                $"[Taxi{Id}] Path desync at node {node.id}: " +
                $"expected edge from {next.from.id}, resetting to Idle.");
            plannedPath.Clear();
            Passenger = null;
            State     = TaxiState.Idle;
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
    public TrafficNode CurrentNode => CurrentLane?.Edge.to;

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

        desiredSpeed = ApplyBrakingConstraints(desiredSpeed);

        Speed = MoveTowards(Speed, desiredSpeed, acceleration * world.DeltaTime);
    }
}
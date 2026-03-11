using System;
using System.Collections.Generic;

/// <summary>
/// Path-following taxi. Follows a pre-computed sequence of LaneLinks
/// supplied by FleetManager. Picks up and drops off passengers.
/// </summary>
public class AutonomousTaxi : VehicleAgent
{
    readonly Queue<LaneLink> path = new();

    TrafficNode pickupNode;
    TrafficNode dropoffNode;

    float desiredSpeed;
    float acceleration = 4f;

    // ---------------------------------------------------------------
    public Pedestrian Passenger    { get; private set; }
    public bool       HasPassenger => Passenger != null;
    public bool       IsAvailable  => !HasPassenger && path.Count == 0;

    public TrafficNode CurrentNode => CurrentLane?.Edge.from;

    // ---------------------------------------------------------------
    public void AssignPath(Queue<LaneLink> links, Pedestrian passenger)
    {
        path.Clear();
        foreach (var l in links) path.Enqueue(l);
        Passenger   = passenger;
        pickupNode  = passenger.CurrentNode;
        dropoffNode = passenger.Destination;
    }

    public void AssignPath(Queue<LaneLink> links)
    {
        path.Clear();
        foreach (var l in links) path.Enqueue(l);
        pickupNode  = null;
        dropoffNode = null;
    }

    // ---------------------------------------------------------------
    public override void Perceive(World world)
    {
        UpdatePerception(world);
        CheckPassengerEvents();
    }

    void CheckPassengerEvents()
    {
        if (TargetNode == null) return;

        if (pickupNode != null && TargetNode == pickupNode && DistanceToEdgeEnd <= 1f)
        {
            Passenger?.OnPickedUp();
            pickupNode = null;
        }

        if (dropoffNode != null && TargetNode == dropoffNode && DistanceToEdgeEnd <= 1f)
        {
            Passenger?.OnDroppedOff();
            Passenger   = null;
            dropoffNode = null;
        }
    }

    /// <summary>
    /// Follow pre-computed link queue.
    /// On a connector there is exactly one onward link — take it.
    /// On a real road lane, dequeue the next path link if it matches current lane,
    /// otherwise wander.
    /// </summary>
    protected override LaneLink SelectNextLink(NavigationGraph graph)
    {
        // Connector: always one link, follow it
        if (OnConnector)
        {
            var connLinks = graph.GetLinksFrom(CurrentLane);
            return connLinks.Count > 0 ? connLinks[0] : null;
        }

        // Use queued path if the next link starts from our current lane
        if (path.Count > 0 && path.Peek().SourceLane == CurrentLane)
            return path.Dequeue();

        // Wander — pick any available link from current lane
        var links = graph.GetLinksFrom(CurrentLane);
        if (links.Count > 0)
            return links[rng.Next(links.Count)];

        // Try any lane on this edge
        foreach (var lane in CurrentLane.Edge.Lanes)
        {
            var fallback = graph.GetLinksFrom(lane);
            if (fallback.Count > 0)
                return fallback[rng.Next(fallback.Count)];
        }

        return null;
    }

    // ---------------------------------------------------------------
    public override void Deliberate(World world) => ChooseSpeed(world);

    void ChooseSpeed(World world)
    {
        float speedLimit = TrafficLaw.SpeedLimitMs(this);
        desiredSpeed     = speedLimit;

        if (GapAhead < 15f && AheadOnLane != null)
        {
            float f  = Math.Max(0f, GapAhead / 15f);
            desiredSpeed = Math.Min(desiredSpeed, AheadOnLane.Speed * f);
        }

        if (IsChangingLane)
            desiredSpeed *= 0.85f;

        if (DesiredLane >= 0 && DesiredLane != LaneNumber)
        {
            float urgency = 1f - Math.Min(1f, DistanceToEnd / CurrentLane.Edge.Length);
            if (urgency > 0.5f)
                desiredSpeed *= 1f - (urgency - 0.5f) * 0.8f;
        }

        // Junction entry guard — same as AmbientDriver
        float junctionCap = JunctionEntryCap(world.Navigation);
        desiredSpeed = Math.Min(desiredSpeed, junctionCap);

        desiredSpeed = ApplyBrakingConstraints(desiredSpeed);
        Speed        = MoveTowards(Speed, desiredSpeed, acceleration * world.DeltaTime);
    }

    public override void Act(World world) => Move(world);

    public void PickUp(Pedestrian passenger) => Passenger = passenger;
    public void DropOff() { Passenger = null; dropoffNode = null; }
}
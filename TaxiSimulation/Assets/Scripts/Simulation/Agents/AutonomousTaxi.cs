using System;
using System.Collections.Generic;

public enum TaxiState { Idle, EnRouteToPickup, Carrying }

/// <summary>
/// Path-following taxi. Follows a pre-computed sequence of LaneLinks
/// supplied by FleetManager. Picks up and drops off passengers.
/// </summary>
public class AutonomousTaxi : VehicleAgent
{
    readonly Queue<LaneLink> path = new();

    TrafficNode pickupNode;
    TrafficNode dropoffNode;

    // Passenger assigned (en route to pick up) but not yet boarded.
    // Kept separate from Passenger so State = EnRouteToPickup, not Carrying.
    Pedestrian _assignedPassenger;

    // Number of path links in leg 2 (pickup→destination).
    // Used to show per-leg distance: leg1 remaining = path.Count - _leg2LinkCount.
    int _leg2LinkCount;

    float desiredSpeed;
    float acceleration = 4f;

    // ---------------------------------------------------------------
    public Pedestrian Passenger    { get; private set; }
    public bool       HasPassenger => Passenger != null;

    /// <summary>True only when idle: no passenger on board, none assigned, no path.</summary>
    public bool IsAvailable => !HasPassenger && _assignedPassenger == null && path.Count == 0;

    public TaxiState State =>
        HasPassenger   ? TaxiState.Carrying       :
        IsAvailable    ? TaxiState.Idle            :
                         TaxiState.EnRouteToPickup;

    public TrafficNode PickupNodeTarget  => pickupNode;
    public TrafficNode DropoffNodeTarget => dropoffNode;

    public TrafficNode CurrentNode => CurrentLane?.Edge.from;

    /// <summary>
    /// Approximate remaining path distance in logical metres.
    /// Skips connector contribution to avoid brief increases at intersections.
    /// </summary>
    public float EstimatedDistanceRemaining
    {
        get
        {
            // Skip connector distance only when Carrying (short connectors inflate the
            // reading). When EnRouteToPickup, the final connector IS the last hop to
            // the passenger, so include its remaining distance to avoid showing < 10m
            // while still a few meters away.
            float d = (OnConnector && State == TaxiState.Carrying) ? 0f : DistanceToEdgeEnd;

            // EnRouteToPickup: only count leg-1 links (path.Count - _leg2LinkCount).
            // Carrying: count all remaining links (they are all leg-2).
            // This ensures the display reaches ~0 at pickup, then resets to destination distance.
            int limit = (State == TaxiState.EnRouteToPickup)
                ? System.Math.Max(0, path.Count - _leg2LinkCount)
                : int.MaxValue;

            int i = 0;
            foreach (var link in path)
            {
                if (i >= limit) break;
                if (!link.DestLane.Edge.IsConnector)
                    d += link.DestLane.Edge.Length;
                i++;
            }
            return d;
        }
    }

    // ---------------------------------------------------------------
    public void AssignPath(Queue<LaneLink> links, Pedestrian passenger, int leg2LinkCount = 0)
    {
        path.Clear();
        foreach (var l in links) path.Enqueue(l);

        // Do NOT set Passenger here — the taxi hasn't boarded anyone yet.
        // Passenger is set in CheckPassengerEvents when the taxi reaches pickupNode.
        Passenger          = null;
        _assignedPassenger = passenger;
        pickupNode         = passenger.CurrentNode;
        dropoffNode        = passenger.Destination;
        _leg2LinkCount     = leg2LinkCount;
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
        // If the assigned passenger cancelled while we were en route, release them.
        if (_assignedPassenger != null &&
            (_assignedPassenger.State == PedestrianState.Cancelled ||
             _assignedPassenger.State == PedestrianState.Done))
        {
            _assignedPassenger = null;
            pickupNode         = null;
            path.Clear();
            return;
        }

        if (TargetNode == null) return;

        // Pickup: taxi has arrived at the passenger's node
        if (pickupNode != null && TargetNode == pickupNode && DistanceToEdgeEnd <= 1f)
        {
            _assignedPassenger?.OnPickedUp();
            Passenger          = _assignedPassenger;
            _assignedPassenger = null;
            pickupNode         = null;
        }

        // Dropoff: taxi has arrived at the destination node
        if (dropoffNode != null && TargetNode == dropoffNode && DistanceToEdgeEnd <= 1f)
        {
            Passenger?.OnDroppedOff();
            Passenger   = null;
            dropoffNode = null;
        }
    }

    /// <summary>
    /// Follow pre-computed link queue.
    /// On a connector: always take the single onward link.
    /// On a real lane:
    ///   1. Consume stale path entries whose destination we already reached.
    ///   2. Dequeue if the next entry matches the current lane.
    ///   3. If a sibling lane is needed, find an equivalent or trigger lane change.
    ///   4. Wander if no path.
    /// </summary>
    protected override LaneLink SelectNextLink(NavigationGraph graph)
    {
        if (OnConnector)
        {
            var connLinks = graph.GetLinksFrom(CurrentLane);
            return connLinks.Count > 0 ? connLinks[0] : null;
        }

        if (path.Count > 0)
        {
            // Drain stale entries (taxi arrived via sibling connector)
            while (path.Count > 0)
            {
                var peek = path.Peek();

                if (peek.DestLane.Edge == CurrentLane.Edge)
                { path.Dequeue(); continue; }

                if (peek.DestLane.Edge.IsConnector)
                {
                    var onward = graph.GetLinksFrom(peek.DestLane);
                    if (onward.Count > 0 && onward[0].DestLane.Edge == CurrentLane.Edge)
                    { path.Dequeue(); continue; }
                }

                break;
            }

            if (path.Count == 0) goto wander;

            var next = path.Peek();

            if (next.SourceLane == CurrentLane)
                return path.Dequeue();

            if (next.SourceLane.Edge == CurrentLane.Edge)
            {
                var targetEdge = next.DestLane.Edge;
                foreach (var link in graph.GetLinksFrom(CurrentLane))
                {
                    if (link.DestLane.Edge == targetEdge)
                    { path.Dequeue(); return link; }

                    if (link.DestLane.Edge.IsConnector)
                    {
                        var onward = graph.GetLinksFrom(link.DestLane);
                        if (onward.Count > 0 && onward[0].DestLane.Edge == targetEdge)
                        { path.Dequeue(); return link; }
                    }
                }

                return next; // sets DesiredLane for lane change
            }
        }

        wander:
        var links = graph.GetLinksFrom(CurrentLane);
        if (links.Count > 0)
            return links[rng.Next(links.Count)];

        foreach (var lane in CurrentLane.Edge.Lanes)
        {
            var fallback = graph.GetLinksFrom(lane);
            if (fallback.Count > 0)
                return fallback[rng.Next(fallback.Count)];
        }

        return null;
    }

    // ---------------------------------------------------------------
    public override void Deliberate(World world)
    {
        ConsiderPathLaneChange();
        ChooseSpeed(world);
    }

    void ConsiderPathLaneChange()
    {
        if (IsChangingLane || OnConnector) return;
        if (DesiredLane < 0 || DesiredLane == LaneNumber) return;
        if (CurrentLane.Edge.Lanes.Count < 2) return;

        int dir    = DesiredLane > LaneNumber ? 1 : -1;
        int target = LaneNumber + dir;
        BeginLaneChange(target);
    }

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

        // Junction entry guard — skipped when stuck to break circular deadlocks
        float junctionCap = IsStuck ? float.MaxValue : JunctionEntryCap(world.Navigation);
        desiredSpeed = Math.Min(desiredSpeed, junctionCap);

        desiredSpeed = ApplyBrakingConstraints(desiredSpeed);
        Speed        = MoveTowards(Speed, desiredSpeed, acceleration * world.DeltaTime);
    }

    public override void Act(World world) => Move(world);

    public void PickUp(Pedestrian passenger) => Passenger = passenger;
    public void DropOff() { Passenger = null; dropoffNode = null; }
}

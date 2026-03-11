using System.Collections.Generic;
using UnityEngine;

public class FleetManager : Agent
{
    NavigationGraph      _graph;
    List<AutonomousTaxi> _taxis   = new();
    HashSet<Pedestrian>  _pending = new();

    public FleetManager(NavigationGraph graph) { _graph = graph; }

    public void RegisterTaxi(AutonomousTaxi taxi)
    {
        if (!_taxis.Contains(taxi)) _taxis.Add(taxi);
    }

    public void RequestRide(Pedestrian p)
    {
        if (!_pending.Contains(p)) _pending.Add(p);
    }

    public void OnTaxiAvailable(AutonomousTaxi taxi) { }

    public override void Perceive(World world) { }

    public override void Deliberate(World world)
    {
        // Drop pedestrians no longer needing service
        _pending.RemoveWhere(p =>
            p.State == PedestrianState.Cancelled ||
            p.State == PedestrianState.Done      ||
            p.State == PedestrianState.Riding    ||
            p.State == PedestrianState.Matched);

        foreach (var p in new List<Pedestrian>(_pending))
        {
            var taxi = FindNearestIdleTaxi(p.CurrentNode);
            if (taxi == null) continue;

            var path = BuildFullLinkPath(taxi.CurrentNode, taxi.CurrentLane,
                                         p.CurrentNode, p.Destination);
            if (path == null)
            {
                Debug.LogWarning(
                    $"[FleetManager] No path for pedestrian at node {p.CurrentNode?.id} " +
                    $"→ {p.Destination?.id}. Cancelling.");
                p.State = PedestrianState.Cancelled;
                continue;
            }

            taxi.AssignPath(path, p);
            p.OnMatched();
        }
    }

    public override void Act(World world) { }

    // ---------------------------------------------------------------
    // Find nearest idle taxi by road distance (connector edges ignored)
    // ---------------------------------------------------------------
    AutonomousTaxi FindNearestIdleTaxi(TrafficNode targetNode)
    {
        AutonomousTaxi best     = null;
        float          bestDist = float.MaxValue;

        foreach (var taxi in _taxis)
        {
            if (!taxi.IsAvailable) continue;
            var edgePath = Pathfinder.FindPath(_graph, taxi.CurrentNode, targetNode);
            if (edgePath == null) continue;

            float dist = 0f;
            foreach (var e in edgePath)
                if (!e.IsConnector) dist += e.Length;

            if (dist < bestDist) { bestDist = dist; best = taxi; }
        }

        return best;
    }

    // ---------------------------------------------------------------
    // Build a LaneLink queue for the full trip: taxi → pickup → dropoff.
    //
    // CONNECTOR AWARENESS:
    // After the connector-lane refactor every lane transition goes:
    //   realLane → [ToConnector link] → connectorLane → [FromConnector link] → realLane
    //
    // Dijkstra returns a raw edge list that may include connector edges.
    // We strip connectors from that list to get only real-road waypoints,
    // then for each consecutive pair (currentEdge → nextRealEdge) we find
    // the two-hop link chain: currentLane → connectorLane → lane on nextRealEdge.
    // ---------------------------------------------------------------
    Queue<LaneLink> BuildFullLinkPath(
        TrafficNode taxiNode,      Lane       taxiLane,
        TrafficNode passengerNode, TrafficNode destinationNode)
    {
        var edgePath1 = Pathfinder.FindPath(_graph, taxiNode,      passengerNode);
        var edgePath2 = Pathfinder.FindPath(_graph, passengerNode, destinationNode);
        if (edgePath1 == null || edgePath2 == null) return null;

        // Build a list of REAL road edges only — connectors are resolved implicitly
        var realEdges = new List<TrafficEdge>();
        foreach (var e in edgePath1) if (!e.IsConnector) realEdges.Add(e);
        foreach (var e in edgePath2) if (!e.IsConnector) realEdges.Add(e);

        if (realEdges.Count == 0) return new Queue<LaneLink>();

        var  result      = new Queue<LaneLink>();
        Lane currentLane = taxiLane;

        foreach (var nextRealEdge in realEdges)
        {
            // Already sitting on this edge (taxi starts here, or pickup == dropoff edge)
            if (currentLane.Edge == nextRealEdge) continue;

            var chain = FindLinkChainToEdge(currentLane, nextRealEdge);
            if (chain == null)
            {
                Debug.LogWarning(
                    $"[FleetManager] No link chain from " +
                    $"edge({currentLane.Edge.from.id}→{currentLane.Edge.to.id}) " +
                    $"to edge({nextRealEdge.from.id}→{nextRealEdge.to.id})");
                return null;
            }

            foreach (var link in chain)
                result.Enqueue(link);

            currentLane = chain[chain.Count - 1].DestLane;
        }

        return result;
    }

    // ---------------------------------------------------------------
    // Return the LaneLink sequence that moves currentLane to a lane on
    // targetEdge, hopping through exactly one connector layer.
    //
    //   Case A — direct link (pre-refactor fallback, kept for safety):
    //     currentLane --link--> lane on targetEdge
    //
    //   Case B — normal post-refactor path:
    //     currentLane --toConnLink--> connectorLane --fromConnLink--> lane on targetEdge
    //
    //   Both cases also try sibling lanes on the same edge when the taxi
    //   happens to be in a lane that doesn't have a direct exit.
    // ---------------------------------------------------------------
    List<LaneLink> FindLinkChainToEdge(Lane currentLane, TrafficEdge targetEdge)
    {
        // Try current lane first, then siblings
        foreach (var candidate in CandidateLanes(currentLane))
        {
            var links = _graph.GetLinksFrom(candidate);
            foreach (var link in links)
            {
                // Case A: direct
                if (link.DestLane.Edge == targetEdge)
                    return new List<LaneLink> { link };

                // Case B: through connector
                if (!link.DestLane.Edge.IsConnector) continue;

                var connLinks = _graph.GetLinksFrom(link.DestLane);
                foreach (var connLink in connLinks)
                {
                    if (connLink.DestLane.Edge == targetEdge)
                        return new List<LaneLink> { link, connLink };
                }
            }
        }

        return null;
    }

    // Current lane first, then all siblings on the same edge, favouring
    // same lane number to keep the taxi in a predictable lane.
    IEnumerable<Lane> CandidateLanes(Lane lane)
    {
        yield return lane;

        foreach (var sibling in lane.Edge.Lanes)
        {
            if (sibling == lane) continue;
            // Same lane number on a different edge shouldn't appear here,
            // but guard anyway.
            yield return sibling;
        }
    }
}
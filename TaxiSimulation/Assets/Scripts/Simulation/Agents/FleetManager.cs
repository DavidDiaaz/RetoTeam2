using System.Collections.Generic;
using UnityEngine;

public class FleetManager : Agent
{
    NavigationGraph      _graph;
    List<AutonomousTaxi> _taxis   = new();
    HashSet<Pedestrian>  _pending = new();

    public IReadOnlyList<AutonomousTaxi>    Taxis   => _taxis;
    public IReadOnlyCollection<Pedestrian>  Pending => _pending;

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

            var (taxiNode, taxiLane) = GetEffectiveTaxiPos(taxi);
            if (taxiNode == null || taxiLane == null) continue;

            var (path, leg2Count) = BuildFullLinkPath(taxiNode, taxiLane,
                                                      p.CurrentNode, p.Destination);
            if (path == null)
            {
                // Don't cancel — retry next tick. The pedestrian's tolerance timer
                // will cancel them if they remain unservable for too long.
                Debug.LogWarning(
                    $"[FleetManager] No path for pedestrian at node {p.CurrentNode?.id} " +
                    $"→ {p.Destination?.id}. Will retry.");
                continue;
            }

            taxi.AssignPath(path, p, leg2Count);
            p.OnMatched();
        }
    }

    public override void Act(World world) { }

    // ---------------------------------------------------------------
    // Returns the real (non-connector) node and lane the taxi is on or
    // about to enter. If the taxi is mid-connector, we use the connector's
    // destination real lane so the pathfinder works with real-graph nodes.
    // ---------------------------------------------------------------
    (TrafficNode node, Lane lane) GetEffectiveTaxiPos(AutonomousTaxi taxi)
    {
        var lane = taxi.CurrentLane;
        if (lane == null) return (null, null);

        if (lane.Edge.IsConnector)
        {
            var links = _graph.GetLinksFrom(lane);
            if (links.Count > 0)
            {
                var realLane = links[0].DestLane;
                return (realLane.Edge.from, realLane);
            }
            return (null, null);
        }

        return (lane.Edge.from, lane);
    }

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
            var (taxiNode, _) = GetEffectiveTaxiPos(taxi);
            if (taxiNode == null) continue;

            var edgePath = Pathfinder.FindPath(_graph, taxiNode, targetNode);
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
    // Returns the queue AND the number of leg-2 links (pickup→destination)
    // so the taxi can compute per-leg distance for the UI.
    // ---------------------------------------------------------------
    (Queue<LaneLink>, int) BuildFullLinkPath(
        TrafficNode taxiNode,      Lane        taxiLane,
        TrafficNode passengerNode, TrafficNode destinationNode)
    {
        var edgePath1 = Pathfinder.FindPath(_graph, taxiNode,      passengerNode);
        var edgePath2 = Pathfinder.FindPath(_graph, passengerNode, destinationNode);
        if (edgePath1 == null || edgePath2 == null) return (null, 0);

        var realEdges1 = new List<TrafficEdge>();
        foreach (var e in edgePath1) if (!e.IsConnector) realEdges1.Add(e);

        var realEdges2 = new List<TrafficEdge>();
        foreach (var e in edgePath2) if (!e.IsConnector) realEdges2.Add(e);

        var  result      = new Queue<LaneLink>();
        Lane currentLane = taxiLane;

        // ── Leg 1: taxi → pickup ──────────────────────────────────────
        foreach (var nextRealEdge in realEdges1)
        {
            if (currentLane.Edge == nextRealEdge) continue;

            var chain = FindLinkChainToEdge(currentLane, nextRealEdge);
            if (chain == null)
            {
                Debug.LogWarning(
                    $"[FleetManager] No link chain (leg1) " +
                    $"from edge({currentLane.Edge.from.id}→{currentLane.Edge.to.id}) " +
                    $"to edge({nextRealEdge.from.id}→{nextRealEdge.to.id})");
                return (null, 0);
            }

            foreach (var link in chain) result.Enqueue(link);
            currentLane = chain[chain.Count - 1].DestLane;
        }

        // ── Leg 2: pickup → destination ───────────────────────────────
        int leg2Count = 0;
        foreach (var nextRealEdge in realEdges2)
        {
            if (currentLane.Edge == nextRealEdge) continue;

            var chain = FindLinkChainToEdge(currentLane, nextRealEdge);
            if (chain == null)
            {
                Debug.LogWarning(
                    $"[FleetManager] No link chain (leg2) " +
                    $"from edge({currentLane.Edge.from.id}→{currentLane.Edge.to.id}) " +
                    $"to edge({nextRealEdge.from.id}→{nextRealEdge.to.id})");
                return (null, 0);
            }

            foreach (var link in chain) result.Enqueue(link);
            leg2Count += chain.Count;
            currentLane = chain[chain.Count - 1].DestLane;
        }

        return (result, leg2Count);
    }

    // ---------------------------------------------------------------
    // Return the LaneLink sequence that moves currentLane to a lane on
    // targetEdge, hopping through exactly one connector layer.
    // ---------------------------------------------------------------
    List<LaneLink> FindLinkChainToEdge(Lane currentLane, TrafficEdge targetEdge)
    {
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

    IEnumerable<Lane> CandidateLanes(Lane lane)
    {
        yield return lane;

        foreach (var sibling in lane.Edge.Lanes)
        {
            if (sibling == lane) continue;
            yield return sibling;
        }
    }
}

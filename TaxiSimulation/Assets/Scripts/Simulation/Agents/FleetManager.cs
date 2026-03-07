using System;
using System.Collections.Generic;

public class FleetManager : Agent
{
    NavigationGraph            graph;
    List<AutonomousTaxi>       taxis       = new();
    HashSet<Pedestrian>        pending     = new();

    public FleetManager(NavigationGraph graph)
    {
        this.graph = graph;
    }

    // ---------------------------------------------------------------
    // Registration
    // ---------------------------------------------------------------
    public void RegisterTaxi(AutonomousTaxi taxi)
    {
        if (!taxis.Contains(taxi))
            taxis.Add(taxi);
    }

    // Called by Pedestrian.Deliberate
    public void RequestRide(Pedestrian p)
    {
        if (!pending.Contains(p))
            pending.Add(p);
    }

    // Called by AutonomousTaxi when it completes a ride
    public void OnTaxiAvailable(AutonomousTaxi taxi)
    {
        // Nothing to do — taxi is already Idle, will be picked up next match cycle
    }

    // ---------------------------------------------------------------
    public override void Perceive(World world) { }

    public override void Deliberate(World world)
    {
        // Remove cancelled or done pedestrians
        pending.RemoveWhere(p =>
            p.State == PedestrianState.Cancelled ||
            p.State == PedestrianState.Done       ||
            p.State == PedestrianState.Riding     ||
            p.State == PedestrianState.Matched);

        // Try to match each pending pedestrian
        foreach (var p in pending.ToArray())
        {
            var taxi = FindNearestIdleTaxi(p.CurrentNode);
            if (taxi == null) continue;

            var path = BuildFullPath(taxi.CurrentNode, p.CurrentNode, p.Destination);
            if (path == null)
            {
                UnityEngine.Debug.LogWarning(
                    $"[FleetManager] No path found for pedestrian at node {p.CurrentNode.id} " +
                    $"→ destination {p.Destination.id}. Cancelling ride request.");
                p.State = PedestrianState.Cancelled;
                continue;
            }

            taxi.AssignPath(path, p);
            p.OnMatched();
        }
    }

    public override void Act(World world) { }

    // ---------------------------------------------------------------
    // Find nearest idle taxi by actual road distance (Dijkstra)
    // ---------------------------------------------------------------
    AutonomousTaxi FindNearestIdleTaxi(TrafficNode targetNode)
    {
        AutonomousTaxi best     = null;
        float          bestDist = float.MaxValue;

        foreach (var taxi in taxis)
        {
            if (!taxi.IsAvailable) continue;

            var path = Pathfinder.FindPath(graph, taxi.CurrentNode, targetNode);
            if (path == null) continue;

            float dist = 0f;
            foreach (var edge in path)
                dist += edge.Length;

            if (dist < bestDist)
            {
                bestDist = dist;
                best     = taxi;
            }
        }

        return best;
    }

    // ---------------------------------------------------------------
    // Build path: taxiNode → passengerNode → destinationNode
    // ---------------------------------------------------------------
    Queue<TrafficEdge> BuildFullPath(
        TrafficNode taxiNode,
        TrafficNode passengerNode,
        TrafficNode destinationNode)
    {
        var leg1 = Pathfinder.FindPath(graph, taxiNode,      passengerNode);
        var leg2 = Pathfinder.FindPath(graph, passengerNode, destinationNode);

        if (leg1 == null || leg2 == null) return null;

        // Combine both legs into one queue
        var full = new Queue<TrafficEdge>(leg1);
        foreach (var edge in leg2)
            full.Enqueue(edge);

        return full;
    }
}
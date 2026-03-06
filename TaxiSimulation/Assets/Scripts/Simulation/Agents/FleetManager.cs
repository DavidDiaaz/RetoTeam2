using System;
using System.Collections.Generic;

public class FleetManager : Agent
{
    NavigationGraph            graph;
    List<AutonomousTaxi>       taxis       = new();
    List<Pedestrian>           pending     = new();

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
        pending.RemoveAll(p =>
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
            if (path == null) continue;

            taxi.AssignPath(path, p);
            p.OnMatched();
        }
    }

    public override void Act(World world) { }

    // ---------------------------------------------------------------
    // Find nearest idle taxi by BFS hop count from targetNode
    // ---------------------------------------------------------------
    AutonomousTaxi FindNearestIdleTaxi(TrafficNode targetNode)
    {
        // BFS from targetNode outward — first idle taxi found wins
        var visited = new HashSet<int>();
        var queue   = new Queue<TrafficNode>();

        queue.Enqueue(targetNode);
        visited.Add(targetNode.id);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();

            // Check if any idle taxi is on an edge whose from == this node
            foreach (var taxi in taxis)
            {
                if (!taxi.IsAvailable) continue;
                if (taxi.CurrentNode == node) return taxi;
            }

            // Expand — traverse edges in reverse (who points to this node?)
            foreach (var n in graph.nodes.Values)
            {
                foreach (var edge in n.Outgoing)
                {
                    if (edge.to == node && !visited.Contains(n.id))
                    {
                        visited.Add(n.id);
                        queue.Enqueue(n);
                    }
                }
            }
        }

        return null;
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
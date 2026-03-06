using System;
using System.Collections.Generic;

public static class Pathfinder
{
    /// <summary>
    /// Finds the shortest path (by edge length) from startNode to goalNode.
    /// Returns an ordered Queue of TrafficEdges to follow, or null if no path exists.
    /// </summary>
    public static Queue<TrafficEdge> FindPath(
        NavigationGraph graph,
        TrafficNode     start,
        TrafficNode     goal)
    {
        if (start == goal) return new Queue<TrafficEdge>();

        // dist[nodeId] = best cumulative distance found so far
        var dist    = new Dictionary<int, float>();
        // prev[nodeId] = (edge taken to reach this node, previous node id)
        var prev    = new Dictionary<int, (TrafficEdge edge, int fromId)>();
        // Min-heap: (cumulativeDistance, nodeId)
        var queue   = new SortedSet<(float cost, int id)>(new CostComparer());

        foreach (var node in graph.nodes.Values)
            dist[node.id] = float.MaxValue;

        dist[start.id] = 0f;
        queue.Add((0f, start.id));

        while (queue.Count > 0)
        {
            var (cost, currentId) = queue.Min;
            queue.Remove(queue.Min);

            if (currentId == goal.id)
                return ReconstructPath(prev, start.id, goal.id, graph);

            if (cost > dist[currentId]) continue;

            var current = graph.GetNode(currentId);

            foreach (var edge in current.Outgoing)
            {
                int   neighborId = edge.to.id;
                float newCost    = dist[currentId] + edge.Length;

                if (newCost < dist[neighborId])
                {
                    dist[neighborId] = newCost;
                    prev[neighborId] = (edge, currentId);
                    queue.Add((newCost, neighborId));
                }
            }
        }

        return null; // no path found
    }

    static Queue<TrafficEdge> ReconstructPath(
        Dictionary<int, (TrafficEdge edge, int fromId)> prev,
        int startId,
        int goalId,
        NavigationGraph graph)
    {
        var edges = new List<TrafficEdge>();
        int current = goalId;

        while (current != startId)
        {
            var (edge, fromId) = prev[current];
            edges.Add(edge);
            current = fromId;
        }

        edges.Reverse();

        var queue = new Queue<TrafficEdge>();
        foreach (var e in edges)
            queue.Enqueue(e);

        return queue;
    }

    // SortedSet needs a comparer that handles equal costs by id
    class CostComparer : IComparer<(float cost, int id)>
    {
        public int Compare((float cost, int id) a, (float cost, int id) b)
        {
            int c = a.cost.CompareTo(b.cost);
            return c != 0 ? c : a.id.CompareTo(b.id);
        }
    }
}
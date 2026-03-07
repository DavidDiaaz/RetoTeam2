using UnityEngine;
using System;
using System.Collections.Generic;

public class NavGraphBuilder : MonoBehaviour
{
    [Header("Scale")]
    [Tooltip("Real-world meters per Unity unit. 4.5 = one road tile width.")]
    public float metersPerUnit = 4.5f;

    [Header("Snapping")]
    [Tooltip("Lane endpoints within this world-unit distance are merged into one node.")]
    public float snapDistance = 1f;

    [Header("Validation")]
    [Tooltip("Edges shorter than this (meters) are skipped. Must be > vehicle length (4.5m).")]
    public float minEdgeLength = 6f;

    public static event Action<TrafficNode, TrafficLight.State> OnLightStateChanged;

    readonly Dictionary<TrafficNode, TrafficLight.State> prevLightStates = new();

    public NavigationGraph Build(out Dictionary<Lane, LaneView> laneViews)
    {
        laneViews = new Dictionary<Lane, LaneView>();

        var graph  = new NavigationGraph();
        var lanes  = FindObjectsByType<LanePathAuthoring>(FindObjectsSortMode.None);
        var lights = FindObjectsByType<TrafficLightAuthoring>(FindObjectsSortMode.None);

        // ---- 1. Collect unique node positions from lane endpoints only ----
        // No intersection math — nodes exist only where lanes start or end.
        // Nearby endpoints snap to the same node.
        var nodePositions = new List<Vector3>();

        foreach (var lane in lanes)
        {
            AddUnique(nodePositions, Flatten(lane.Start));
            AddUnique(nodePositions, Flatten(lane.End));
        }

        // ---- 2. Create one node per unique position ----
        int nextId = 0;
        var nodeByIndex = new List<(Vector3 pos, TrafficNode node)>();

        foreach (var pos in nodePositions)
        {
            var node = new TrafficNode(nextId++);
            graph.AddNode(node);
            nodeByIndex.Add((pos, node));
            Debug.Log($"[NavGraphBuilder] Node {node.id} at {pos}");
        }

        // ---- 3. Assign traffic lights ----
        foreach (var tla in lights)
        {
            var light = new TrafficLight { CurrentState = tla.InitialState };
            tla.Light = light;

            Vector3 tlaPos = Flatten(tla.transform.position);

            foreach (var (pos, node) in nodeByIndex)
            {
                if (Vector3.Distance(pos, tlaPos) <= tla.Radius && node.Light == null)
                {
                    node.Light = light;
                    prevLightStates[node] = tla.InitialState;
                }
            }
        }

        // ---- 4. Build one edge per lane ----
        foreach (var lanePath in lanes)
        {
            Vector3 start = Flatten(lanePath.Start);
            Vector3 end   = Flatten(lanePath.End);

            TrafficNode fromNode = FindClosestNode(nodeByIndex, start);
            TrafficNode toNode   = FindClosestNode(nodeByIndex, end);

            if (fromNode == null || toNode == null)
            {
                Debug.LogWarning($"[NavGraphBuilder] Lane '{lanePath.name}' could not find nodes — skipped.");
                continue;
            }

            if (fromNode == toNode)
            {
                Debug.LogWarning($"[NavGraphBuilder] Lane '{lanePath.name}' start and end snapped to same node {fromNode.id} — skipped. Move endpoints further apart or reduce snapDistance.");
                continue;
            }

            float meters = Vector3.Distance(start, end) * metersPerUnit;

            if (meters < minEdgeLength)
            {
                Debug.LogWarning($"[NavGraphBuilder] Lane '{lanePath.name}' {fromNode.id}→{toNode.id} is {meters:F2}m — too short, skipped.");
                continue;
            }

            var edge = graph.AddEdge(fromNode.id, toNode.id, 1, meters, lanePath.RoadClass, lanePath.SpeedLimit);

            var laneView = new LaneView
            {
                Lane       = edge.Lanes[0],
                Waypoints  = new Vector3[] { start, end },
                LaneNumber = lanePath.LaneNumber
            };
            laneView.Build();
            laneViews[edge.Lanes[0]] = laneView;

            Debug.Log($"[NavGraphBuilder] '{lanePath.name}' {fromNode.id}→{toNode.id} {meters:F1}m ~{meters / (lanePath.SpeedLimit * 1000f / 3600f):F1}s");
        }

        // ---- 5. Report dead ends ----
        foreach (var node in graph.nodes.Values)
            if (node.Outgoing.Count == 0)
                Debug.LogWarning($"[NavGraphBuilder] Node {node.id} is a dead end.");

        return graph;
    }

    public void PollLightEvents(NavigationGraph graph)
    {
        foreach (var node in graph.nodes.Values)
        {
            if (node.Light == null) continue;
            var current = node.Light.CurrentState;
            if (!prevLightStates.TryGetValue(node, out var prev) || prev != current)
            {
                prevLightStates[node] = current;
                OnLightStateChanged?.Invoke(node, current);
            }
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    // Flatten Y so all comparisons are in XZ only
    Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // Add point only if no existing point is within snapDistance
    void AddUnique(List<Vector3> points, Vector3 p)
    {
        foreach (var existing in points)
            if (Vector3.Distance(existing, p) <= snapDistance) return;
        points.Add(p);
    }

    // Find the node whose position is closest to p (within snapDistance)
    TrafficNode FindClosestNode(List<(Vector3 pos, TrafficNode node)> nodes, Vector3 p)
    {
        TrafficNode best  = null;
        float       bestD = float.MaxValue;

        foreach (var (pos, node) in nodes)
        {
            float d = Vector3.Distance(pos, p);
            if (d < bestD && d <= snapDistance)
            {
                bestD = d;
                best  = node;
            }
        }

        return best;
    }
}
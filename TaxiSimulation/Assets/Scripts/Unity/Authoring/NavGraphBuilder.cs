using UnityEngine;
using System;
using System.Collections.Generic;

public class NavGraphBuilder : MonoBehaviour
{
    [Header("Scale")]
    [Tooltip("Real-world meters per Unity unit.")]
    public float metersPerUnit = 4.5f;

    public static event Action<TrafficNode, TrafficLight.State> OnLightStateChanged;

    readonly Dictionary<TrafficNode, TrafficLight.State> prevLightStates = new();

    public List<(RoadSegment seg, TrafficEdge edge)> RoadEdges { get; private set; } = new();

    Dictionary<RoadSegment, TrafficNode> startNodes = new();
    Dictionary<RoadSegment, TrafficNode> endNodes   = new();

    public NavigationGraph Build(out Dictionary<Lane, LaneView> laneViews)
    {
        laneViews  = new Dictionary<Lane, LaneView>();
        RoadEdges  = new List<(RoadSegment, TrafficEdge)>();
        startNodes = new Dictionary<RoadSegment, TrafficNode>();
        endNodes   = new Dictionary<RoadSegment, TrafficNode>();

        var graph = new NavigationGraph();
        int nextId = 0;

        // Collect all segments from all Road objects
        var roads    = FindObjectsByType<Road>(FindObjectsSortMode.None);
        var segments = new List<RoadSegment>();

        foreach (var road in roads)
            foreach (var seg in road.Segments)
                if (seg != null) segments.Add(seg);

        // Fallback — if no Roads with segments, try standalone RoadSegments
        if (segments.Count == 0)
        {
            var standalone = FindObjectsByType<RoadSegment>(FindObjectsSortMode.None);
            segments.AddRange(standalone);
            if (standalone.Length > 0)
                Debug.LogWarning("[NavGraphBuilder] No Road objects with segments found. Using standalone RoadSegments.");
        }

        // ---- Pass 1: segments → edges ----
        foreach (var seg in segments)
        {
            var fromNode = new TrafficNode(nextId++);
            var toNode   = new TrafficNode(nextId++);

            // Human-readable labels for debugging
            fromNode.Label = $"{seg.name} START";
            toNode.Label   = $"{seg.name} END";

            graph.AddNode(fromNode);
            graph.AddNode(toNode);

            startNodes[seg] = fromNode;
            endNodes[seg]   = toNode;

            if (seg.HasTrafficLight)
            {
                toNode.Light = new TrafficLight
                {
                    CurrentState   = seg.InitialState,
                    GreenDuration  = seg.GreenDuration,
                    YellowDuration = seg.YellowDuration,
                    RedDuration    = seg.RedDuration
                };
                prevLightStates[toNode] = seg.InitialState;
            }

            float meters = seg.WorldLength * metersPerUnit;

            if (meters < 6f)
            {
                Debug.LogWarning($"[NavGraphBuilder] '{seg.name}' is {meters:F1}m — too short, skipped.");
                continue;
            }

            var edge = graph.AddEdge(
                fromNode.id, toNode.id,
                seg.LaneCount,
                meters,
                seg.RoadClass);

            for (int i = 0; i < seg.LaneCount; i++)
            {
                var laneView = new LaneView
                {
                    Lane      = edge.Lanes[i],
                    Waypoints = new Vector3[] { seg.LaneStartPosition(i), seg.LaneEndPosition(i) },
                    LaneNumber = i
                };
                laneView.Build();
                laneViews[edge.Lanes[i]] = laneView;
            }

            RoadEdges.Add((seg, edge));

            Debug.Log(
                $"[NavGraphBuilder] '{seg.name}' [{fromNode.id}→{toNode.id}] " +
                $"{meters:F1}m {seg.LaneCount} lanes {seg.RoadClass} {seg.SpeedLimit}km/h");
        }

        // ---- Pass 2: connections ----
        var connections = FindObjectsByType<RoadConnection>(FindObjectsSortMode.None);

        foreach (var conn in connections)
        {
            if (!conn.IsValid)
            {
                Debug.LogWarning($"[NavGraphBuilder] '{conn.name}' is invalid — skipped.");
                continue;
            }

            if (!endNodes.TryGetValue(conn.SourceRoad, out var fromNode) ||
                !startNodes.TryGetValue(conn.TargetRoad, out var toNode))
            {
                Debug.LogWarning($"[NavGraphBuilder] '{conn.name}' references unknown segment — skipped.");
                continue;
            }

            float meters = conn.WorldLength * metersPerUnit;
            if (meters < 1f) meters = 1f;

            var edge = graph.AddEdge(
                fromNode.id, toNode.id,
                laneCount:         1,
                length:            meters,
                roadClass:         conn.RoadClass,
                entryLaneRequired: conn.TargetLane);

            edge.IsConnection = true;

            var laneView = new LaneView
            {
                Lane      = edge.Lanes[0],
                Waypoints = new Vector3[] { conn.StartPoint, conn.EndPoint },
                LaneNumber = 0
            };
            laneView.Build();
            laneViews[edge.Lanes[0]] = laneView;

            Debug.Log(
                $"[NavGraphBuilder] Connection '{conn.name}' " +
                $"{conn.SourceRoad.name}[L{conn.SourceLane}] → " +
                $"{conn.TargetRoad.name}[L{conn.TargetLane}] " +
                $"{meters:F1}m entryLane={conn.TargetLane}");
        }

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
}
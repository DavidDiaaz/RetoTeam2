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

    public List<(RoadSegment road, TrafficEdge edge)> RoadEdges { get; private set; } = new();

    Dictionary<RoadSegment, TrafficNode> startNodes = new();
    Dictionary<RoadSegment, TrafficNode> endNodes   = new();

    public NavigationGraph Build(out Dictionary<Lane, LaneView> laneViews)
    {
        laneViews  = new Dictionary<Lane, LaneView>();
        RoadEdges  = new List<(RoadSegment, TrafficEdge)>();
        startNodes = new Dictionary<RoadSegment, TrafficNode>();
        endNodes   = new Dictionary<RoadSegment, TrafficNode>();

        var graph    = new NavigationGraph();
        var segments = FindObjectsByType<RoadSegment>(FindObjectsSortMode.None);

        int nextId = 0;

        // ---- Pass 1: road segments ----
        foreach (var road in segments)
        {
            var fromNode = new TrafficNode(nextId++);
            var toNode   = new TrafficNode(nextId++);

            graph.AddNode(fromNode);
            graph.AddNode(toNode);

            startNodes[road] = fromNode;
            endNodes[road]   = toNode;

            if (road.HasTrafficLight)
            {
                toNode.Light = new TrafficLight
                {
                    CurrentState   = road.InitialState,
                    GreenDuration  = road.GreenDuration,
                    YellowDuration = road.YellowDuration,
                    RedDuration    = road.RedDuration
                };
                prevLightStates[toNode] = road.InitialState;
            }

            float meters = road.WorldLength * metersPerUnit;

            if (meters < 6f)
            {
                Debug.LogWarning(
                    $"[NavGraphBuilder] '{road.name}' is {meters:F1}m — too short, skipped.");
                continue;
            }

            var edge = graph.AddEdge(
                fromNode.id, toNode.id,
                road.LaneCount,
                meters,
                road.RoadClass);

            for (int i = 0; i < road.LaneCount; i++)
            {
                var laneView = new LaneView
                {
                    Lane      = edge.Lanes[i],
                    Waypoints = new Vector3[]
                    {
                        road.LaneStartPosition(i),
                        road.LaneEndPosition(i)
                    },
                    LaneNumber = i
                };
                laneView.Build();
                laneViews[edge.Lanes[i]] = laneView;
            }

            RoadEdges.Add((road, edge));

            Debug.Log(
                $"[NavGraphBuilder] '{road.name}' {fromNode.id}→{toNode.id} " +
                $"{meters:F1}m {road.LaneCount} lanes {road.RoadClass} {road.SpeedLimit}km/h");
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
                Debug.LogWarning(
                    $"[NavGraphBuilder] '{conn.name}' references unknown road — skipped.");
                continue;
            }

            float meters = conn.WorldLength * metersPerUnit;
            if (meters < 1f) meters = 1f;

            var edge = graph.AddEdge(
                fromNode.id, toNode.id,
                laneCount:          1,
                length:             meters,
                roadClass:          conn.RoadClass,
                entryLaneRequired:  conn.TargetLane);

            edge.IsConnection = true; // prevents SimulationManager from despawning mid-arc

            var laneView = new LaneView
            {
                Lane      = edge.Lanes[0],
                Waypoints = new Vector3[]
                {
                    conn.StartPoint,
                    conn.EndPoint
                },
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
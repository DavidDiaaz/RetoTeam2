using UnityEngine;
using System;
using System.Collections.Generic;

public class NavGraphBuilder : MonoBehaviour
{
    [Header("Scale")]
    public float metersPerUnit = 4.5f;

    public static event Action<TrafficNode, TrafficLight.State> OnLightStateChanged;
    readonly Dictionary<TrafficNode, TrafficLight.State> prevLightStates = new();

    public Dictionary<RoadSegment, TrafficEdge> SegmentEdges { get; private set; } = new();
    public List<(RoadSegment seg, TrafficEdge edge)> RoadEdges { get; private set; } = new();

    Dictionary<RoadSegment, TrafficNode> startNodes = new();
    Dictionary<RoadSegment, TrafficNode> endNodes   = new();

    public NavigationGraph Build(out Dictionary<Lane, LaneView> laneViews)
    {
        laneViews    = new Dictionary<Lane, LaneView>();
        SegmentEdges = new Dictionary<RoadSegment, TrafficEdge>();
        RoadEdges    = new List<(RoadSegment, TrafficEdge)>();
        startNodes   = new Dictionary<RoadSegment, TrafficNode>();
        endNodes     = new Dictionary<RoadSegment, TrafficNode>();

        var graph  = new NavigationGraph();
        int nextId = 0;

        // ---- Collect all segments ----
        var roads    = FindObjectsByType<Road>(FindObjectsSortMode.None);
        var segments = new List<RoadSegment>();
        foreach (var road in roads)
            foreach (var seg in road.Segments)
                if (seg != null) segments.Add(seg);

        if (segments.Count == 0)
            foreach (var seg in FindObjectsByType<RoadSegment>(FindObjectsSortMode.None))
                segments.Add(seg);

        // ---- Pass 1: build one edge per road segment ----
        foreach (var seg in segments)
        {
            float meters = seg.WorldLength * metersPerUnit;
            if (meters < 1f)
            {
                Debug.LogWarning($"[NavGraphBuilder] '{seg.name}' is {meters:F1}m — skipped.");
                continue;
            }

            var fromNode = new TrafficNode(nextId++) { Label = $"{seg.name} START" };
            var toNode   = new TrafficNode(nextId++) { Label = $"{seg.name} END" };

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

            var edge = graph.AddEdge(fromNode.id, toNode.id, seg.LaneCount, meters, seg.RoadClass);

            for (int i = 0; i < seg.LaneCount; i++)
            {
                edge.Lanes[i].LaneNumber = i;

                var laneView = new LaneView
                {
                    Lane       = edge.Lanes[i],
                    Waypoints  = new Vector3[] { seg.LaneStartPosition(i), seg.LaneEndPosition(i) },
                    LaneNumber = i
                };
                laneView.Build();
                laneViews[edge.Lanes[i]] = laneView;
            }

            SegmentEdges[seg] = edge;
            RoadEdges.Add((seg, edge));
        }

        // ---- Pass 2: explicit RoadConnection cross-road connectors ----
        var connections = FindObjectsByType<RoadConnection>(FindObjectsSortMode.None);

        foreach (var conn in connections)
        {
            if (!conn.IsValid)
            {
                Debug.LogWarning($"[NavGraphBuilder] '{conn.name}' invalid — skipped.");
                continue;
            }

            if (!SegmentEdges.TryGetValue(conn.SourceRoad, out var srcEdge) ||
                !SegmentEdges.TryGetValue(conn.TargetRoad, out var dstEdge))
            {
                Debug.LogWarning($"[NavGraphBuilder] '{conn.name}' references unknown segment — skipped.");
                continue;
            }

            if (conn.SourceLane >= srcEdge.Lanes.Count || conn.TargetLane >= dstEdge.Lanes.Count)
            {
                Debug.LogWarning($"[NavGraphBuilder] '{conn.name}' lane index out of range — skipped.");
                continue;
            }

            Lane srcLane = srcEdge.Lanes[conn.SourceLane];
            Lane dstLane = dstEdge.Lanes[conn.TargetLane];

            bool  samePRoad   = conn.SourceRoad.ParentRoad == conn.TargetRoad.ParentRoad;
            float mergeNorm   = samePRoad ? 0f : conn.ComputeMergePosition(metersPerUnit);
            bool  isSideEntry = !samePRoad && mergeNorm > 0.01f;

            Vector3 srcEnd   = conn.StartPoint;
            Vector3 dstStart = conn.EndPoint;

            Lane connLane = graph.AddConnectorLane(
                srcLane, dstLane, srcEnd, dstStart,
                mergeNorm, metersPerUnit, isSideEntry);

            var connView = new LaneView
            {
                Lane       = connLane,
                Waypoints  = new Vector3[] { srcEnd, dstStart },
                LaneNumber = 0
            };
            connView.Build();
            laneViews[connLane] = connView;
        }

        // ---- Pass 3: auto-link same-road segment continuations ----
        foreach (var road in roads)
        {
            var segs = road.Segments;
            for (int s = 0; s < segs.Count - 1; s++)
            {
                var a = segs[s];
                var b = segs[s + 1];
                if (a == null || b == null) continue;
                if (!SegmentEdges.TryGetValue(a, out var edgeA)) continue;
                if (!SegmentEdges.TryGetValue(b, out var edgeB)) continue;

                int sharedLanes = Mathf.Min(edgeA.Lanes.Count, edgeB.Lanes.Count);
                for (int i = 0; i < sharedLanes; i++)
                {
                    bool alreadyLinked = false;
                    foreach (var link in graph.GetLinksFrom(edgeA.Lanes[i]))
                    {
                        if (link.DestLane == edgeB.Lanes[i])
                        {
                            alreadyLinked = true; break;
                        }
                        if (link.ToConnector)
                        {
                            var onward = graph.GetLinksFrom(link.DestLane);
                            if (onward.Count > 0 && onward[0].DestLane == edgeB.Lanes[i])
                            {
                                alreadyLinked = true; break;
                            }
                        }
                    }

                    if (alreadyLinked) continue;

                    Vector3 srcEnd   = a.LaneEndPosition(i);
                    Vector3 dstStart = b.LaneStartPosition(i);

                    Lane connLane = graph.AddConnectorLane(
                        edgeA.Lanes[i], edgeB.Lanes[i],
                        srcEnd, dstStart,
                        destMergePosition: 0f,
                        metersPerUnit,
                        isSideEntry: false);

                    var connView = new LaneView
                    {
                        Lane       = connLane,
                        Waypoints  = new Vector3[] { srcEnd, dstStart },
                        LaneNumber = 0
                    };
                    connView.Build();
                    laneViews[connLane] = connView;
                }
            }
        }

        // ---- Pass 4: register TrafficLightGroups ----
        // Find all groups in the scene, wire their lights, and hand them to World.
        // This must run after the graph is built so TrafficNode.Light exists.
        // SimulationManager calls RegisterGroupsWithWorld() after Build().
        _pendingGroups = FindObjectsByType<TrafficLightGroup>(FindObjectsSortMode.None);
        foreach (var group in _pendingGroups)
        {
            foreach (var seg in group.AllSegments)
            {
                if (seg == null) continue;
                if (!endNodes.TryGetValue(seg, out var node)) continue;
                if (node.Light == null)
                {
                    // Group-managed segments get a light even if the checkbox was off
                    node.Light = new TrafficLight
                    {
                        CurrentState   = TrafficLight.State.Red,
                        GreenDuration  = 20f,
                        YellowDuration = 3f,
                        RedDuration    = 20f
                    };
                    prevLightStates[node] = TrafficLight.State.Red;
                }
                group.RegisterLight(seg, node.Light);
            }

            // Spawn the light view prefabs if the group has a prefab reference
            SpawnLightViews(group);

            group.Initialise();
        }

        Debug.Log($"[NavGraphBuilder] Built {graph.nodes.Count} nodes, " +
                  $"{RoadEdges.Count} road edges, {graph.links.Count} lane links, " +
                  $"{_pendingGroups.Length} light groups.");

        return graph;
    }

    TrafficLightGroup[] _pendingGroups = Array.Empty<TrafficLightGroup>();

    /// <summary>Called by SimulationManager after Build() to hook groups into World.</summary>
    public void RegisterGroupsWithWorld(World world)
    {
        foreach (var group in _pendingGroups)
            world.RegisterLightGroup(group);
    }

    // ---------------------------------------------------------------
    // Spawn TrafficLightView prefabs at each segment end position
    // ---------------------------------------------------------------
    void SpawnLightViews(TrafficLightGroup group)
    {
        if (group.LightViewPrefab == null) return;

        foreach (var seg in group.AllSegments)
        {
            if (seg == null) continue;
            if (!endNodes.TryGetValue(seg, out var node)) continue;
            if (node.Light == null) continue;

            // Place one prefab per lane end — use lane 0 position + offset per lane
            for (int i = 0; i < seg.LaneCount; i++)
            {
                Vector3 pos = seg.LaneEndPosition(i) + Vector3.up * 0.05f;
                var go = Instantiate(group.LightViewPrefab, pos, seg.transform.rotation);
                go.name = $"TrafficLight_{seg.name}_L{i}";

                var view = go.GetComponent<TrafficLightView>();
                if (view != null)
                    view.Bind(node.Light);
            }
        }
    }

    // ---------------------------------------------------------------
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
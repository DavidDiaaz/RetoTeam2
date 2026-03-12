using System.Collections.Generic;
using UnityEngine;

public class NavigationGraph
{
    public Dictionary<int, TrafficNode> nodes = new();
    public List<LaneLink>               links = new();

    // Index for fast lookup: given a lane, what links lead out of it?
    Dictionary<Lane, List<LaneLink>> _linksFrom = new();

    public void AddNode(TrafficNode node) => nodes[node.id] = node;

    public TrafficEdge AddEdge(
        int       fromId,
        int       toId,
        int       laneCount,
        float     length,
        RoadClass roadClass)
    {
        var from = nodes[fromId];
        var to   = nodes[toId];

        var edge = new TrafficEdge(from, to)
        {
            Length     = length,
            SpeedLimit = RoadClassInfo.SpeedLimit(roadClass),
            RoadClass  = roadClass
        };

        for (int i = 0; i < laneCount; i++)
            edge.Lanes.Add(new Lane { Edge = edge, LaneNumber = i });

        from.Outgoing.Add(edge);
        return edge;
    }

    /// <summary>
    /// Register a valid lane-to-lane continuation.
    /// mergePosition: normalised [0,1] position on destLane where cars join.
    /// </summary>
    public LaneLink AddLaneLink(Lane sourceLane, Lane destLane, float mergePosition = 0f)
    {
        var link = new LaneLink(sourceLane, destLane, mergePosition);
        links.Add(link);

        if (!_linksFrom.TryGetValue(sourceLane, out var list))
        {
            list = new List<LaneLink>();
            _linksFrom[sourceLane] = list;
        }
        list.Add(link);
        return link;
    }

    /// <summary>
    /// Create a physical connector lane bridging sourceLane's endpoint to
    /// destLane's start (or side-entry point), and register both LaneLinks.
    ///
    /// Returns the connector Lane so NavGraphBuilder can attach a LaneView.
    ///
    /// sourceLaneEndWorld  — world-space position at the end of sourceLane
    /// destLaneStartWorld  — world-space position at the start of destLane
    ///                       (the point where the connector exits onto destLane)
    /// destMergePosition   — normalised [0,1] on destLane (0 = straight entry)
    /// metersPerUnit       — world-to-logical scale used by the road network
    /// isSideEntry         — true when merging from a minor road onto a main road
    /// speedLimit          — override; defaults to a junction crawl speed
    /// </summary>
    public Lane AddConnectorLane(
        Lane    sourceLane,
        Lane    destLane,
        Vector3 sourceLaneEndWorld,
        Vector3 destLaneStartWorld,
        float   destMergePosition,
        float   metersPerUnit,
        bool    isSideEntry   = false,
        int     speedLimit    = 0)
    {
        // Logical length of the connector
        float worldDist  = Vector3.Distance(sourceLaneEndWorld, destLaneStartWorld);
        float logicalLen = Mathf.Max(worldDist * metersPerUnit, 0.5f); // floor: 0.5 m

        // Reuse the REAL road nodes as connector endpoints so that Dijkstra
        // (which operates on the node graph) can traverse across segments.
        // sourceLane.Edge.to  = end of source segment
        // destLane.Edge.from  = start of destination segment
        var nodeA = sourceLane.Edge.to;
        var nodeB = destLane.Edge.from;

        // Connector edge — single lane, connector flag set
        int connSpeed = speedLimit > 0
            ? speedLimit
            : Mathf.Min(
                RoadClassInfo.SpeedLimit(sourceLane.Edge.RoadClass),
                RoadClassInfo.SpeedLimit(destLane.Edge.RoadClass));

        var connEdge = new TrafficEdge(nodeA, nodeB)
        {
            Length      = logicalLen,
            SpeedLimit  = connSpeed,
            RoadClass   = RoadClass.Connector,
            IsConnector = true
        };
        var connLane = new Lane { Edge = connEdge, LaneNumber = 0 };
        connEdge.Lanes.Add(connLane);
        nodeA.Outgoing.Add(connEdge); // KEY: Dijkstra can now cross to the next segment

        // Link A: sourceLane → connectorLane (always enters at start)
        AddLaneLink(sourceLane, connLane, mergePosition: 0f);

        // Link B: connectorLane → destLane (may enter mid-segment for side entries)
        AddLaneLink(connLane, destLane, mergePosition: destMergePosition);

        // Register this connector on the dest lane so main-road cars can
        // detect sibling connector conflicts at the same merge point.
        if (!destLane.IncomingConnectors.Contains(connLane))
            destLane.IncomingConnectors.Add(connLane);

        return connLane;
    }

    /// <summary>All valid outgoing lane links from this lane.</summary>
    public List<LaneLink> GetLinksFrom(Lane lane)
    {
        _linksFrom.TryGetValue(lane, out var list);
        return list ?? _empty;
    }

    /// <summary>
    /// Resolve the real destination lane one hop past a connector.
    /// Returns null if sourceLane doesn't lead to a connector or the
    /// connector has no onward link.
    /// </summary>
    public Lane GetConnectorDest(Lane sourceLane)
    {
        var linksOut = GetLinksFrom(sourceLane);
        if (linksOut.Count == 0 || !linksOut[0].ToConnector) return null;

        var connLane  = linksOut[0].DestLane;
        var onward    = GetLinksFrom(connLane);
        return onward.Count > 0 ? onward[0].DestLane : null;
    }

    /// <summary>
    /// Resolve the LaneLink that exits a connector onto the real road.
    /// Useful for yield / gap-check logic.
    /// </summary>
    public LaneLink GetConnectorExitLink(Lane sourceLane)
    {
        var linksOut = GetLinksFrom(sourceLane);
        if (linksOut.Count == 0 || !linksOut[0].ToConnector) return null;

        var connLane = linksOut[0].DestLane;
        var onward   = GetLinksFrom(connLane);
        return onward.Count > 0 ? onward[0] : null;
    }

    public TrafficNode GetNode(int id) => nodes[id];

    static readonly List<LaneLink> _empty = new();
}
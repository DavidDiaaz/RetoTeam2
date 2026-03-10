using System.Collections.Generic;

public class NavigationGraph
{
    public Dictionary<int, TrafficNode> nodes = new();

    public void AddNode(TrafficNode node)
    {
        nodes[node.id] = node;
    }

    public TrafficEdge AddEdge(
        int       fromId,
        int       toId,
        int       laneCount,
        float     length,
        RoadClass roadClass,
        int       entryLaneRequired = -1)
    {
        TrafficNode from = nodes[fromId];
        TrafficNode to   = nodes[toId];

        var edge = new TrafficEdge(from, to)
        {
            Length              = length,
            SpeedLimit          = RoadClassInfo.SpeedLimit(roadClass),
            RoadClass           = roadClass,
            EntryLaneRequired   = entryLaneRequired
        };

        for (int i = 0; i < laneCount; i++)
        {
            edge.Lanes.Add(new Lane
            {
                Edge       = edge,
                LaneNumber = i
            });
        }

        from.Outgoing.Add(edge);
        return edge;
    }

    public TrafficNode GetNode(int id) => nodes[id];
}
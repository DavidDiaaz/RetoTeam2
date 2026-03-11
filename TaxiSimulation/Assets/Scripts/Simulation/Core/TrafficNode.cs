using System.Collections.Generic;

/// <summary>
/// Junction point between road segments.
/// In the new model, nodes are purely structural — they mark where
/// segments meet. Contention is handled by lane physics, not node logic.
/// </summary>
public class TrafficNode
{
    public int    id;
    public string Label    = "";
    public List<TrafficEdge> Outgoing = new();
    public TrafficLight      Light;

    public TrafficNode(int id) { this.id = id; }
}
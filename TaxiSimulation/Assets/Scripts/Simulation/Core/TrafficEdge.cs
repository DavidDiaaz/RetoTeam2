using System.Collections.Generic;

public class TrafficEdge
{
    public TrafficNode from;
    public TrafficNode to;

    public float     Length;
    public int       SpeedLimit;
    public RoadClass RoadClass;

    /// <summary>
    /// Which lane number a vehicle must be in to enter this edge.
    /// -1 means any lane is acceptable.
    /// </summary>
    public int EntryLaneRequired = -1;

    /// <summary>
    /// True for edges created by RoadConnection — the arc between two road segments.
    /// SimulationManager will not despawn vehicles on connection edges even if the
    /// end node has no outgoing edges (it does — to the target road).
    /// </summary>
    public bool IsConnection = false;

    public List<Lane> Lanes = new();

    public TrafficEdge(TrafficNode from, TrafficNode to)
    {
        this.from = from;
        this.to   = to;
    }

    public Lane GetLeftLane(int laneNumber)
    {
        int target = laneNumber - 1;
        return target >= 0 ? Lanes[target] : null;
    }

    public Lane GetRightLane(int laneNumber)
    {
        int target = laneNumber + 1;
        return target < Lanes.Count ? Lanes[target] : null;
    }
}
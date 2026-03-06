using System.Collections.Generic;

public class TrafficEdge
{
    public TrafficNode from;
    public TrafficNode to;

    public float      Length;
    public int        SpeedLimit;
    public RoadClass  RoadClass;

    public List<Lane> Lanes = new();

    public TrafficEdge(TrafficNode from, TrafficNode to)
    {
        this.from = from;
        this.to   = to;
    }

    // Adjacent lane to the left of laneNumber, null if none
    public Lane GetLeftLane(int laneNumber)
    {
        int target = laneNumber - 1;
        return target >= 0 ? Lanes[target] : null;
    }

    // Adjacent lane to the right of laneNumber, null if none
    public Lane GetRightLane(int laneNumber)
    {
        int target = laneNumber + 1;
        return target < Lanes.Count ? Lanes[target] : null;
    }
}
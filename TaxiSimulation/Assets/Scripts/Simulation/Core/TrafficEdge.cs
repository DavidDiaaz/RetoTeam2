using System.Collections.Generic;

/// <summary>
/// A directed road segment between two nodes.
/// Contains N lanes, each of which cars travel along.
///
/// IsConnector = true marks synthetic junction-bridging edges created by
/// NavGraphBuilder. Connector edges are typically very short (1–10 m) and
/// single-lane. VehicleAgent skips stop-line braking on connector edges so
/// that cars shorter or longer than the connector still flow through cleanly.
/// </summary>
public class TrafficEdge
{
    public TrafficNode from;
    public TrafficNode to;

    public float     Length;
    public int       SpeedLimit;
    public RoadClass RoadClass;

    /// <summary>
    /// True for synthetic connector edges that bridge two road segments.
    /// Connector lanes are never despawn points and never trigger stop-line
    /// braking — cars flow through them purely on following-distance logic.
    /// </summary>
    public bool IsConnector { get; set; }

    public List<Lane> Lanes = new();

    public TrafficEdge(TrafficNode from, TrafficNode to)
    {
        this.from = from;
        this.to   = to;
    }

    // ---------------------------------------------------------------
    // Lane neighbours — for lateral perception queries
    // ---------------------------------------------------------------

    /// <summary>Lane to the left (lower index). Null at leftmost.</summary>
    public Lane GetLeftLane(int laneNumber)
    {
        int t = laneNumber - 1;
        return (t >= 0 && t < Lanes.Count) ? Lanes[t] : null;
    }

    /// <summary>Lane to the right (higher index). Null at rightmost.</summary>
    public Lane GetRightLane(int laneNumber)
    {
        int t = laneNumber + 1;
        return (t >= 0 && t < Lanes.Count) ? Lanes[t] : null;
    }
}
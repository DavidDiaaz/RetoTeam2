/// <summary>
/// Static traffic law rules.
/// No spatial math — road class hierarchy and lane number encode priority.
/// </summary>
public static class TrafficLaw
{
    // ---------------------------------------------------------------
    // Right of way — used at merge points
    // ---------------------------------------------------------------

    /// <summary>
    /// Does 'me' have legal right of way over 'other'?
    /// Road class hierarchy first, then lower lane number wins (left over right).
    /// </summary>
    public static bool HasRightOfWay(VehicleAgent me, VehicleAgent other)
    {
        int myWeight    = RoadClassInfo.RightOfWayWeight(me.CurrentLane.Edge.RoadClass);
        int theirWeight = RoadClassInfo.RightOfWayWeight(other.CurrentLane.Edge.RoadClass);

        if (myWeight != theirWeight)
            return myWeight > theirWeight;

        return me.LaneNumber < other.LaneNumber;
    }

    // ---------------------------------------------------------------
    // Roundabout
    // ---------------------------------------------------------------

    /// <summary>Must yield before entering a roundabout if not already inside.</summary>
    public static bool MustYieldToRoundabout(VehicleAgent me)
    {
        if (me.CurrentLane.Edge.RoadClass == RoadClass.Roundabout)
            return false;
        return me.TargetEdge?.RoadClass == RoadClass.Roundabout;
    }

    // ---------------------------------------------------------------
    // Speed
    // ---------------------------------------------------------------

    public static float SpeedLimitMs(VehicleAgent me)
    => me.CurrentLane.Edge.SpeedLimit * (1000f / 3600f) / 3.5f;
    
    /// <summary>
    /// Roundabout occupancy — returns false until roundabout merge logic is implemented
    /// via LaneLink presence model.
    /// </summary>
    public static bool IsRoundaboutOccupied(VehicleAgent me) => false;

    public static bool IsSpeeding(VehicleAgent me)
        => me.Speed > SpeedLimitMs(me) * 1.05f;
}
using System.Collections.Generic;

/// <summary>
/// Static traffic law rules. Returns what the law says should happen.
/// Agents may choose to follow or ignore based on Profile.LawAbidance.
/// No spatial math — direction is encoded by lane number convention:
///   0 = leftmost, highest = rightmost.
/// </summary>
public static class TrafficLaw
{
    // ---------------------------------------------------------------
    // Right of way
    // ---------------------------------------------------------------

    /// <summary>
    /// Does 'me' have legal right of way over 'other'?
    /// Road class hierarchy first, then right-hand rule via lane number.
    /// Lower lane number = left = has priority over right.
    /// </summary>
    public static bool HasRightOfWay(VehicleAgent me, VehicleAgent other)
    {
        int myWeight    = RoadClassInfo.RightOfWayWeight(me.CurrentLane.Edge.RoadClass);
        int theirWeight = RoadClassInfo.RightOfWayWeight(other.CurrentLane.Edge.RoadClass);

        if (myWeight != theirWeight)
            return myWeight > theirWeight;

        // Same road class — lower lane number has priority (left over right)
        return me.LaneNumber < other.LaneNumber;
    }

    /// <summary>
    /// Returns -1 to +1 score based on legal right of way over all contenders.
    /// </summary>
    public static float RightOfWayScore(VehicleAgent me, List<VehicleAgent> contenders)
    {
        if (contenders == null || contenders.Count == 0) return 0f;

        int wins = 0, losses = 0;

        foreach (var other in contenders)
        {
            if (other == me) continue;
            if (HasRightOfWay(me, other)) wins++;
            else losses++;
        }

        int total = wins + losses;
        return total == 0 ? 0f : (wins - losses) / (float)total;
    }

    // ---------------------------------------------------------------
    // Roundabout
    // ---------------------------------------------------------------

    public static bool MustYieldToRoundabout(VehicleAgent me)
    {
        if (me.CurrentLane.Edge.RoadClass == RoadClass.Roundabout)
            return false;
        return me.TargetEdge?.RoadClass == RoadClass.Roundabout;
    }

    public static bool IsRoundaboutOccupied(VehicleAgent me)
    {
        if (me.TargetNode == null) return false;

        foreach (var contender in me.TargetNode.Contenders)
        {
            if (contender == me) continue;
            if (contender.CurrentLane.Edge.RoadClass == RoadClass.Roundabout)
                return true;
        }

        var occupant = me.TargetNode.OccupiedBy;
        return occupant != null && occupant != me &&
               occupant.CurrentLane.Edge.RoadClass == RoadClass.Roundabout;
    }

    // ---------------------------------------------------------------
    // Speed
    // ---------------------------------------------------------------

    public static float SpeedLimitMs(VehicleAgent me)
        => me.CurrentLane.Edge.SpeedLimit * (1000f / 3600f);

    public static bool IsSpeeding(VehicleAgent me)
        => me.Speed > SpeedLimitMs(me) * 1.05f;
}
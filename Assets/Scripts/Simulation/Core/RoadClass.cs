public enum RoadClass
{
    Local      = 0,
    Collector  = 1,
    Arterial   = 2,
    Highway    = 3,
    Roundabout = 4,
    Connector  = 5   // synthetic: junction-bridging lanes created by NavGraphBuilder
}

public static class RoadClassInfo
{
    public static int SpeedLimit(RoadClass rc) => rc switch
    {
        RoadClass.Local      => 30,
        RoadClass.Collector  => 50,
        RoadClass.Arterial   => 70,
        RoadClass.Highway    => 100,
        RoadClass.Roundabout => 20,
        RoadClass.Connector  => 20,  // slow through junctions
        _                    => 30
    };

    public static int DefaultLaneCount(RoadClass rc) => rc switch
    {
        RoadClass.Local      => 1,
        RoadClass.Collector  => 2,
        RoadClass.Arterial   => 3,
        RoadClass.Highway    => 4,
        RoadClass.Roundabout => 1,
        RoadClass.Connector  => 1,   // always single-lane
        _                    => 1
    };

    public static int RightOfWayWeight(RoadClass rc) => rc switch
    {
        RoadClass.Local      => 0,
        RoadClass.Collector  => 1,
        RoadClass.Arterial   => 2,
        RoadClass.Highway    => 3,
        RoadClass.Roundabout => 4,
        RoadClass.Connector  => 0,   // inherits from source road via JunctionEntryCap yield logic
        _                    => 0
    };
}
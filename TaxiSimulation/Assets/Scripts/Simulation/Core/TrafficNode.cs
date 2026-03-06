using System.Collections.Generic;

public class TrafficNode
{
    public int               id;
    public List<TrafficEdge> Outgoing   = new();
    public TrafficLight      Light;

    // ---------------------------------------------------------------
    // Intersection state
    // ---------------------------------------------------------------

    // Vehicle physically inside the intersection right now
    public VehicleAgent OccupiedBy = null;

    // Vehicles stopped at their edge end wanting to enter this node
    // Populated during Perceive, cleared each tick before Perceive
    public List<VehicleAgent> Contenders = new();

    public TrafficNode(int id)
    {
        this.id = id;
    }

    public bool IsBlocked => OccupiedBy != null;

    public void ClearContenders() => Contenders.Clear();

    public void RegisterContender(VehicleAgent v)
    {
        if (!Contenders.Contains(v))
            Contenders.Add(v);
    }
}
using System.Collections.Generic;

public class TrafficNode
{
    public int    id;
    public string Label = ""; // set by NavGraphBuilder, e.g. "Road_seg0 END"

    public List<TrafficEdge> Outgoing  = new();
    public TrafficLight      Light;

    public VehicleAgent       OccupiedBy = null;
    public List<VehicleAgent> Contenders = new();

    public TrafficNode(int id) { this.id = id; }

    public bool IsBlocked => OccupiedBy != null;

    public void ClearContenders() => Contenders.Clear();

    public void RegisterContender(VehicleAgent v)
    {
        if (!Contenders.Contains(v))
            Contenders.Add(v);
    }
}
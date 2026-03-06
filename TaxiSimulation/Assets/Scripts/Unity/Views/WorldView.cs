using UnityEngine;
using System.Collections.Generic;

public class WorldView : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject vehiclePrefab;

    Dictionary<Lane, LaneView> laneViews = new();

    public void SetLaneViews(Dictionary<Lane, LaneView> views)
    {
        laneViews = views;
    }

    public LaneView GetLaneView(Lane lane)
    {
        laneViews.TryGetValue(lane, out var view);
        return view;
    }

    public void SpawnVehicles(List<Agent> agents)
    {
        foreach (var agent in agents)
        {
            if (agent is VehicleAgent vehicle)
            {
                GameObject  go   = Instantiate(vehiclePrefab, transform);
                VehicleView view = go.AddComponent<VehicleView>();
                view.Agent     = vehicle;
                view.WorldView = this;
            }
        }
    }
}
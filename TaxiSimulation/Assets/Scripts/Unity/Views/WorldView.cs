using UnityEngine;
using System.Collections.Generic;

public class WorldView : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject vehiclePrefab;

    Dictionary<Lane, LaneView>           laneViews    = new();
    Dictionary<VehicleAgent, GameObject> vehicleGOs   = new();

    public void SetLaneViews(Dictionary<Lane, LaneView> views)
    {
        laneViews = views;
    }

    public LaneView GetLaneView(Lane lane)
    {
        laneViews.TryGetValue(lane, out var view);
        return view;
    }

    // Spawn a single vehicle and register it
    public void SpawnVehicle(VehicleAgent vehicle)
    {
        if (vehicleGOs.ContainsKey(vehicle)) return;

        GameObject  go   = Instantiate(vehiclePrefab, transform);
        VehicleView view = go.AddComponent<VehicleView>();
        view.Agent     = vehicle;
        view.WorldView = this;

        vehicleGOs[vehicle] = go;
    }

    // Destroy a vehicle's GameObject and unregister it
    public void DestroyVehicle(VehicleAgent vehicle)
    {
        if (!vehicleGOs.TryGetValue(vehicle, out var go)) return;
        vehicleGOs.Remove(vehicle);
        Destroy(go);
    }

    // Legacy batch spawn — kept for compatibility
    public void SpawnVehicles(List<Agent> agents)
    {
        foreach (var agent in agents)
            if (agent is VehicleAgent v)
                SpawnVehicle(v);
    }
}
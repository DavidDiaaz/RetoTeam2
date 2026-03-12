using UnityEngine;
using System.Collections.Generic;

public class WorldView : MonoBehaviour
{
    [Header("Vehicle prefabs")]
    public GameObject ambientCarPrefab;
    public GameObject taxiPrefab;

    [Header("Pedestrian prefab")]
    public GameObject pedestrianPrefab;

    Dictionary<Lane, LaneView>           _laneViews     = new();
    Dictionary<VehicleAgent, GameObject> _vehicleGOs    = new();
    Dictionary<Pedestrian,   GameObject> _pedestrianGOs = new();

    public void SetLaneViews(Dictionary<Lane, LaneView> views) => _laneViews = views;

    public LaneView GetLaneView(Lane lane)
    {
        _laneViews.TryGetValue(lane, out var view);
        return view;
    }

    // ---------------------------------------------------------------
    // Vehicles
    // ---------------------------------------------------------------

    /// <summary>Spawn a vehicle and return its GO (used by SimulationManager
    /// to register it with CameraFollowController).</summary>
    public GameObject SpawnVehicleAndReturn(VehicleAgent vehicle)
    {
        if (_vehicleGOs.TryGetValue(vehicle, out var existing)) return existing;

        var prefab = vehicle is AutonomousTaxi ? taxiPrefab : ambientCarPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"[WorldView] No prefab assigned for {vehicle.GetType().Name}");
            return null;
        }

        var go        = Instantiate(prefab, transform);
        var view      = go.AddComponent<VehicleView>();
        view.Agent    = vehicle;
        view.WorldView = this;

        _vehicleGOs[vehicle] = go;
        return go;
    }

    /// <summary>Convenience wrapper — same as SpawnVehicleAndReturn but discards the return value.</summary>
    public void SpawnVehicle(VehicleAgent vehicle) => SpawnVehicleAndReturn(vehicle);

    public void DestroyVehicle(VehicleAgent vehicle)
    {
        if (!_vehicleGOs.TryGetValue(vehicle, out var go)) return;
        _vehicleGOs.Remove(vehicle);
        Destroy(go);
    }

    // ---------------------------------------------------------------
    // Pedestrians
    // ---------------------------------------------------------------

    public GameObject SpawnPedestrianAndReturn(Pedestrian p)
    {
        if (_pedestrianGOs.TryGetValue(p, out var existing)) return existing;
        if (pedestrianPrefab == null)
        {
            Debug.LogWarning("[WorldView] pedestrianPrefab not assigned.");
            return null;
        }

        var go   = Instantiate(pedestrianPrefab, p.WorldPosition, Quaternion.identity, transform);
        var view = go.AddComponent<PedestrianView>();
        view.Bind(p);

        _pedestrianGOs[p] = go;
        return go;
    }

    public void SpawnPedestrian(Pedestrian p) => SpawnPedestrianAndReturn(p);

    public void DestroyPedestrian(Pedestrian p)
    {
        if (!_pedestrianGOs.TryGetValue(p, out var go)) return;
        _pedestrianGOs.Remove(p);
        Destroy(go);
    }
}
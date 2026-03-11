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

    public void SpawnVehicle(VehicleAgent vehicle)
    {
        if (_vehicleGOs.ContainsKey(vehicle)) return;

        var prefab = vehicle is AutonomousTaxi ? taxiPrefab : ambientCarPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"[WorldView] No prefab assigned for {vehicle.GetType().Name}");
            return;
        }

        var go        = Instantiate(prefab, transform);
        var view      = go.AddComponent<VehicleView>();
        view.Agent    = vehicle;
        view.WorldView = this;

        _vehicleGOs[vehicle] = go;
    }

    public void DestroyVehicle(VehicleAgent vehicle)
    {
        if (!_vehicleGOs.TryGetValue(vehicle, out var go)) return;
        _vehicleGOs.Remove(vehicle);
        Destroy(go);
    }

    // ---------------------------------------------------------------
    // Pedestrians
    // ---------------------------------------------------------------

    public void SpawnPedestrian(Pedestrian p)
    {
        if (_pedestrianGOs.ContainsKey(p)) return;
        if (pedestrianPrefab == null)
        {
            Debug.LogWarning("[WorldView] pedestrianPrefab not assigned.");
            return;
        }

        var go   = Instantiate(pedestrianPrefab, p.WorldPosition, Quaternion.identity, transform);
        var view = go.AddComponent<PedestrianView>();
        view.Bind(p);

        _pedestrianGOs[p] = go;
    }

    public void DestroyPedestrian(Pedestrian p)
    {
        if (!_pedestrianGOs.TryGetValue(p, out var go)) return;
        _pedestrianGOs.Remove(p);
        Destroy(go);
    }
}
using UnityEngine;
using System.Collections.Generic;

public class SimulationManager : MonoBehaviour
{
    // ---------------------------------------------------------------
    // Inspector
    // ---------------------------------------------------------------

    [Header("References")]
    public WorldView              worldView;
    public NavGraphBuilder        builder;
    public CameraFollowController followCam;   // optional — assign to enable follow cam

    [Header("Ambient traffic")]
    [Tooltip("Ambient cars placed per Road, spread across its lanes")]
    public int carsPerRoad = 4;

    [Header("Taxis")]
    public int taxiCount = 3;

    [Header("Pedestrians")]
    public int   initialPedestrians  = 5;
    public int   maxPedestrians      = 10;
    [Tooltip("Seconds between automatic new pedestrian spawns")]
    public float pedestrianSpawnRate = 15f;
    [Tooltip("Seconds a pedestrian waits before cancelling")]
    public float pedestrianTolerance = 120f;

    // ---------------------------------------------------------------
    // Runtime
    // ---------------------------------------------------------------

    World world;
    public World World => world;

    List<Lane>        _spawnableLanes = new();
    List<TrafficNode> _roadNodes      = new();

    Dictionary<TrafficEdge, RoadSegment> _edgeToSegment = new();

    float _pedestrianSpawnTimer = 0f;
    int   _activePedestrians    = 0;
    readonly HashSet<Pedestrian> _trackedPedestrians = new();

    // Maps VehicleAgent → instantiated GO (so we can tell the follow cam)
    readonly Dictionary<VehicleAgent, GameObject> _vehicleGOs = new();

    // ---------------------------------------------------------------
    void Start()
    {
        var graph = builder.Build(out var laneViews);
        world = new World(graph);

        builder.RegisterGroupsWithWorld(world);
        worldView.SetLaneViews(laneViews);

        foreach (var (seg, edge) in builder.RoadEdges)
        {
            _edgeToSegment[edge] = seg;
            if (edge.IsConnector) continue;

            // Skip dead-end segments — their lanes lead nowhere and cause cascade jams.
            // Dead-end = the edge's end node has no outgoing connections (terminal node).
            if (IsDeadEnd(edge)) continue;

            foreach (var lane in edge.Lanes)
                _spawnableLanes.Add(lane);

            // Only use edge.to nodes that have outgoing connections for pedestrian placement.
            // Terminal nodes cause taxis to go there and get stuck.
            if (edge.to != null && edge.to.Outgoing.Count > 0 && !_roadNodes.Contains(edge.to))
                _roadNodes.Add(edge.to);
        }

        SpawnAmbientTraffic();
        SpawnTaxis();

        for (int i = 0; i < initialPedestrians; i++)
            TrySpawnPedestrian();

        Debug.Log($"[Simulation] Ready — {world.Agents.Count} agents, " +
                  $"{_spawnableLanes.Count} lanes, {_roadNodes.Count} nodes.");
    }

    // ---------------------------------------------------------------
    void Update()
    {
        world.Tick(Time.deltaTime);
        builder.PollLightEvents(world.Navigation);

        _pedestrianSpawnTimer += Time.deltaTime;
        if (_pedestrianSpawnTimer >= pedestrianSpawnRate)
        {
            _pedestrianSpawnTimer = 0f;
            if (_activePedestrians < maxPedestrians)
                TrySpawnPedestrian();
        }

        CleanUpPedestrians();
    }

    // ---------------------------------------------------------------
    // Ambient traffic
    // ---------------------------------------------------------------
    void SpawnAmbientTraffic()
    {
        var byRoad = new Dictionary<Road, List<Lane>>();
        foreach (var lane in _spawnableLanes)
        {
            var seg  = FindSegmentForEdge(lane.Edge);
            var road = seg != null ? seg.GetComponentInParent<Road>() : null;
            if (road == null) continue;

            if (!byRoad.TryGetValue(road, out var list))
                byRoad[road] = list = new List<Lane>();
            list.Add(lane);
        }

        foreach (var (_, lanes) in byRoad)
        {
            int count = Mathf.Min(carsPerRoad, lanes.Count);
            for (int i = 0; i < count; i++)
            {
                float t    = count == 1 ? 0.5f : i / (float)(count - 1);
                int   idx  = Mathf.RoundToInt(t * (lanes.Count - 1));
                Lane  lane = lanes[idx];

                float pos = Mathf.Clamp(
                    lane.Edge.Length * (0.1f + 0.8f * i / Mathf.Max(count - 1, 1)),
                    0f, lane.Edge.Length - 5f);

                SpawnAmbientCar(lane, pos);
            }
        }
    }

    void SpawnAmbientCar(Lane lane, float position)
    {
        if (lane.Edge.IsConnector) return;
        if (!lane.IsSegmentFree(position, 4.5f)) return;

        var car = new AmbientDriver
        {
            CurrentLane = lane,
            LaneNumber  = lane.LaneNumber,
            Position    = position,
            Speed       = 0f
        };

        lane.InsertSorted(car);
        world.Agents.Add(car);

        var go = worldView.SpawnVehicleAndReturn(car);
        if (go != null)
        {
            _vehicleGOs[car] = go;
            followCam?.RegisterVehicle(car, go);
        }
    }

    // ---------------------------------------------------------------
    // Taxis
    // ---------------------------------------------------------------
    void SpawnTaxis()
    {
        if (_spawnableLanes.Count == 0) return;

        var shuffled = new List<Lane>(_spawnableLanes);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        int spawned = 0;
        foreach (var lane in shuffled)
        {
            if (spawned >= taxiCount) break;
            if (!lane.IsSegmentFree(0f, 4.5f)) continue;

            var taxi = new AutonomousTaxi
            {
                CurrentLane = lane,
                LaneNumber  = lane.LaneNumber,
                Position    = 0f,
                Speed       = 0f
            };

            lane.InsertSorted(taxi);
            world.AddTaxi(taxi);

            var go = worldView.SpawnVehicleAndReturn(taxi);
            if (go != null)
            {
                _vehicleGOs[taxi] = go;
                followCam?.RegisterVehicle(taxi, go);
            }
            spawned++;
        }

        Debug.Log($"[Simulation] Spawned {spawned}/{taxiCount} taxis.");
    }

    // ---------------------------------------------------------------
    // Pedestrians
    // ---------------------------------------------------------------
    void TrySpawnPedestrian()
    {
        if (_roadNodes.Count < 2) return;

        int originIdx = Random.Range(0, _roadNodes.Count);
        int destIdx;
        do { destIdx = Random.Range(0, _roadNodes.Count); }
        while (destIdx == originIdx);

        TrafficNode origin = _roadNodes[originIdx];
        TrafficNode dest   = _roadNodes[destIdx];

        Vector3 worldPos = GetNodeWorldPosition(origin)
            + new Vector3(Random.Range(-0.5f, 0.5f), 0.1f, Random.Range(-0.5f, 0.5f));

        var p = new Pedestrian(origin, dest, pedestrianTolerance, worldPos);
        world.AddPedestrian(p);
        var pedGO = worldView.SpawnPedestrianAndReturn(p);
        if (pedGO != null) followCam?.RegisterPedestrian(p, pedGO);
        _trackedPedestrians.Add(p);
        _activePedestrians++;
    }

    void CleanUpPedestrians()
    {
        var toRemove = new List<Pedestrian>();
        foreach (var p in _trackedPedestrians)
        {
            if (p.State == PedestrianState.Done ||
                p.State == PedestrianState.Cancelled)
                toRemove.Add(p);
        }

        foreach (var p in toRemove)
        {
            _trackedPedestrians.Remove(p);
            followCam?.UnregisterPedestrian(p);
            worldView.DestroyPedestrian(p);
            _activePedestrians = Mathf.Max(0, _activePedestrians - 1);
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>True if the edge ends at a terminal node (no outgoing edges).</summary>
    static bool IsDeadEnd(TrafficEdge edge) =>
        edge != null && !edge.IsConnector && edge.to != null && edge.to.Outgoing.Count == 0;

    RoadSegment FindSegmentForEdge(TrafficEdge edge)
    {
        _edgeToSegment.TryGetValue(edge, out var seg);
        return seg;
    }

    Vector3 GetNodeWorldPosition(TrafficNode node)
    {
        foreach (var (seg, edge) in builder.RoadEdges)
        {
            if (edge.IsConnector) continue;
            if (edge.to   == node) return seg.LaneEndPosition(0);
            if (edge.from == node) return seg.LaneStartPosition(0);
        }
        return Vector3.zero;
    }
}
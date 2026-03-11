using UnityEngine;
using System.Collections.Generic;

public class SimulationManager : MonoBehaviour
{
    // ---------------------------------------------------------------
    // Inspector
    // ---------------------------------------------------------------

    [Header("References")]
    public WorldView       worldView;
    public NavGraphBuilder builder;

    [Header("Ambient traffic")]
    [Tooltip("How many ambient cars to place per Road (spread across its segments/lanes)")]
    public int carsPerRoad = 4;

    [Header("Taxis")]
    public int taxiCount = 3;

    [Header("Pedestrians")]
    public int   initialPedestrians  = 5;
    public int   maxPedestrians      = 10;
    [Tooltip("Seconds between automatic new pedestrian spawns")]
    public float pedestrianSpawnRate = 15f;
    [Tooltip("Seconds a pedestrian will wait before cancelling")]
    public float pedestrianTolerance = 120f;

    // ---------------------------------------------------------------
    // Runtime
    // ---------------------------------------------------------------

    World world;

    List<Lane>        _spawnableLanes = new();
    List<TrafficNode> _roadNodes      = new();

    // Segment → edge reverse lookup, built once on Start
    Dictionary<TrafficEdge, RoadSegment> _edgeToSegment = new();

    float _pedestrianSpawnTimer = 0f;
    int   _activePedestrians    = 0;

    // Pedestrians we own so we can clean up their views
    readonly HashSet<Pedestrian> _trackedPedestrians = new();

    // ---------------------------------------------------------------
    void Start()
    {
        var graph = builder.Build(out var laneViews);
        world = new World(graph);

        builder.RegisterGroupsWithWorld(world);
        worldView.SetLaneViews(laneViews);

        // Build helper lookups
        foreach (var (seg, edge) in builder.RoadEdges)
        {
            _edgeToSegment[edge] = seg;

            if (edge.IsConnector) continue;

            foreach (var lane in edge.Lanes)
                _spawnableLanes.Add(lane);

            if (edge.from != null && !_roadNodes.Contains(edge.from))
                _roadNodes.Add(edge.from);
            if (edge.to   != null && !_roadNodes.Contains(edge.to))
                _roadNodes.Add(edge.to);
        }

        SpawnAmbientTraffic();
        SpawnTaxis();

        for (int i = 0; i < initialPedestrians; i++)
            TrySpawnPedestrian();

        Debug.Log($"[Simulation] Ready — {world.Agents.Count} agents, " +
                  $"{_spawnableLanes.Count} spawnable lanes, " +
                  $"{_roadNodes.Count} road nodes.");
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
    // Ambient traffic — carsPerRoad, evenly spread across all lanes in that road
    // ---------------------------------------------------------------
    void SpawnAmbientTraffic()
    {
        // Group lanes by their owning Road
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
                // Spread evenly by index across available lanes
                float t    = count == 1 ? 0.5f : i / (float)(count - 1);
                int   idx  = Mathf.RoundToInt(t * (lanes.Count - 1));
                Lane  lane = lanes[idx];

                // Space cars along the lane so they don't all pile at t=0
                float pos = Mathf.Clamp(
                    lane.Edge.Length * (0.1f + 0.8f * i / Mathf.Max(count - 1, 1)),
                    0f,
                    lane.Edge.Length - 5f);

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
        worldView.SpawnVehicle(car);
    }

    // ---------------------------------------------------------------
    // Taxis — placed on random lanes at startup
    // ---------------------------------------------------------------
    void SpawnTaxis()
    {
        if (_spawnableLanes.Count == 0) return;

        // Shuffle a copy so we don't bias toward the same lanes every run
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
            worldView.SpawnVehicle(taxi);
            spawned++;
        }

        Debug.Log($"[Simulation] Spawned {spawned}/{taxiCount} taxis.");
    }

    // ---------------------------------------------------------------
    // Pedestrians — random origin/destination from real road nodes
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

        // Stand just off the road edge at the origin node
        Vector3 worldPos   = GetNodeWorldPosition(origin);
        worldPos           += new Vector3(Random.Range(-0.5f, 0.5f), 0.1f, Random.Range(-0.5f, 0.5f));

        var p = new Pedestrian(origin, dest, pedestrianTolerance, worldPos);

        world.AddPedestrian(p);
        worldView.SpawnPedestrian(p);

        _trackedPedestrians.Add(p);
        _activePedestrians++;
    }

    // ---------------------------------------------------------------
    // Remove views for Done / Cancelled pedestrians
    // (World.Tick already prunes them from world.Agents)
    // ---------------------------------------------------------------
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
            worldView.DestroyPedestrian(p);
            _activePedestrians = Mathf.Max(0, _activePedestrians - 1);
        }
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    RoadSegment FindSegmentForEdge(TrafficEdge edge)
    {
        _edgeToSegment.TryGetValue(edge, out var seg);
        return seg;
    }

    Vector3 GetNodeWorldPosition(TrafficNode node)
    {
        // Find a real-road lane whose edge starts or ends at this node
        foreach (var (seg, edge) in builder.RoadEdges)
        {
            if (edge.IsConnector) continue;
            if (edge.to == node)   return seg.LaneEndPosition(0);
            if (edge.from == node) return seg.LaneStartPosition(0);
        }
        return Vector3.zero;
    }
}
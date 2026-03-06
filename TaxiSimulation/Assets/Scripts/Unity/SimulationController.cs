using UnityEngine;
using System.Collections.Generic;

public class SimulationManager : MonoBehaviour
{
    [Header("Params")]
    public float trafficDensity = 0.5f;

    [Header("References")]
    public WorldView       worldView;
    public NavGraphBuilder builder;

    World world;

    void Start()
    {
        var graph = builder.Build(out var laneViews);
        world     = new World(graph);

        worldView.SetLaneViews(laneViews);
        PopulateTraffic(world);
        worldView.SpawnVehicles(world.Agents);

        Debug.Log($"[Simulation] Ready — {world.Agents.Count} agents, {graph.nodes.Count} nodes");
    }

    void Update()
    {
        world.Tick(Time.deltaTime);
        builder.PollLightEvents(world.Navigation);
    }

    void PopulateTraffic(World world)
    {
        foreach (var node in world.Navigation.nodes.Values)
            foreach (var edge in node.Outgoing)
                for (int i = 0; i < edge.Lanes.Count; i++)
                    PopulateLane(world, edge.Lanes[i]);
    }

    void PopulateLane(World world, Lane lane)
    {
        float laneLength = lane.Edge.Length;
        float minGap     = lane.Edge.Length / 10f; // minimum separation
        int   count      = Mathf.FloorToInt(10 * trafficDensity);

        int laneNumber = worldView.GetLaneView(lane)?.LaneNumber ?? 0;

        // Generate random non-overlapping positions
        var positions = new List<float>();

        int attempts = 0;
        while (positions.Count < count && attempts < count * 10)
        {
            attempts++;
            float candidate = UnityEngine.Random.Range(0f, laneLength);

            bool tooClose = false;
            foreach (float p in positions)
            {
                if (Mathf.Abs(p - candidate) < minGap)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose) positions.Add(candidate);
        }

        // Sort so LaneIndex order matches Position order
        positions.Sort();

        for (int i = 0; i < positions.Count; i++)
        {
            AmbientDriver car = new AmbientDriver();
            car.CurrentLane = lane;
            car.LaneNumber  = laneNumber;
            car.Position    = positions[i];
            car.LaneIndex   = lane.Vehicles.Count;

            lane.Vehicles.Add(car);
            world.Agents.Add(car);
        }
    }
}
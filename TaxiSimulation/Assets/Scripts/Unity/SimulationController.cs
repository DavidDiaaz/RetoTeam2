using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SimulationManager : MonoBehaviour
{
    [Header("Spawning")]
    [Tooltip("Seconds before a new car spawns after one exits.")]
    public float respawnDelay = 2f;

    [Header("References")]
    public WorldView       worldView;
    public NavGraphBuilder builder;

    World world;

    void Start()
    {
        var graph = builder.Build(out var laneViews);
        world = new World(graph);

        worldView.SetLaneViews(laneViews);

        foreach (var (road, edge) in builder.RoadEdges)
            for (int i = 0; i < edge.Lanes.Count; i++)
                SpawnCar(edge.Lanes[i]);

        Debug.Log($"[Simulation] Ready — {world.Agents.Count} agents");
    }

    void Update()
    {
        world.Tick(Time.deltaTime);
        builder.PollLightEvents(world.Navigation);
        CheckForExits();
    }

    void CheckForExits()
    {
        for (int i = world.Agents.Count - 1; i >= 0; i--)
        {
            if (!(world.Agents[i] is VehicleAgent v)) continue;

            // Only despawn at true dead ends — road segment edges with no outgoing connections
            // Never despawn on connection edges even if their end node looks terminal
            bool atDeadEnd = v.TargetNode != null &&
                             v.TargetNode.Outgoing.Count == 0 &&
                             !v.CurrentLane.Edge.IsConnection &&
                             v.DistanceToEdgeEnd <= 0.1f;

            if (!atDeadEnd) continue;

            Lane lane = v.CurrentLane;

            world.Agents.RemoveAt(i);
            lane.Remove(v);
            worldView.DestroyVehicle(v);

            StartCoroutine(RespawnAfterDelay(lane));
        }
    }

    IEnumerator RespawnAfterDelay(Lane lane)
    {
        yield return new WaitForSeconds(respawnDelay);
        SpawnCar(lane);
    }

    void SpawnCar(Lane lane)
    {
        if (!lane.IsSegmentFree(0f, 4.5f)) return;

        var car = new AmbientDriver();
        car.CurrentLane = lane;
        car.LaneNumber  = lane.LaneNumber;
        car.Position    = 0f;
        car.Speed       = 0f;

        lane.InsertSorted(car);
        world.Agents.Add(car);
        worldView.SpawnVehicle(car);
    }
}
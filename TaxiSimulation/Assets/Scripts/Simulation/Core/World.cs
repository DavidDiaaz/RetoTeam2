using System.Collections.Generic;

public class World
{
    public NavigationGraph Navigation;
    public FleetManager    FleetManager;
    public List<Agent>     Agents = new();
    public float           DeltaTime { get; private set; }

    // Traffic light groups are ticked here instead of individual lights
    // so the group coordinator controls all state transitions.
    readonly List<TrafficLightGroup> _lightGroups = new();

    public World(NavigationGraph navigation)
    {
        Navigation   = navigation;
        FleetManager = new FleetManager(navigation);
        Agents.Add(FleetManager);
    }

    public void RegisterLightGroup(TrafficLightGroup group)
    {
        if (!_lightGroups.Contains(group))
            _lightGroups.Add(group);
    }

    public void Tick(float dt)
    {
        DeltaTime = dt;

        // Tick light groups — individual TrafficLight.Update() is gone
        foreach (var group in _lightGroups)
            group.Tick(dt);

        foreach (var agent in Agents) agent.Perceive(this);
        foreach (var agent in Agents) agent.Deliberate(this);
        foreach (var agent in Agents) agent.Act(this);

        Agents.RemoveAll(a =>
            a is Pedestrian p &&
            (p.State == PedestrianState.Done || p.State == PedestrianState.Cancelled));
    }

    public void AddTaxi(AutonomousTaxi taxi)
    {
        Agents.Add(taxi);
        FleetManager.RegisterTaxi(taxi);
    }

    public void AddPedestrian(Pedestrian p) => Agents.Add(p);
}
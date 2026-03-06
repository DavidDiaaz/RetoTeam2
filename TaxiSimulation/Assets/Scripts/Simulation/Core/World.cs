using System.Collections.Generic;

public class World
{
    public NavigationGraph Navigation;
    public FleetManager    FleetManager;
    public List<Agent>     Agents    = new();
    public float           DeltaTime { get; private set; }

    public World(NavigationGraph navigation)
    {
        Navigation   = navigation;
        FleetManager = new FleetManager(navigation);
        Agents.Add(FleetManager);
    }

    public void Tick(float dt)
    {
        DeltaTime = dt;

        // Update traffic lights
        foreach (var node in Navigation.nodes.Values)
            node.Light?.Update(dt);

        // Clear intersection contenders before perception
        foreach (var node in Navigation.nodes.Values)
            node.ClearContenders();

        foreach (var agent in Agents)
            agent.Perceive(this);

        foreach (var agent in Agents)
            agent.Deliberate(this);

        foreach (var agent in Agents)
            agent.Act(this);

        // Remove done/cancelled pedestrians
        Agents.RemoveAll(a =>
            a is Pedestrian p &&
            (p.State == PedestrianState.Done ||
             p.State == PedestrianState.Cancelled));
    }

    public void AddTaxi(AutonomousTaxi taxi)
    {
        Agents.Add(taxi);
        FleetManager.RegisterTaxi(taxi);
    }

    public void AddPedestrian(Pedestrian p)
    {
        Agents.Add(p);
    }
}
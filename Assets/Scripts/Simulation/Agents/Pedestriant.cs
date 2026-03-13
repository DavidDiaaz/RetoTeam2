using UnityEngine;

public enum PedestrianState
{
    Waiting,
    Matched,    // taxi assigned, en route to pick up
    Riding,
    Done,
    Cancelled
}

public class Pedestrian : Agent
{
    public TrafficNode     CurrentNode;
    public TrafficNode     Destination;
    public PedestrianState State          = PedestrianState.Waiting;
    public float           ToleranceTimer;

    /// <summary>World-space position where this pedestrian stands (set by SimulationManager).</summary>
    public Vector3 WorldPosition;

    bool _requestSent = false;

    public Pedestrian(TrafficNode currentNode, TrafficNode destination,
                      float toleranceSeconds, Vector3 worldPosition)
    {
        CurrentNode    = currentNode;
        Destination    = destination;
        ToleranceTimer = toleranceSeconds;
        WorldPosition  = worldPosition;
    }

    public override void Perceive(World world) { }

    public override void Deliberate(World world)
    {
        if (State == PedestrianState.Waiting || State == PedestrianState.Matched)
        {
            ToleranceTimer -= world.DeltaTime;
            if (ToleranceTimer <= 0f)
            {
                State = PedestrianState.Cancelled;
                return;
            }
        }

        if (!_requestSent && State == PedestrianState.Waiting)
        {
            _requestSent = true;
            world.FleetManager?.RequestRide(this);
        }
    }

    public override void Act(World world) { }

    public void OnMatched()    => State = PedestrianState.Matched;
    public void OnPickedUp()   => State = PedestrianState.Riding;
    public void OnDroppedOff() => State = PedestrianState.Done;
}
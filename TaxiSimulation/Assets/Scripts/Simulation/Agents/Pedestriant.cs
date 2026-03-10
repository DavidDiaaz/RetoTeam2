public enum PedestrianState
{
    Waiting,
    Matched,   // taxi assigned, not yet picked up
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

    bool requestSent = false;

    public Pedestrian(TrafficNode currentNode, TrafficNode destination, float toleranceSeconds)
    {
        CurrentNode    = currentNode;
        Destination    = destination;
        ToleranceTimer = toleranceSeconds;
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

        // Send ride request once
        if (!requestSent && State == PedestrianState.Waiting)
        {
            requestSent = true;
            world.FleetManager?.RequestRide(this);
        }
    }

    public override void Act(World world) { }

    public void OnMatched()   => State = PedestrianState.Matched;
    public void OnPickedUp()  => State = PedestrianState.Riding;
    public void OnDroppedOff() => State = PedestrianState.Done;
}
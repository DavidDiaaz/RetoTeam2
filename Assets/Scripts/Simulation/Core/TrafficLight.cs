/// <summary>
/// Pure data model for a single traffic light head.
/// State is driven externally by TrafficLightGroup — this class
/// does NOT self-update. World.Tick no longer calls light.Update().
/// </summary>
public class TrafficLight
{
    public enum State { Green, Yellow, Red }

    public State CurrentState = State.Red;

    // Durations are still stored here for inspector convenience
    // but are ignored at runtime — the Group owns timing.
    public float GreenDuration  = 20f;
    public float YellowDuration = 3f;
    public float RedDuration    = 20f;
}
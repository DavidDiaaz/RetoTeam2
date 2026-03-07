public class TrafficLight
{
    public enum State { Green, Yellow, Red }

    public State CurrentState;

    public float GreenDuration  = 20f;
    public float YellowDuration = 2f;
    public float RedDuration    = 20f;

    float timer = 0f;

    public void Update(float dt)
    {
        timer += dt;

        switch (CurrentState)
        {
            case State.Green:
                if (timer >= GreenDuration)  { CurrentState = State.Yellow; timer = 0f; }
                break;
            case State.Yellow:
                if (timer >= YellowDuration) { CurrentState = State.Red;    timer = 0f; }
                break;
            case State.Red:
                if (timer >= RedDuration)    { CurrentState = State.Green;  timer = 0f; }
                break;
        }
    }
}
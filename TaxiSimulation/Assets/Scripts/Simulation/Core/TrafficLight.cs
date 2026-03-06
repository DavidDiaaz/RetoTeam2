public class TrafficLight
{
    public enum State
    {
        Green,
        Yellow,
        Red
    }

    public State CurrentState;

    float timer;

    public void Update(float dt)
    {
        timer += dt;

        if (CurrentState == State.Green && timer > 20f)  // was 10f
            {
                CurrentState = State.Yellow;
                timer = 0;
            }
            else if (CurrentState == State.Yellow && timer > 2f)
            {
                CurrentState = State.Red;
                timer = 0;
            }
            else if (CurrentState == State.Red && timer > 20f)  // was 10f
            {
                CurrentState = State.Green;
                timer = 0;
            }
    }
}
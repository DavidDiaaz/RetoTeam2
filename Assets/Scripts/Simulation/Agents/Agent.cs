public abstract class Agent
{
    public abstract void Perceive(World world);
    public abstract void Deliberate(World world);
    public abstract void Act(World world);
}
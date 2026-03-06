using System;

public class Profile
{
    public float DesiredSpeedFactor;
    public float MinFollowingDistance;
    public float LaneChangeAggression;
    public float Kindness;            

    static Random rng = new Random();

    public static Profile RandomProfile()
    {
        return new Profile
        {
            DesiredSpeedFactor   = Range(0.7f, 1.3f),
            MinFollowingDistance = Range(4.0f, 8.0f),
            LaneChangeAggression = Range(0f,   1f),
            Kindness             = Range(0f,   1f),
        };
    }

    static float Range(float min, float max)
    {
        return (float)(min + rng.NextDouble() * (max - min));
    }
}
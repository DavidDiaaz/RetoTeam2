using System;

public class Profile
{
    public float DesiredSpeedFactor;
    public float MinFollowingDistance;
    public float LaneChangeAggression;
    public float Kindness;

    /// <summary>
    /// 0 = ignores traffic laws entirely.
    /// 1 = always follows the law strictly.
    /// Affects right-of-way priority bonus and speed limit compliance.
    /// </summary>
    public float LawAbidance;

    static readonly Random rng = new Random();

    public static Profile RandomProfile() => new Profile
    {
        DesiredSpeedFactor   = 0.7f + (float)rng.NextDouble() * 0.6f,  // 0.7–1.3
        MinFollowingDistance = 4f   + (float)rng.NextDouble() * 4f,    // 4–8m
        LaneChangeAggression = (float)rng.NextDouble(),                  // 0–1
        Kindness             = (float)rng.NextDouble(),                  // 0–1
        LawAbidance          = 0.3f + (float)rng.NextDouble() * 0.7f,  // 0.3–1.0
    };

    public static Profile Aggressive() => new Profile
    {
        DesiredSpeedFactor   = 1.3f,
        MinFollowingDistance = 4f,
        LaneChangeAggression = 0.9f,
        Kindness             = 0.1f,
        LawAbidance          = 0.2f
    };

    public static Profile Cautious() => new Profile
    {
        DesiredSpeedFactor   = 0.8f,
        MinFollowingDistance = 8f,
        LaneChangeAggression = 0.1f,
        Kindness             = 0.9f,
        LawAbidance          = 0.95f
    };
}
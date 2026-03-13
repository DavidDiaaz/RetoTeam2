using System;

/// <summary>
/// Driving personality profile. Randomised per vehicle.
///
/// MinFollowingDistance : absolute minimum gap (metres) — never go below this
/// DesiredTimeGap       : gap in seconds at current speed the driver wants to maintain
/// DesiredSpeedFactor   : fraction of the speed limit the driver naturally travels at
/// </summary>
public class Profile
{
    public float MinFollowingDistance { get; private set; }
    public float DesiredTimeGap       { get; private set; }
    public float DesiredSpeedFactor   { get; private set; }

    Profile() { }

    static readonly Random rng = new Random();

    public static Profile RandomProfile()
    {
        // Tailgater  →  MinGap 1 m,  TimeGap 0.8 s,  Speed 105%
        // Average    →  MinGap 2 m,  TimeGap 1.5 s,  Speed 100%
        // Cautious   →  MinGap 4 m,  TimeGap 2.5 s,  Speed  90%
        float t = (float)rng.NextDouble(); // 0 = aggressive, 1 = cautious

        return new Profile
        {
            MinFollowingDistance = Lerp(1f,   4f,   t),
            DesiredTimeGap       = Lerp(0.8f, 2.5f, t),
            DesiredSpeedFactor   = Lerp(1.05f, 0.88f, t)
        };
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
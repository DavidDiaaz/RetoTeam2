using UnityEngine;

public class LaneView
{
    public Lane      Lane;
    public Vector3[] Waypoints;
    public float[]   CumulativeDistances;

    public int LaneNumber; // mirrors LanePathAuthoring.LaneNumber

    public void Build()
    {
        CumulativeDistances = new float[Waypoints.Length];
        CumulativeDistances[0] = 0f;
        for (int i = 1; i < Waypoints.Length; i++)
            CumulativeDistances[i] = CumulativeDistances[i - 1] +
                                     Vector3.Distance(Waypoints[i - 1], Waypoints[i]);
    }

    public float WorldLength => CumulativeDistances != null && CumulativeDistances.Length > 0
        ? CumulativeDistances[^1] : 0f;

    public Vector3 Evaluate(float logicalPosition, float edgeLength)
    {
        if (Waypoints == null || Waypoints.Length == 0) return Vector3.zero;
        if (Waypoints.Length == 1) return Waypoints[0];

        float worldDist = WorldLength * Mathf.Clamp01(logicalPosition / edgeLength);

        for (int i = 0; i < CumulativeDistances.Length - 1; i++)
        {
            if (worldDist <= CumulativeDistances[i + 1])
            {
                float segLen = CumulativeDistances[i + 1] - CumulativeDistances[i];
                float t      = segLen > 0f ? (worldDist - CumulativeDistances[i]) / segLen : 0f;
                return Vector3.Lerp(Waypoints[i], Waypoints[i + 1], t);
            }
        }

        return Waypoints[^1];
    }

    public Vector3 TangentAt(float logicalPosition, float edgeLength)
    {
        if (Waypoints == null || Waypoints.Length < 2) return Vector3.forward;

        float worldDist = WorldLength * Mathf.Clamp01(logicalPosition / edgeLength);

        for (int i = 0; i < CumulativeDistances.Length - 1; i++)
            if (worldDist <= CumulativeDistances[i + 1])
                return (Waypoints[i + 1] - Waypoints[i]).normalized;

        return (Waypoints[^1] - Waypoints[^2]).normalized;
    }
}
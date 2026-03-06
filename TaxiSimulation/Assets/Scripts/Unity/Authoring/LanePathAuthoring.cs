using UnityEngine;

public class LanePathAuthoring : MonoBehaviour
{
    [Header("Lane")]
    public Vector3   Start;
    public Vector3   End;
    public int       SpeedLimit = 30;
    public RoadClass RoadClass  = RoadClass.Primary;

    [Header("Lane Identity")]
    [Tooltip("Which lane number this is within its parallel group (0=leftmost). " +
             "Used for lane-change logic — adjacent lanes should have consecutive numbers.")]
    public int LaneNumber = 0;

    // ---------------------------------------------------------------
    // Computed
    // ---------------------------------------------------------------
    public float    WorldLength => Vector3.Distance(Start, End);
    public Vector3  Direction   => (End - Start).normalized;

    public float MetersLength(float metersPerUnit)        => WorldLength * metersPerUnit;
    public float ExpectedTravelTime(float mpu, float kmh) => MetersLength(mpu) / (kmh * 1000f / 3600f);

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (Start == End) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(Start, End);
        Gizmos.DrawSphere(Start, 0.2f);
        Gizmos.DrawSphere(End,   0.3f);

        // Arrow at midpoint
        Vector3 mid = (Start + End) * 0.5f;
        Vector3 dir = Direction;
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(mid, dir * 1.5f);
        Gizmos.DrawRay(mid + dir * 1.5f, (Quaternion.Euler(0, 150, 0) * dir) * 0.5f);
        Gizmos.DrawRay(mid + dir * 1.5f, (Quaternion.Euler(0,-150, 0) * dir) * 0.5f);
    }
#endif
}
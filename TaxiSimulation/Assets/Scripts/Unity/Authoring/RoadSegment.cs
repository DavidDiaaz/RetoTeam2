using UnityEngine;

/// <summary>
/// Place one GameObject per road segment in the scene.
/// Transform defines everything:
///   position  = road center
///   forward   = traffic flow direction
///   localScale.z = length (Unity units)
///   localScale.x = width  (Unity units, used for lane visualization only)
/// </summary>
public class RoadSegment : MonoBehaviour
{
    [Header("Road")]
    public RoadClass RoadClass  = RoadClass.Local;

    [Tooltip("Override default lane count from RoadClass. 0 = use default.")]
    public int LaneCountOverride = 0;

    [Header("Traffic Light")]
    public bool HasTrafficLight  = false;
    public float GreenDuration   = 20f;
    public float YellowDuration  = 2f;
    public float RedDuration     = 20f;
    public TrafficLight.State InitialState = TrafficLight.State.Green;

    // ---------------------------------------------------------------
    // Computed — read by NavGraphBuilder
    // ---------------------------------------------------------------
    public int LaneCount => LaneCountOverride > 0
        ? LaneCountOverride
        : RoadClassInfo.DefaultLaneCount(RoadClass);

    public int   SpeedLimit => RoadClassInfo.SpeedLimit(RoadClass);
    public float WorldLength => transform.localScale.z;
    public float WorldWidth  => transform.localScale.x;

    // Start and end world positions (XZ, Y=0)
    public Vector3 StartPosition => Flatten(transform.position - transform.forward * WorldLength * 0.5f);
    public Vector3 EndPosition   => Flatten(transform.position + transform.forward * WorldLength * 0.5f);

    // Lane center positions at start face (for LaneView waypoints)
    // Lanes spread evenly across road width, lane 0 = leftmost
    public Vector3 LaneStartPosition(int laneIndex)
    {
        return LaneCenterPosition(StartPosition, laneIndex);
    }

    public Vector3 LaneEndPosition(int laneIndex)
    {
        return LaneCenterPosition(EndPosition, laneIndex);
    }

    Vector3 LaneCenterPosition(Vector3 faceCenter, int laneIndex)
    {
        if (LaneCount <= 1) return faceCenter;

        // Right vector relative to road direction
        Vector3 right     = Vector3.Cross(Vector3.up, transform.forward).normalized;
        float   laneWidth = WorldWidth / LaneCount;
        float   offset    = (laneIndex - (LaneCount - 1) * 0.5f) * laneWidth;

        return faceCenter + right * offset;
    }

    static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ---------------------------------------------------------------
#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Vector3 start  = StartPosition;
        Vector3 end    = EndPosition;
        Vector3 fwd    = transform.forward;
        Vector3 right  = Vector3.Cross(Vector3.up, fwd).normalized;
        float   w      = WorldWidth;
        float   len    = WorldLength;

        // Road color by class
        Color roadColor = RoadClass switch
        {
            RoadClass.Highway    => new Color(0.8f, 0.2f, 0.2f, 0.25f),
            RoadClass.Arterial   => new Color(0.9f, 0.6f, 0.1f, 0.25f),
            RoadClass.Collector  => new Color(0.2f, 0.6f, 0.9f, 0.25f),
            RoadClass.Roundabout => new Color(0.2f, 0.9f, 0.5f, 0.25f),
            _                    => new Color(0.7f, 0.7f, 0.7f, 0.25f)
        };

        // Draw filled box
        Gizmos.color = roadColor;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, new Vector3(w, 0.05f, len));
        Gizmos.DrawCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = Matrix4x4.identity;

        // Draw outline
        Gizmos.color = new Color(roadColor.r, roadColor.g, roadColor.b, 0.9f);
        Vector3 c0 = start + right * w * 0.5f;
        Vector3 c1 = start - right * w * 0.5f;
        Vector3 c2 = end   + right * w * 0.5f;
        Vector3 c3 = end   - right * w * 0.5f;
        Gizmos.DrawLine(c0, c2);
        Gizmos.DrawLine(c1, c3);
        Gizmos.DrawLine(c0, c1);
        Gizmos.DrawLine(c2, c3);

        // Lane dividers
        Gizmos.color = new Color(1f, 1f, 1f, 0.4f);
        float laneWidth = w / LaneCount;
        for (int i = 1; i < LaneCount; i++)
        {
            float   t     = -w * 0.5f + laneWidth * i;
            Vector3 ls    = start + right * t;
            Vector3 le    = end   + right * t;
            Gizmos.DrawLine(ls, le);
        }

        // Direction arrow
        Gizmos.color = Color.yellow;
        Vector3 mid = (start + end) * 0.5f;
        Gizmos.DrawRay(mid, fwd * len * 0.3f);
        Gizmos.DrawRay(mid + fwd * len * 0.3f, (Quaternion.Euler(0,  150, 0) * fwd) * len * 0.08f);
        Gizmos.DrawRay(mid + fwd * len * 0.3f, (Quaternion.Euler(0, -150, 0) * fwd) * len * 0.08f);

        // Traffic light indicator
        if (HasTrafficLight)
        {
            Gizmos.color = InitialState == TrafficLight.State.Green  ? Color.green  :
                           InitialState == TrafficLight.State.Yellow ? Color.yellow :
                           Color.red;
            Gizmos.DrawSphere(end + Vector3.up * 0.5f, 0.3f);
        }

        // Lane labels
#if UNITY_EDITOR
        for (int i = 0; i < LaneCount; i++)
        {
            Vector3 labelPos = (LaneStartPosition(i) + LaneEndPosition(i)) * 0.5f + Vector3.up * 0.1f;
            UnityEditor.Handles.Label(labelPos, $"L{i}");
        }
#endif
    }
#endif
}
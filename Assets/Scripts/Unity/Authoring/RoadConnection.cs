using UnityEngine;

/// <summary>
/// Authoring component: defines a valid lane-to-lane continuation
/// between two road segments. NavGraphBuilder reads these and creates
/// LaneLink objects in the NavigationGraph.
///
/// For same-road continuations (straight through): SourceRoad and TargetRoad
/// are segments of the same Road parent — NavGraphBuilder auto-detects these
/// and creates straight LaneLinks (MergePosition = 0).
///
/// For cross-road connections (side street entering main road):
/// MergePosition is computed from the spatial overlap between the
/// target segment and the source segment's endpoint.
/// </summary>
public class RoadConnection : MonoBehaviour
{
    [Header("Source")]
    public RoadSegment SourceRoad;
    public int         SourceLane;

    [Header("Target")]
    public RoadSegment TargetRoad;
    public int         TargetLane;

    // ---------------------------------------------------------------
    public bool IsValid =>
        SourceRoad != null && TargetRoad != null &&
        SourceLane >= 0 && SourceLane < SourceRoad.LaneCount &&
        TargetLane >= 0 && TargetLane < TargetRoad.LaneCount;

    /// <summary>
    /// Where on the target lane does traffic from SourceLane join?
    /// 0 = start of segment (straight continuation).
    /// Computed spatially by NavGraphBuilder — not set by designer.
    /// </summary>
    public float ComputeMergePosition(float metersPerUnit)
    {
        if (!IsValid) return 0f;

        Vector3 entryPoint  = SourceRoad.LaneEndPosition(SourceLane);
        Vector3 laneStart   = TargetRoad.LaneStartPosition(TargetLane);
        Vector3 laneEnd     = TargetRoad.LaneEndPosition(TargetLane);

        float   laneWorldLen = Vector3.Distance(laneStart, laneEnd);
        if (laneWorldLen < 0.001f) return 0f;

        // Project entry point onto target lane axis
        Vector3 dir      = (laneEnd - laneStart).normalized;
        float   along    = Vector3.Dot(entryPoint - laneStart, dir);
        float   clamped  = Mathf.Clamp(along, 0f, laneWorldLen);

        // Convert to logical [0,1] normalized position
        float   segLen   = TargetRoad.WorldLength * metersPerUnit;
        return Mathf.Clamp01((clamped / laneWorldLen) * (TargetRoad.WorldLength / (segLen / metersPerUnit)));
    }

    public Vector3 StartPoint =>
        SourceRoad != null ? SourceRoad.LaneEndPosition(SourceLane) : transform.position;

    public Vector3 EndPoint =>
        TargetRoad != null ? TargetRoad.LaneStartPosition(TargetLane) : transform.position;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!IsValid) return;

        Vector3 start = StartPoint;
        Vector3 end   = EndPoint;

        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
        Gizmos.DrawLine(start, end);

        Vector3 mid = (start + end) * 0.5f;
        Vector3 dir = (end - start).normalized;
        float   len = Vector3.Distance(start, end);

        Gizmos.DrawRay(mid, dir * len * 0.15f);
        Gizmos.DrawRay(mid + dir * len * 0.15f,
            (UnityEngine.Quaternion.Euler(0,  150, 0) * dir) * len * 0.05f);
        Gizmos.DrawRay(mid + dir * len * 0.15f,
            (UnityEngine.Quaternion.Euler(0, -150, 0) * dir) * len * 0.05f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(start, 0.15f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(end, 0.15f);

        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(start + Vector3.up * 0.4f,
            $"{SourceRoad?.name} L{SourceLane}");
        UnityEditor.Handles.Label(end + Vector3.up * 0.4f,
            $"{TargetRoad?.name} L{TargetLane}");
    }

    void OnDrawGizmosSelected()
    {
        DrawLaneEndpoints(SourceRoad, isEnd: true,  color: Color.cyan);
        DrawLaneEndpoints(TargetRoad, isEnd: false, color: Color.yellow);
    }

    void DrawLaneEndpoints(RoadSegment road, bool isEnd, Color color)
    {
        if (road == null) return;
        for (int i = 0; i < road.LaneCount; i++)
        {
            Vector3 pos = isEnd ? road.LaneEndPosition(i) : road.LaneStartPosition(i);
            Gizmos.color = (i == (isEnd ? SourceLane : TargetLane))
                ? color
                : new Color(color.r, color.g, color.b, 0.3f);
            Gizmos.DrawSphere(pos, 0.2f);
            UnityEditor.Handles.Label(pos + Vector3.up * 0.3f, $"L{i}");
        }
    }
#endif
}
using UnityEngine;

/// <summary>
/// Connects one specific lane on a source road's end face
/// to one specific lane on a target road's start face.
/// One connection per lane pair. NavGraphBuilder reads these
/// after building all road edges and adds one edge per connection.
/// </summary>
public class RoadConnection : MonoBehaviour
{
    [Header("Source")]
    public RoadSegment SourceRoad;
    public int         SourceLane;

    [Header("Target")]
    public RoadSegment TargetRoad;
    public int         TargetLane; // becomes EntryLaneRequired on the edge

    [Header("Road Class")]
    [Tooltip("Road class of this connection arc. Usually matches source or target.")]
    public RoadClass RoadClass = RoadClass.Local;

    // ---------------------------------------------------------------
    // Computed
    // ---------------------------------------------------------------

    public bool IsValid =>
        SourceRoad != null && TargetRoad != null &&
        SourceLane >= 0 && SourceLane < SourceRoad.LaneCount &&
        TargetLane >= 0 && TargetLane < TargetRoad.LaneCount;

    public Vector3 StartPoint =>
        SourceRoad != null ? SourceRoad.LaneEndPosition(SourceLane)   : transform.position;

    public Vector3 EndPoint =>
        TargetRoad != null ? TargetRoad.LaneStartPosition(TargetLane) : transform.position;

    public float WorldLength =>
        Vector3.Distance(StartPoint, EndPoint);

    // ---------------------------------------------------------------
#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!IsValid) return;

        Vector3 start = StartPoint;
        Vector3 end   = EndPoint;

        // Connection arc color
        Gizmos.color = IsValid ? new Color(0.3f, 1f, 0.4f, 0.9f) : new Color(1f, 0.2f, 0.2f, 0.9f);

        // Draw line
        Gizmos.DrawLine(start, end);

        // Arrow at midpoint
        Vector3 mid = (start + end) * 0.5f;
        Vector3 dir = (end - start).normalized;
        float   len = WorldLength;

        Gizmos.DrawRay(mid, dir * len * 0.15f);
        Gizmos.DrawRay(
            mid + dir * len * 0.15f,
            (Quaternion.Euler(0,  150, 0) * dir) * len * 0.05f);
        Gizmos.DrawRay(
            mid + dir * len * 0.15f,
            (Quaternion.Euler(0, -150, 0) * dir) * len * 0.05f);

        // Endpoint dots
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(start, 0.15f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(end, 0.15f);

#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(
            start + Vector3.up * 0.4f,
            SourceRoad != null ? $"{SourceRoad.name} L{SourceLane}" : "?");
        UnityEditor.Handles.Label(
            end + Vector3.up * 0.4f,
            TargetRoad != null ? $"{TargetRoad.name} L{TargetLane}" : "?");
#endif
    }

    // Draw lane endpoint dots on each road so designer can see connection points
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
using UnityEngine;
using System.Collections.Generic;

public class Road : MonoBehaviour
{
    [Header("Road")]
    public RoadClass RoadClass        = RoadClass.Local;
    public int       LaneCountOverride = 0;

    [Header("Traffic Light")]
    public bool               HasTrafficLight = false;
    public float              GreenDuration   = 20f;
    public float              YellowDuration  = 2f;
    public float              RedDuration     = 20f;
    public TrafficLight.State InitialState    = TrafficLight.State.Green;

    // ---------------------------------------------------------------
    public int   LaneCount  => LaneCountOverride > 0 ? LaneCountOverride : RoadClassInfo.DefaultLaneCount(RoadClass);
    public int   SpeedLimit => RoadClassInfo.SpeedLimit(RoadClass);
    public float WorldLength => transform.localScale.z;
    public float WorldWidth  => transform.localScale.x;

    public Vector3 StartPosition => Flatten(transform.position - transform.forward * WorldLength * 0.5f);
    public Vector3 EndPosition   => Flatten(transform.position + transform.forward * WorldLength * 0.5f);

    public Vector3 LaneStartPosition(int laneIndex) => LaneCenterPosition(StartPosition, laneIndex);
    public Vector3 LaneEndPosition(int laneIndex)   => LaneCenterPosition(EndPosition,   laneIndex);

    [HideInInspector] public List<RoadSegment> Segments = new();

    // ---------------------------------------------------------------
    /// <summary>
    /// Returns the (tEnter, tExit) interval that the other road's area
    /// covers on this road's forward axis, in [0,1] space.
    /// Returns (-1,-1) if no overlap.
    /// </summary>
    public (float tEnter, float tExit) GetOverlapInterval(Road other)
    {
        // Project other road's center onto this road's world-space axis
        Vector3 origin = StartPosition;
        Vector3 fwd    = transform.forward;

        // Other road's half-extent along our forward axis in world space
        float otherHalfZ = Mathf.Abs(Vector3.Dot(other.transform.forward * other.WorldLength * 0.5f, fwd))
                        + Mathf.Abs(Vector3.Dot(other.transform.right   * other.WorldWidth  * 0.5f, fwd));

        // Other road's half-extent along our right axis in world space
        Vector3 right     = Vector3.Cross(Vector3.up, fwd).normalized;
        float   otherHalfX = Mathf.Abs(Vector3.Dot(other.transform.forward * other.WorldLength * 0.5f, right))
                        + Mathf.Abs(Vector3.Dot(other.transform.right   * other.WorldWidth  * 0.5f, right));

        // Other center projected onto our axes
        Vector3 toOther  = other.transform.position - transform.position;
        float   projFwd  = Vector3.Dot(toOther, fwd);
        float   projRight = Vector3.Dot(toOther, right);

        // No overlap along our right axis — roads don't cross laterally
        if (Mathf.Abs(projRight) > WorldWidth * 0.5f + otherHalfX) return (-1f, -1f);

        // No overlap along our forward axis
        if (Mathf.Abs(projFwd) > WorldLength * 0.5f + otherHalfZ) return (-1f, -1f);

        // Interval in world units from our start, converted to [0,1]
        float enterWorld = projFwd - otherHalfZ;
        float exitWorld  = projFwd + otherHalfZ;

        float enter = (enterWorld + WorldLength * 0.5f) / WorldLength;
        float exit  = (exitWorld  + WorldLength * 0.5f) / WorldLength;

        return (Mathf.Clamp01(enter), Mathf.Clamp01(exit));
    }

    // ---------------------------------------------------------------
    /// <summary>
    /// Destroys existing child segments and creates new ones
    /// from the gaps between the masked intervals.
    /// </summary>
    public void Rebuild(List<(float tEnter, float tExit)> masks)
    {
        foreach (var seg in Segments)
            if (seg != null)
                DestroyImmediate(seg.gameObject);
        Segments.Clear();

        // Sort and merge overlapping masks
        masks.Sort((a, b) => a.tEnter.CompareTo(b.tEnter));

        var merged = new List<(float tEnter, float tExit)>();
        foreach (var mask in masks)
        {
            if (merged.Count == 0 || mask.tEnter > merged[merged.Count - 1].tExit)
                merged.Add(mask);
            else
                merged[merged.Count - 1] = (
                    merged[merged.Count - 1].tEnter,
                    Mathf.Max(merged[merged.Count - 1].tExit, mask.tExit));
        }

        // Collect free intervals between masks
        var free = new List<(float tStart, float tEnd)>();
        float cursor = 0f;

        foreach (var mask in merged)
        {
            if (mask.tEnter > cursor + 0.01f)
                free.Add((cursor, mask.tEnter));
            cursor = mask.tExit;
        }

        if (cursor < 0.99f)
            free.Add((cursor, 1f));

        // Create one segment per free interval
        for (int i = 0; i < free.Count; i++)
        {
            float tStart = free[i].tStart;
            float tEnd   = free[i].tEnd;
            float length = (tEnd - tStart) * WorldLength;

            if (length < 1f) continue; // skip slivers

            Vector3 segStart = Vector3.Lerp(StartPosition, EndPosition, tStart);
            Vector3 segEnd   = Vector3.Lerp(StartPosition, EndPosition, tEnd);
            Vector3 center   = (segStart + segEnd) * 0.5f;

            var go = new GameObject($"{name}_seg{i}");
            go.transform.SetParent(transform);
            go.transform.position   = center;
            go.transform.rotation   = transform.rotation;
            go.transform.localScale = new Vector3(WorldWidth, transform.localScale.y, length);

            var seg = go.AddComponent<RoadSegment>();
            seg.RoadClass         = RoadClass;
            seg.LaneCountOverride = LaneCount;
            seg.HasTrafficLight   = false;
            seg.ParentRoad        = this;
            seg.SegmentIndex      = i;
            seg.IsLastSegment     = (i == free.Count - 1);

            if (seg.IsLastSegment && HasTrafficLight)
            {
                seg.HasTrafficLight = true;
                seg.GreenDuration   = GreenDuration;
                seg.YellowDuration  = YellowDuration;
                seg.RedDuration     = RedDuration;
                seg.InitialState    = InitialState;
            }

            Segments.Add(seg);
        }
    }

    // ---------------------------------------------------------------
    Vector3 LaneCenterPosition(Vector3 faceCenter, int laneIndex)
    {
        if (LaneCount <= 1) return faceCenter;
        Vector3 right     = Vector3.Cross(Vector3.up, transform.forward).normalized;
        float   laneWidth = WorldWidth / LaneCount;
        float   offset    = (laneIndex - (LaneCount - 1) * 0.5f) * laneWidth;
        return faceCenter + right * offset;
    }

    static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (Segments.Count > 0) return;
        DrawAreaGizmo();
    }

    void OnDrawGizmosSelected() => DrawAreaGizmo();

    void DrawAreaGizmo()
    {
        Vector3 start = StartPosition;
        Vector3 end   = EndPosition;
        Vector3 fwd   = transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        float   w     = WorldWidth;
        float   len   = WorldLength;

        Color roadColor = RoadClass switch
        {
            RoadClass.Highway    => new Color(0.8f, 0.2f, 0.2f, 0.2f),
            RoadClass.Arterial   => new Color(0.9f, 0.6f, 0.1f, 0.2f),
            RoadClass.Collector  => new Color(0.2f, 0.6f, 0.9f, 0.2f),
            RoadClass.Roundabout => new Color(0.2f, 0.9f, 0.5f, 0.2f),
            _                    => new Color(0.7f, 0.7f, 0.7f, 0.2f)
        };

        Gizmos.color  = roadColor;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, new Vector3(w, 0.05f, len));
        Gizmos.DrawCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = Matrix4x4.identity;

        Gizmos.color = new Color(roadColor.r, roadColor.g, roadColor.b, 0.6f);
        Gizmos.DrawLine(start + right * w * 0.5f, end   + right * w * 0.5f);
        Gizmos.DrawLine(start - right * w * 0.5f, end   - right * w * 0.5f);
        Gizmos.DrawLine(start + right * w * 0.5f, start - right * w * 0.5f);
        Gizmos.DrawLine(end   + right * w * 0.5f, end   - right * w * 0.5f);

        Gizmos.color = Color.yellow;
        Vector3 mid  = (start + end) * 0.5f;
        Gizmos.DrawRay(mid, fwd * len * 0.3f);
        Gizmos.DrawRay(mid + fwd * len * 0.3f, (Quaternion.Euler(0,  150, 0) * fwd) * len * 0.08f);
        Gizmos.DrawRay(mid + fwd * len * 0.3f, (Quaternion.Euler(0, -150, 0) * fwd) * len * 0.08f);

        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, name);
    }
#endif
}
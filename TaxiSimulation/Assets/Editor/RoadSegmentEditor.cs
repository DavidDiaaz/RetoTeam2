#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(RoadSegment))]
public class RoadSegmentEditor : Editor
{
    RoadSegment road;

    void OnEnable() => road = (RoadSegment)target;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ---- Road ----
        EditorGUILayout.LabelField("Road", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("RoadClass"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("LaneCountOverride"));

        // Read-only info
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.IntField("Speed Limit (km/h)", road.SpeedLimit);
        EditorGUILayout.IntField("Lane Count", road.LaneCount);
        EditorGUILayout.FloatField("Length (world units)", road.WorldLength);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(8);

        // ---- Traffic Light ----
        EditorGUILayout.LabelField("Traffic Light", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("HasTrafficLight"));

        if (road.HasTrafficLight)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("InitialState"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("GreenDuration"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("YellowDuration"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("RedDuration"));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8);

        // ---- Info box ----
        float metersPerUnit = 4.5f;
        var builder = FindFirstObjectByType<NavGraphBuilder>();
        if (builder != null) metersPerUnit = builder.metersPerUnit;

        float meters  = road.WorldLength * metersPerUnit;
        float seconds = meters / (road.SpeedLimit * 1000f / 3600f);

        EditorGUILayout.HelpBox(
            $"Real length : {meters:F1} m\n" +
            $"At {road.SpeedLimit} km/h : {seconds:F1} s\n" +
            $"Lanes : {road.LaneCount}",
            MessageType.Info);

        serializedObject.ApplyModifiedProperties();
    }

    void OnSceneGUI()
    {
        // Direction handle — drag end point
        EditorGUI.BeginChangeCheck();
        Vector3 newEnd = Handles.FreeMoveHandle(
            road.EndPosition, 0.4f, Vector3.zero, Handles.SphereHandleCap);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(road.transform, "Move Road End");
            // Recompute forward and length from new end
            Vector3 start  = road.StartPosition;
            Vector3 dir    = (newEnd - start);
            float   newLen = dir.magnitude;
            if (newLen > 0.1f)
            {
                road.transform.forward         = dir.normalized;
                Vector3 scale                  = road.transform.localScale;
                scale.z                        = newLen;
                road.transform.localScale      = scale;
                road.transform.position        = (start + newEnd) * 0.5f;
            }
            EditorUtility.SetDirty(road.transform);
        }

        Handles.color = Color.white;
        Handles.Label(road.StartPosition + Vector3.up * 0.5f, "START");
        Handles.Label(road.EndPosition   + Vector3.up * 0.5f, "END");
    }
}
#endif
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Road))]
public class RoadEditor : Editor
{
    Road road;

    void OnEnable() => road = (Road)target;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Road", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("RoadClass"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("LaneCountOverride"));

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.IntField("Speed Limit (km/h)", road.SpeedLimit);
        EditorGUILayout.IntField("Lane Count", road.LaneCount);
        EditorGUILayout.FloatField("Length (world units)", road.WorldLength);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField("Traffic Light (last segment)", EditorStyles.boldLabel);
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

        // Segment summary
        EditorGUILayout.LabelField("Generated Segments", EditorStyles.boldLabel);
        if (road.Segments.Count == 0)
        {
            EditorGUILayout.HelpBox("No segments yet. Press Rebuild in Window → Road Builder.", MessageType.Info);
        }
        else
        {
            var builder = FindFirstObjectByType<NavGraphBuilder>();
            float mpu = builder != null ? builder.metersPerUnit : 4.5f;

            foreach (var seg in road.Segments)
            {
                if (seg == null) continue;
                float meters  = seg.WorldLength * mpu;
                string light  = seg.HasTrafficLight ? " [LIGHT]" : "";
                EditorGUILayout.LabelField($"  {seg.name}  {meters:F1}m{light}");
            }
        }

        EditorGUILayout.Space(8);

        // Info box
        if (road.Segments.Count == 0)
        {
            var builder = FindFirstObjectByType<NavGraphBuilder>();
            float mpu   = builder != null ? builder.metersPerUnit : 4.5f;
            float meters = road.WorldLength * mpu;
            float secs   = meters / (road.SpeedLimit * 1000f / 3600f);

            EditorGUILayout.HelpBox(
                $"Full length : {meters:F1} m\n" +
                $"At {road.SpeedLimit} km/h : {secs:F1} s\n" +
                $"Lanes : {road.LaneCount}",
                MessageType.Info);
        }

        if (GUILayout.Button("Rebuild This Road"))
        {
            var allRoads = FindObjectsByType<Road>(FindObjectsSortMode.None);
            var masks    = new System.Collections.Generic.List<(float tEnter, float tExit)>();

            foreach (var other in allRoads)
            {
                if (other == road) continue;
                var (tEnter, tExit) = road.GetOverlapInterval(other);
                if (tEnter >= 0f)
                    masks.Add((tEnter, tExit));
            }

            Undo.RecordObject(road, "Rebuild Road");
            road.Rebuild(masks);
            SceneView.RepaintAll();
        }

        serializedObject.ApplyModifiedProperties();
    }

    void OnSceneGUI()
    {
        EditorGUI.BeginChangeCheck();
        Vector3 newEnd = Handles.FreeMoveHandle(
            road.EndPosition, 0.4f, Vector3.zero, Handles.SphereHandleCap);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(road.transform, "Move Road End");
            Vector3 start  = road.StartPosition;
            Vector3 dir    = newEnd - start;
            float   newLen = dir.magnitude;
            if (newLen > 0.1f)
            {
                road.transform.forward    = dir.normalized;
                Vector3 scale             = road.transform.localScale;
                scale.z                   = newLen;
                road.transform.localScale = scale;
                road.transform.position   = (start + newEnd) * 0.5f;
            }
            EditorUtility.SetDirty(road.transform);
        }

        Handles.color = Color.white;
        Handles.Label(road.StartPosition + Vector3.up * 0.5f, "START");
        Handles.Label(road.EndPosition   + Vector3.up * 0.5f, "END");
    }
}
#endif
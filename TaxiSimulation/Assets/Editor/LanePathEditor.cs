#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(LanePathAuthoring))]
public class LanePathEditor : Editor
{
    LanePathAuthoring lane;

    void OnEnable() => lane = (LanePathAuthoring)target;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);

        // ---- Live readout ----
        var builders = FindObjectsByType<NavGraphBuilder>(FindObjectsSortMode.None);
        float mpu = builders.Length > 0 ? builders[0].metersPerUnit : 4.5f;

        float meters  = lane.MetersLength(mpu);
        float seconds = lane.ExpectedTravelTime(mpu, lane.SpeedLimit);

        EditorGUILayout.HelpBox(
            $"World length : {lane.WorldLength:F1} units\n" +
            $"Real length  : {meters:F1} m\n" +
            $"At {lane.SpeedLimit} km/h : {seconds:F2} s",
            MessageType.Info);

        EditorGUILayout.Space(4);

        // ---- Place buttons ----
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("📍 Place Start at Camera"))
        {
            Undo.RecordObject(lane, "Place Start");
            var cam = SceneView.lastActiveSceneView.camera;
            lane.Start   = cam.transform.position + cam.transform.forward * 5f;
            lane.Start.y = 0f;
            EditorUtility.SetDirty(lane);
        }

        if (GUILayout.Button("📍 Place End at Camera"))
        {
            Undo.RecordObject(lane, "Place End");
            var cam = SceneView.lastActiveSceneView.camera;
            lane.End   = cam.transform.position + cam.transform.forward * 5f;
            lane.End.y = 0f;
            EditorUtility.SetDirty(lane);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // ---- Reverse ----
        if (GUILayout.Button("⇄ Reverse Direction"))
        {
            Undo.RecordObject(lane, "Reverse Lane");
            (lane.Start, lane.End) = (lane.End, lane.Start);
            EditorUtility.SetDirty(lane);
        }
    }

    void OnSceneGUI()
    {
        // Start handle
        EditorGUI.BeginChangeCheck();
        Vector3 newStart = Handles.FreeMoveHandle(
            lane.Start, 0.4f, Vector3.zero, Handles.SphereHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(lane, "Move Lane Start");
            lane.Start   = newStart;
            lane.Start.y = 0f;
            EditorUtility.SetDirty(lane);
        }

        // End handle
        EditorGUI.BeginChangeCheck();
        Vector3 newEnd = Handles.FreeMoveHandle(
            lane.End, 0.5f, Vector3.zero, Handles.SphereHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(lane, "Move Lane End");
            lane.End   = newEnd;
            lane.End.y = 0f;
            EditorUtility.SetDirty(lane);
        }

        // Labels
        Handles.color = Color.white;
        Handles.Label(lane.Start + Vector3.up * 0.5f, "START");
        Handles.Label(lane.End   + Vector3.up * 0.5f, "END");

        // Line
        Handles.color = Color.cyan;
        Handles.DrawLine(lane.Start, lane.End, 2f);
    }
}
#endif
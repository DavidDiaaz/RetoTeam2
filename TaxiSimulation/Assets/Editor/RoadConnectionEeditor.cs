#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(RoadConnection))]
public class RoadConnectionEditor : Editor
{
    RoadConnection conn;

    void OnEnable() => conn = (RoadConnection)target;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ---- Source ----
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("SourceRoad"));

        if (conn.SourceRoad != null)
        {
            var sourceLaneProp = serializedObject.FindProperty("SourceLane");
            sourceLaneProp.intValue = EditorGUILayout.IntSlider(
                "Source Lane", sourceLaneProp.intValue, 0, conn.SourceRoad.LaneCount - 1);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.LabelField("connects from end face of", conn.SourceRoad.name);
            EditorGUI.EndDisabledGroup();
        }
        else
        {
            EditorGUILayout.HelpBox("Assign a Source Road.", MessageType.Warning);
        }

        EditorGUILayout.Space(6);

        // ---- Target ----
        EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("TargetRoad"));

        if (conn.TargetRoad != null)
        {
            var targetLaneProp = serializedObject.FindProperty("TargetLane");
            targetLaneProp.intValue = EditorGUILayout.IntSlider(
                "Target Lane (Entry)", targetLaneProp.intValue, 0, conn.TargetRoad.LaneCount - 1);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.LabelField("connects to start face of", conn.TargetRoad.name);
            EditorGUI.EndDisabledGroup();
        }
        else
        {
            EditorGUILayout.HelpBox("Assign a Target Road.", MessageType.Warning);
        }

        EditorGUILayout.Space(6);

        // ---- Road Class ----
        EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("RoadClass"));

        EditorGUILayout.Space(6);

        // ---- Info / Validation ----
        if (conn.IsValid)
        {
            float metersPerUnit = 4.5f;
            var builder = FindFirstObjectByType<NavGraphBuilder>();
            if (builder != null) metersPerUnit = builder.metersPerUnit;

            float meters = conn.WorldLength * metersPerUnit;

            EditorGUILayout.HelpBox(
                $"✓ Valid connection\n" +
                $"{conn.SourceRoad.name} Lane {conn.SourceLane} → " +
                $"{conn.TargetRoad.name} Lane {conn.TargetLane}\n" +
                $"Length: {meters:F1} m\n" +
                $"EntryLaneRequired: {conn.TargetLane}",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Connection is incomplete — assign both roads and valid lane indices.",
                MessageType.Error);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
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
            var prop = serializedObject.FindProperty("SourceLane");
            prop.intValue = EditorGUILayout.IntSlider(
                "Source Lane", prop.intValue, 0, conn.SourceRoad.LaneCount - 1);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.LabelField("exits from end face of", conn.SourceRoad.name);
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
            var prop = serializedObject.FindProperty("TargetLane");
            prop.intValue = EditorGUILayout.IntSlider(
                "Target Lane", prop.intValue, 0, conn.TargetRoad.LaneCount - 1);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.LabelField("enters start face of", conn.TargetRoad.name);
            EditorGUI.EndDisabledGroup();
        }
        else
        {
            EditorGUILayout.HelpBox("Assign a Target Road.", MessageType.Warning);
        }

        EditorGUILayout.Space(6);

        // ---- Info / Validation ----
        if (conn.IsValid)
        {
            float metersPerUnit = 4.5f;
            var builder = FindFirstObjectByType<NavGraphBuilder>();
            if (builder != null) metersPerUnit = builder.metersPerUnit;

            float mergePos = conn.ComputeMergePosition(metersPerUnit);
            bool  straight = mergePos < 0.01f;
            bool  samePRoad = conn.SourceRoad.ParentRoad == conn.TargetRoad.ParentRoad;

            string kind = samePRoad
                ? "Same-road continuation (auto-linked by NavGraphBuilder)"
                : straight
                    ? "Straight entry (merge position ≈ 0)"
                    : $"Side merge at {mergePos:P0} along target lane";

            EditorGUILayout.HelpBox(
                $"✓ Valid\n" +
                $"{conn.SourceRoad.name} L{conn.SourceLane}  →  " +
                $"{conn.TargetRoad.name} L{conn.TargetLane}\n" +
                $"{kind}",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Incomplete — assign both roads and valid lane indices.",
                MessageType.Error);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
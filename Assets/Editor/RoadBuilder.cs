#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class RoadBuilder : EditorWindow
{
    [MenuItem("Window/Road Builder")]
    public static void Open() => GetWindow<RoadBuilder>("Road Builder");

    void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Road Builder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Place Road objects in the scene, then press Rebuild.\n\n" +
            "Overlapping Road areas are subtracted from each road, " +
            "leaving segments in the gaps between intersections.",
            MessageType.Info);

        EditorGUILayout.Space(8);

        if (GUILayout.Button("Rebuild All Roads", GUILayout.Height(36)))
            RebuildAll();

        EditorGUILayout.Space(4);

        if (GUILayout.Button("Clear All Segments", GUILayout.Height(24)))
            ClearAll();
    }

    public static void RebuildAll()
    {
        var roads = FindObjectsByType<Road>(FindObjectsSortMode.None);

        if (roads.Length == 0)
        {
            Debug.LogWarning("[RoadBuilder] No Road objects found in scene.");
            return;
        }

        Undo.SetCurrentGroupName("Rebuild Roads");
        int group = Undo.GetCurrentGroup();

        foreach (var road in roads)
        {
            var masks = new List<(float tEnter, float tExit)>();

            foreach (var other in roads)
            {
                if (other == road) continue;
                var (tEnter, tExit) = road.GetOverlapInterval(other);
                if (tEnter >= 0f)
                    masks.Add((tEnter, tExit));
            }

            Debug.Log($"[RoadBuilder] '{road.name}' masks: " +
                      string.Join(", ", masks.ConvertAll(m => $"[{m.tEnter:F2}–{m.tExit:F2}]")));

            Undo.RecordObject(road, "Rebuild Road Segments");
            road.Rebuild(masks);

            Debug.Log($"[RoadBuilder] '{road.name}' → {road.Segments.Count} segment(s)");
        }

        Undo.CollapseUndoOperations(group);
        SceneView.RepaintAll();
    }

    static void ClearAll()
    {
        var roads = FindObjectsByType<Road>(FindObjectsSortMode.None);
        Undo.SetCurrentGroupName("Clear Road Segments");
        int group = Undo.GetCurrentGroup();

        foreach (var road in roads)
        {
            Undo.RecordObject(road, "Clear Segments");
            road.Rebuild(new List<(float, float)>());
        }

        Undo.CollapseUndoOperations(group);
        SceneView.RepaintAll();
    }
}
#endif
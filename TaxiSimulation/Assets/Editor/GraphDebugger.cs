#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Text;

public class GraphDebugger : EditorWindow
{
    Vector2 scrollPos;
    string  report = "Press Build & Inspect to generate report.";
    bool    built  = false;

    [MenuItem("Window/Graph Debugger")]
    public static void Open() => GetWindow<GraphDebugger>("Graph Debugger");

    void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Build & Inspect", GUILayout.Height(30)))
            RunInspection();

        if (built && GUILayout.Button("Clear", GUILayout.Height(30), GUILayout.Width(60)))
        {
            report = "Press Build & Inspect to generate report.";
            built  = false;
            Repaint();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    void RunInspection()
    {
        var builder = FindFirstObjectByType<NavGraphBuilder>();
        if (builder == null)
        {
            report = "ERROR: No NavGraphBuilder found in scene.";
            Repaint();
            return;
        }

        var graph = builder.Build(out var laneViews);
        var sb    = new StringBuilder();

        // Collect all edges
        var seen     = new HashSet<TrafficEdge>();
        var allEdges = new List<TrafficEdge>();
        foreach (var node in graph.nodes.Values)
            foreach (var edge in node.Outgoing)
                if (seen.Add(edge)) allEdges.Add(edge);

        sb.AppendLine("=== GRAPH INSPECTION REPORT ===");
        sb.AppendLine($"Nodes     : {graph.nodes.Count}");
        sb.AppendLine($"Edges     : {allEdges.Count}");
        sb.AppendLine($"LaneLinks : {graph.links.Count}");
        sb.AppendLine($"LaneViews : {laneViews.Count}");
        sb.AppendLine();

        // ---- Nodes ----
        sb.AppendLine("--- NODES ---");
        foreach (var kvp in graph.nodes)
        {
            var    node  = kvp.Value;
            string label = node.Label != "" ? $"[{node.Label}]" : "";
            string light = node.Light != null
                ? $" [LIGHT g={node.Light.GreenDuration}s y={node.Light.YellowDuration}s r={node.Light.RedDuration}s]"
                : "";

            if (node.Outgoing.Count == 0)
                sb.AppendLine($"  Node {node.id} {label}  TERMINAL{light}");
            else
            {
                sb.Append($"  Node {node.id} {label}  → ");
                foreach (var e in node.Outgoing)
                    sb.Append($"{e.to.id}[{e.to.Label}] ");
                sb.AppendLine(light);
            }
        }
        sb.AppendLine();

        // ---- Edges ----
        sb.AppendLine("--- EDGES ---");
        foreach (var edge in allEdges)
        {
            string lanes = "";
            for (int i = 0; i < edge.Lanes.Count; i++)
            {
                int linkCount = graph.GetLinksFrom(edge.Lanes[i]).Count;
                lanes += $"L{i}({linkCount}out) ";
            }
            sb.AppendLine(
                $"  [{edge.from.Label}]→[{edge.to.Label}]  " +
                $"{edge.Length:F1}m  {edge.SpeedLimit}km/h  [{lanes.Trim()}]");
        }
        sb.AppendLine();

        // ---- Lane Links ----
        sb.AppendLine("--- LANE LINKS ---");
        foreach (var link in graph.links)
        {
            string kind = link.IsStraight ? "straight" : $"merge@{link.MergePosition:F2}";
            sb.AppendLine(
                $"  [{link.SourceLane.Edge.from.Label}→{link.SourceLane.Edge.to.Label}] " +
                $"L{link.SourceLane.LaneNumber}  →  " +
                $"[{link.DestLane.Edge.from.Label}→{link.DestLane.Edge.to.Label}] " +
                $"L{link.DestLane.LaneNumber}  ({kind})");
        }
        sb.AppendLine();

        // ---- Connectivity warnings ----
        sb.AppendLine("--- CONNECTIVITY WARNINGS ---");
        bool clean = true;

        foreach (var edge in allEdges)
        {
            if (edge.to.Outgoing.Count == 0) continue; // terminal — OK

            for (int i = 0; i < edge.Lanes.Count; i++)
            {
                var links = graph.GetLinksFrom(edge.Lanes[i]);
                if (links.Count == 0)
                {
                    sb.AppendLine(
                        $"  ⚠ [{edge.from.Label}→{edge.to.Label}] " +
                        $"L{i} has NO outgoing LaneLink — vehicle will be stuck.");
                    clean = false;
                }
            }
        }

        // Check merge positions are reasonable
        foreach (var link in graph.links)
        {
            if (!link.IsStraight && (link.MergePosition < 0f || link.MergePosition > 1f))
            {
                sb.AppendLine(
                    $"  ⚠ LaneLink {link.SourceLane.Edge.from.Label}→{link.DestLane.Edge.to.Label} " +
                    $"has invalid MergePosition={link.MergePosition:F3}");
                clean = false;
            }
        }

        if (clean) sb.AppendLine("  ✓ No issues found.");
        sb.AppendLine();

        // ---- Road segments ----
        sb.AppendLine("--- ROAD SEGMENTS ---");
        var roads = FindObjectsByType<Road>(FindObjectsSortMode.None);
        foreach (var road in roads)
        {
            sb.AppendLine(
                $"  {road.name}  {road.RoadClass}  {road.LaneCount} lanes  " +
                $"{road.SpeedLimit}km/h  {road.Segments.Count} segment(s)");
            foreach (var seg in road.Segments)
            {
                if (seg == null) continue;
                float m   = seg.WorldLength * builder.metersPerUnit;
                string lt = seg.HasTrafficLight ? " [LIGHT]" : "";
                sb.AppendLine($"    {seg.name}  {m:F1}m{lt}");
            }
        }
        sb.AppendLine();

        // ---- Road connections ----
        sb.AppendLine("--- ROAD CONNECTIONS ---");
        var conns = FindObjectsByType<RoadConnection>(FindObjectsSortMode.None);
        if (conns.Length == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var conn in conns)
            {
                if (!conn.IsValid)
                {
                    sb.AppendLine($"  ✗ {conn.name}  INVALID");
                    continue;
                }
                float mergePos = conn.ComputeMergePosition(builder.metersPerUnit);
                string kind    = mergePos < 0.01f ? "straight" : $"merge@{mergePos:F2}";
                sb.AppendLine(
                    $"  ✓ {conn.name}  " +
                    $"{conn.SourceRoad.name}[L{conn.SourceLane}] → " +
                    $"{conn.TargetRoad.name}[L{conn.TargetLane}]  ({kind})");
            }
        }

        report = sb.ToString();
        built  = true;
        Debug.Log("[GraphDebugger]\n" + report);
        Repaint();
    }
}
#endif
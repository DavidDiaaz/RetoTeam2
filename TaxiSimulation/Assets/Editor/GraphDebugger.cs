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

        // Count edges
        var allEdges  = new List<TrafficEdge>();
        int connEdges = 0, roadEdges = 0;

        foreach (var node in graph.nodes.Values)
            foreach (var edge in node.Outgoing)
            {
                allEdges.Add(edge);
                if (edge.IsConnection) connEdges++;
                else roadEdges++;
            }

        sb.AppendLine("=== GRAPH INSPECTION REPORT ===");
        sb.AppendLine($"Nodes : {graph.nodes.Count}");
        sb.AppendLine($"Edges : {allEdges.Count}  (road={roadEdges}  connection={connEdges})");
        sb.AppendLine($"Lanes : {laneViews.Count}");
        sb.AppendLine();

        // ---- Nodes ----
        sb.AppendLine("--- NODES ---");
        foreach (var kvp in graph.nodes)
        {
            var    node  = kvp.Value;
            string label = node.Label != "" ? $"[{node.Label}]" : "";
            string light = node.Light != null
                ? $"[LIGHT g={node.Light.GreenDuration}s y={node.Light.YellowDuration}s r={node.Light.RedDuration}s]"
                : "";

            if (node.Outgoing.Count == 0)
                sb.AppendLine($"  Node {node.id} {label}  TERMINAL  {light}");
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
            string kind  = edge.IsConnection ? "CONN" : "ROAD";
            string entry = edge.EntryLaneRequired >= 0 ? $"entryReq={edge.EntryLaneRequired}" : "entryReq=any";
            string lanes = "";
            for (int i = 0; i < edge.Lanes.Count; i++) lanes += $"L{i} ";

            sb.AppendLine(
                $"  {kind}  [{edge.from.Label}]→[{edge.to.Label}]  " +
                $"{edge.Length:F1}m  {edge.SpeedLimit}km/h  " +
                $"lanes=[{lanes.Trim()}]  {entry}");
        }

        sb.AppendLine();

        // ---- Connectivity warnings ----
        sb.AppendLine("--- CONNECTIVITY WARNINGS ---");
        bool clean = true;

        // Connection to terminal
        foreach (var node in graph.nodes.Values)
        {
            if (node.Outgoing.Count > 0) continue;
            foreach (var other in graph.nodes.Values)
                foreach (var e in other.Outgoing)
                    if (e.to == node && e.IsConnection)
                    {
                        sb.AppendLine($"  ⚠ [{node.Label}] is TERMINAL but has incoming CONNECTION — target segment may be missing.");
                        clean = false;
                    }
        }

        // entryReq out of range
        foreach (var edge in allEdges)
        {
            if (!edge.IsConnection || edge.EntryLaneRequired < 0) continue;
            foreach (var outEdge in edge.to.Outgoing)
            {
                if (outEdge.IsConnection) continue;
                if (edge.EntryLaneRequired >= outEdge.Lanes.Count)
                {
                    sb.AppendLine(
                        $"  ⚠ CONN [{edge.from.Label}→{edge.to.Label}] " +
                        $"entryReq={edge.EntryLaneRequired} but target has only {outEdge.Lanes.Count} lane(s).");
                    clean = false;
                }
            }
        }

        // Lane with no reachable outgoing
        foreach (var edge in allEdges)
        {
            if (edge.IsConnection) continue;
            if (edge.to.Outgoing.Count == 0) continue;

            for (int lane = 0; lane < edge.Lanes.Count; lane++)
            {
                bool reachable = false;
                foreach (var outEdge in edge.to.Outgoing)
                    if (outEdge.EntryLaneRequired < 0 || outEdge.EntryLaneRequired == lane)
                    { reachable = true; break; }

                if (!reachable)
                {
                    sb.AppendLine(
                        $"  ⚠ [{edge.from.Label}→{edge.to.Label}] Lane {lane} " +
                        $"has NO reachable outgoing connection — vehicle will be stuck.");
                    clean = false;
                }
            }
        }

        if (clean) sb.AppendLine("  ✓ No issues found.");
        sb.AppendLine();

        // ---- Road segments ----
        sb.AppendLine("--- ROAD SEGMENTS ---");
        var roads = FindObjectsByType<Road>(FindObjectsSortMode.None);
        foreach (var road in roads)
        {
            sb.AppendLine($"  {road.name}  {road.RoadClass}  {road.LaneCount} lanes  {road.SpeedLimit}km/h  {road.Segments.Count} segment(s)");
            foreach (var seg in road.Segments)
            {
                if (seg == null) continue;
                float m     = seg.WorldLength * builder.metersPerUnit;
                string lt   = seg.HasTrafficLight ? " [LIGHT]" : "";
                sb.AppendLine($"    {seg.name}  {m:F1}m{lt}");
            }
        }

        sb.AppendLine();

        // ---- Connections ----
        sb.AppendLine("--- ROAD CONNECTIONS ---");
        var conns = FindObjectsByType<RoadConnection>(FindObjectsSortMode.None);
        if (conns.Length == 0)
            sb.AppendLine("  (none)");
        else
            foreach (var conn in conns)
                if (!conn.IsValid)
                    sb.AppendLine($"  ✗ {conn.name}  INVALID");
                else
                    sb.AppendLine(
                        $"  ✓ {conn.name}  " +
                        $"{conn.SourceRoad.name}[L{conn.SourceLane}] → " +
                        $"{conn.TargetRoad.name}[L{conn.TargetLane}]  " +
                        $"{conn.WorldLength * builder.metersPerUnit:F1}m");

        report = sb.ToString();
        built  = true;
        Debug.Log("[GraphDebugger]\n" + report);
        Repaint();
    }
}
#endif
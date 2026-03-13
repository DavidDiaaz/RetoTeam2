using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Coordinates N traffic lights through a sequence of phases so that
/// crossing streams are never simultaneously green.
///
/// Setup example — 4-way intersection:
///   Phase 0: NS green,  EW red    (greenDuration seconds)
///   Phase 0→1 all-red clearance   (clearanceDuration seconds)
///   Phase 1: EW green,  NS red    (greenDuration seconds)
///   Phase 1→0 all-red clearance
///
/// T-junction, roundabout entries etc. just add more phases.
///
/// Each entry in Phases lists which RoadSegment lights are GREEN in
/// that phase; all others are automatically Red. Yellow fires on the
/// light that is about to turn red, for yellowDuration seconds before
/// the clearance gap.
/// </summary>
public class TrafficLightGroup : MonoBehaviour
{
    // ---------------------------------------------------------------
    // Inspector
    // ---------------------------------------------------------------

    [Serializable]
    public class Phase
    {
        [Tooltip("How long this phase stays green (seconds)")]
        public float GreenDuration = 20f;

        [Tooltip("Road segments whose END node light is GREEN in this phase. " +
                 "All others in the group are Red.")]
        public RoadSegment[] GreenSegments;
    }

    [Header("Phases — cycle in order, wrap around")]
    public Phase[] Phases;

    [Header("Transition timing (shared)")]
    [Tooltip("Yellow warning before the green phase ends")]
    public float YellowDuration  = 3f;
    [Tooltip("All-red clearance between phases")]
    public float ClearanceDuration = 1f;

    [Header("All segments managed by this group")]
    [Tooltip("Every RoadSegment in the intersection. " +
             "Segments listed in a Phase.GreenSegments get Green; " +
             "all others get Red during that phase.")]
    public RoadSegment[] AllSegments;

    [Header("Prefab for spawning light views at each segment end")]
    public GameObject LightViewPrefab;

    [Header("Initial phase offset (0 = start at phase 0)")]
    public int StartPhase = 0;

    // ---------------------------------------------------------------
    // Runtime state
    // ---------------------------------------------------------------

    enum GroupState { Green, Yellow, Clearance }

    GroupState _groupState;
    int        _currentPhase;
    float      _timer;

    // Populated by NavGraphBuilder after the navigation graph is built.
    // Maps RoadSegment → the TrafficLight on its end node.
    Dictionary<RoadSegment, TrafficLight> _lights = new();

    // ---------------------------------------------------------------
    // Called by NavGraphBuilder after building the graph
    // ---------------------------------------------------------------
    public void RegisterLight(RoadSegment seg, TrafficLight light)
        => _lights[seg] = light;

    public void Initialise()
    {
        if (Phases == null || Phases.Length == 0)
        {
            Debug.LogWarning($"[TrafficLightGroup] '{name}' has no phases.");
            return;
        }

        _currentPhase = Mathf.Clamp(StartPhase, 0, Phases.Length - 1);
        _timer        = 0f;
        _groupState   = GroupState.Green;

        ApplyPhase(_currentPhase, GroupState.Green);
    }

    // ---------------------------------------------------------------
    // Called each tick from World (replaces individual light.Update)
    // ---------------------------------------------------------------
    public void Tick(float dt)
    {
        if (Phases == null || Phases.Length == 0) return;

        _timer += dt;
        var phase = Phases[_currentPhase];

        switch (_groupState)
        {
            case GroupState.Green:
                if (_timer >= phase.GreenDuration - YellowDuration)
                {
                    _groupState = GroupState.Yellow;
                    _timer      = 0f;
                    ApplyPhase(_currentPhase, GroupState.Yellow);
                }
                break;

            case GroupState.Yellow:
                if (_timer >= YellowDuration)
                {
                    _groupState = GroupState.Clearance;
                    _timer      = 0f;
                    ApplyAllRed();
                }
                break;

            case GroupState.Clearance:
                if (_timer >= ClearanceDuration)
                {
                    _currentPhase = (_currentPhase + 1) % Phases.Length;
                    _groupState   = GroupState.Green;
                    _timer        = 0f;
                    ApplyPhase(_currentPhase, GroupState.Green);
                }
                break;
        }
    }

    // ---------------------------------------------------------------
    void ApplyPhase(int phaseIndex, GroupState gs)
    {
        var phase = Phases[phaseIndex];

        // Build a fast lookup of green segments for this phase
        var greenSet = new HashSet<RoadSegment>();
        if (phase.GreenSegments != null)
            foreach (var seg in phase.GreenSegments)
                if (seg != null) greenSet.Add(seg);

        foreach (var seg in AllSegments)
        {
            if (seg == null || !_lights.TryGetValue(seg, out var light)) continue;

            if (greenSet.Contains(seg))
            {
                light.CurrentState = gs == GroupState.Yellow
                    ? TrafficLight.State.Yellow
                    : TrafficLight.State.Green;
            }
            else
            {
                light.CurrentState = TrafficLight.State.Red;
            }
        }
    }

    void ApplyAllRed()
    {
        foreach (var seg in AllSegments)
        {
            if (seg == null || !_lights.TryGetValue(seg, out var light)) continue;
            light.CurrentState = TrafficLight.State.Red;
        }
    }

    // ---------------------------------------------------------------
    // Editor helper
    // ---------------------------------------------------------------
#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (AllSegments == null) return;
        foreach (var seg in AllSegments)
        {
            if (seg == null) continue;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(seg.LaneEndPosition(0) + Vector3.up * 0.5f, 0.4f);
            UnityEditor.Handles.Label(
                seg.LaneEndPosition(0) + Vector3.up * 1.2f,
                seg.name);
        }
    }
#endif
}
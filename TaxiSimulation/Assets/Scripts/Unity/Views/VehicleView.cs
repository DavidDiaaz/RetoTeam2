using UnityEngine;

public class VehicleView : MonoBehaviour
{
    public VehicleAgent Agent;
    public WorldView    WorldView;

    Lane    lastLane;
    TrafficEdge lastEdge;

    Vector3 laneChangeFromPos;
    float   laneChangeTimer   = 0f;
    const float LaneChangeDuration = 0.3f;

    void Update()
    {
        if (Agent == null || WorldView == null) return;

        var laneView = WorldView.GetLaneView(Agent.CurrentLane);
        if (laneView == null) return;

        float   edgeLength = Agent.CurrentLane.Edge.Length;
        Vector3 targetPos  = laneView.Evaluate(Agent.Position, edgeLength);
        Vector3 tangent    = laneView.TangentAt(Agent.Position, edgeLength);

        bool edgeChanged = Agent.CurrentLane.Edge != lastEdge;
        bool laneChanged = Agent.CurrentLane != lastLane;

        if (laneChanged)
        {
            if (edgeChanged)
            {
                // Edge transition — snap directly, no lerp
                // (vehicle moved to next road segment, not a lateral change)
                laneChangeTimer = 0f;
            }
            else
            {
                // Lateral lane change on same edge — smooth lerp
                laneChangeFromPos = transform.position;
                laneChangeTimer   = LaneChangeDuration;
            }
        }

        lastLane = Agent.CurrentLane;
        lastEdge = Agent.CurrentLane.Edge;

        // Apply position
        Vector3 finalPos;
        if (laneChangeTimer > 0f)
        {
            laneChangeTimer -= Time.deltaTime;
            float t  = 1f - Mathf.Clamp01(laneChangeTimer / LaneChangeDuration);
            finalPos = Vector3.Lerp(laneChangeFromPos, targetPos, t);
        }
        else
        {
            finalPos = targetPos;
        }

        transform.position = finalPos;

        // Face direction of travel
        if (tangent.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
    }
}
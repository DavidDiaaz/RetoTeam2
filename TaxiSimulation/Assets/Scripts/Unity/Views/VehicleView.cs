using UnityEngine;

public class VehicleView : MonoBehaviour
{
    public VehicleAgent Agent;
    public WorldView    WorldView;

    /// <summary>
    /// Where the prefab's pivot sits along the car's length.
    /// 0 = pivot at the FRONT,  0.5 = pivot at the CENTER (default),  1 = pivot at the REAR.
    /// Adjust this in the inspector if cars visually overlap or float ahead/behind.
    /// </summary>
    [Range(0f, 1f)]
    public float PivotFractionFromFront = 0.5f;

    /// <summary>
    /// How fast the rendered position tracks the simulation position.
    /// Higher = snappier, lower = smoother but laggier.
    /// 30-50 is good for most frame rates; increase if cars feel floaty.
    /// </summary>
    [SerializeField] float _positionTrackSpeed = 40f;

    // Keep the last rendered world position so we can smooth out one-tick
    // connector traversals (car enters and exits a short connector in the
    // same simulation tick — the view never sees the intermediate state).
    Vector3 _lastPos;
    bool    _hasPrev;
    bool    _firstFrame = true;

    // When a lane change completes, hold the blended position for one frame
    // so there is no snap on the tick CurrentLane switches to the dest lane.
    Vector3 _laneChangeFinalPos;
    Vector3 _laneChangeFinalTan;
    bool    _wasChangingLane;

    void Update()
    {
        if (Agent == null || WorldView == null) return;

        float   edgeLength = Agent.CurrentLane.Edge.Length;
        Vector3 finalPos;
        Vector3 tangent;

        // Agent.Position is the car's FRONT. Offset by PivotFractionFromFront so the
        // model pivot lands at the correct logical position along the lane.
        // Default 0.5 = centre pivot: renders at [Position-Length, Position].
        float centrePos = Mathf.Clamp(Agent.Position - Agent.Length * PivotFractionFromFront,
                                      0f, edgeLength);

        if (Agent.IsChangingLane)
        {
            var originView = WorldView.GetLaneView(Agent.LaneChangeOrigin);
            var destView   = WorldView.GetLaneView(Agent.LaneChangeDest);

            if (originView != null && destView != null)
            {
                Vector3 originPos = originView.Evaluate(centrePos, edgeLength);
                Vector3 destPos   = destView  .Evaluate(centrePos, edgeLength);
                finalPos = Vector3.Lerp(originPos, destPos, Agent.LaneChangeProgress);

                Vector3 originTan = originView.TangentAt(centrePos, edgeLength);
                Vector3 destTan   = destView  .TangentAt(centrePos, edgeLength);
                tangent = Vector3.Slerp(originTan, destTan, Agent.LaneChangeProgress);
            }
            else
            {
                var view = WorldView.GetLaneView(Agent.CurrentLane);
                if (view == null) return;
                finalPos = view.Evaluate(centrePos, edgeLength);
                tangent  = view.TangentAt(centrePos, edgeLength);
            }

            // Remember the blended position so the frame after completion
            // can hold it rather than snapping to the dest lane position.
            _laneChangeFinalPos = finalPos;
            _laneChangeFinalTan = tangent;
            _wasChangingLane    = true;
        }
        else
        {
            // One-frame holdover: lane change just completed this tick.
            // Use the last blended position so there is no snap.
            if (_wasChangingLane)
            {
                finalPos         = _laneChangeFinalPos;
                tangent          = _laneChangeFinalTan;
                _wasChangingLane = false;
            }
            else
            {
                var laneView = WorldView.GetLaneView(Agent.CurrentLane);
                if (laneView == null) return;

                finalPos = laneView.Evaluate(centrePos, edgeLength);
                tangent  = laneView.TangentAt(centrePos, edgeLength);

                // Short connector or lane transition: smooth large jumps so
                // the car doesn't visually teleport between segments.
                if (_hasPrev)
                {
                    float maxJump = Mathf.Max(Agent.Speed * Time.deltaTime * 6f, 2f);
                    if (Vector3.Distance(_lastPos, finalPos) > maxJump)
                        finalPos = Vector3.MoveTowards(_lastPos, finalPos, maxJump);
                }
            }
        }

        _lastPos = finalPos;
        _hasPrev = true;

        // Lerp the visual position toward the simulation target every Unity frame.
        // This smooths out connector transitions, lane changes, and any tick-to-frame
        // timing differences — eliminating the teleport effect.
        float t = Mathf.Min(1f, _positionTrackSpeed * Time.deltaTime);
        transform.position = _firstFrame
            ? finalPos
            : Vector3.Lerp(transform.position, finalPos, t);
        _firstFrame = false;

        if (tangent.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
    }
}
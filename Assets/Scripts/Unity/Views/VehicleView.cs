using UnityEngine;

public class VehicleView : MonoBehaviour
{
    public VehicleAgent Agent;
    public WorldView    WorldView;

    // Keep the last rendered world position so we can smooth out one-tick
    // connector traversals (car enters and exits a short connector in the
    // same simulation tick — the view never sees the intermediate state).
    Vector3    _lastPos;
    bool       _hasPrev;
    Quaternion _rotation = Quaternion.identity;

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

        if (Agent.IsChangingLane)
        {
            var originView = WorldView.GetLaneView(Agent.LaneChangeOrigin);
            var destView   = WorldView.GetLaneView(Agent.LaneChangeDest);

            if (originView != null && destView != null)
            {
                Vector3 originPos = originView.Evaluate(Agent.Position, edgeLength);
                Vector3 destPos   = destView  .Evaluate(Agent.Position, edgeLength);
                finalPos = Vector3.Lerp(originPos, destPos, Agent.LaneChangeProgress);

                Vector3 originTan = originView.TangentAt(Agent.Position, edgeLength);
                Vector3 destTan   = destView  .TangentAt(Agent.Position, edgeLength);
                tangent = Vector3.Slerp(originTan, destTan, Agent.LaneChangeProgress);
            }
            else
            {
                var view = WorldView.GetLaneView(Agent.CurrentLane);
                if (view == null) return;
                finalPos = view.Evaluate(Agent.Position, edgeLength);
                tangent  = view.TangentAt(Agent.Position, edgeLength);
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

                finalPos = laneView.Evaluate(Agent.Position, edgeLength);
                tangent  = laneView.TangentAt(Agent.Position, edgeLength);

                // Short connector traversed in one tick: the car exits the
                // connector and enters the next segment in a single Act() call,
                // so this frame is the first time the view sees the new position.
                // If the jump is large relative to the speed, smooth it.
                if (_hasPrev)
                {
                    float maxJump = Mathf.Max(Agent.Speed * Time.deltaTime * 2f, 0.5f);
                    if (Vector3.Distance(_lastPos, finalPos) > maxJump)
                        finalPos = Vector3.MoveTowards(_lastPos, finalPos, maxJump);
                }
            }
        }

        _lastPos = finalPos;
        _hasPrev = true;

        transform.position = finalPos;
        if (tangent.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(tangent, Vector3.up);
            _rotation = Quaternion.Slerp(_rotation, targetRot, Time.deltaTime * 8f);
            transform.rotation = _rotation;
        }
    }
}
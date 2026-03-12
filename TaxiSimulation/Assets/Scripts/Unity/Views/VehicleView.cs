using UnityEngine;

/// <summary>
/// Drives the visual representation of a VehicleAgent:
///   • Position / rotation along the lane spline
///   • Wheel spin + front axle steering
///   • Rear lights — always on as dim tail lights, brighten to full red when braking
///   • Turn signals — blink correct side during lane changes and junction turns
///
/// Prefab structure (all slots optional):
///
///   VehicleRoot  ← this component
///     Body             (mesh)
///     RearWheels       Transform — spins, no steer
///     FrontLeftWheel   Transform — spins + steers
///     FrontRightWheel  Transform — spins + steers
///     RearLights       Renderer   — emission color driven by code
///     TurnSignalLeft   GameObject — SetActive blink
///     TurnSignalRight  GameObject
/// </summary>
public class VehicleView : MonoBehaviour
{
    public VehicleAgent Agent;
    public WorldView    WorldView;

    // ---------------------------------------------------------------
    // Wheels
    // ---------------------------------------------------------------
    [Header("Wheels")]
    [Tooltip("Single rear axle object — spins only, no steer")]
    public Transform RearWheels;
    public Transform FrontLeftWheel;
    public Transform FrontRightWheel;

    [Tooltip("Wheel radius in world units")]
    public float WheelRadius   = 0.33f;
    [Tooltip("Max steering angle on front axle (degrees)")]
    public float MaxSteerAngle = 30f;

    // ---------------------------------------------------------------
    // Rear lights — single renderer, HDR color changes only
    // ---------------------------------------------------------------
    [Header("Rear Lights")]
    [Tooltip("Material name to search for on child renderers (case-insensitive). " +
             "Leave empty to match any material with Emission enabled.)")]
    public string RearLightMaterialName = "TailLights_Instance";

    [Tooltip("Dim HDR color — tail lights always on")]
    public Color TailLightColor  = new Color(0.6f,  0.02f, 0.02f) * 0.4f;
    [Tooltip("Bright HDR color — brake lights")]
    public Color BrakeLightColor = new Color(1.0f,  0.05f, 0.05f) * 3.0f;

    // ---------------------------------------------------------------
    // Turn signals
    // ---------------------------------------------------------------
    [Header("Turn Signals")]
    public GameObject TurnSignalLeft;
    public GameObject TurnSignalRight;
    public float      BlinkInterval = 0.4f;

    // ---------------------------------------------------------------
    // Internal — position smoothing (unchanged from previous version)
    // ---------------------------------------------------------------
    Vector3 _lastPos;
    bool    _hasPrev;
    Vector3 _laneChangeFinalPos;
    Vector3 _laneChangeFinalTan;
    bool    _wasChangingLane;

    // ---------------------------------------------------------------
    // Internal — wheels
    // ---------------------------------------------------------------
    float _wheelRollDeg;
    float _steerAngle;

    // ---------------------------------------------------------------
    // Internal — rear lights
    // ---------------------------------------------------------------
    float        _prevSpeed;
    bool         _isBraking;
    MaterialPropertyBlock _mpb;
    Renderer[] _rearRenderers = new Renderer[0];
    int[]      _rearMatIndices;
    float      _modelLength;   // measured from bounds in Start()

    // ---------------------------------------------------------------
    // Internal — turn signals
    // ---------------------------------------------------------------
    enum SignalSide { None, Left, Right }
    SignalSide _signalSide   = SignalSide.None;
    float      _blinkTimer   = 0f;
    bool       _blinkVisible = false;

    // ---------------------------------------------------------------
    void Awake()
    {
        _mpb = new MaterialPropertyBlock();
    }

    void Start()
    {
        MeasureModelLength();
        FindRearRenderers();
        SetRearLightColor(TailLightColor);
    }

    void MeasureModelLength()
    {
        // Temporarily reset scale to (1,1,1) so bounds reflect the raw model size,
        // then restore. This way any existing prefab scale doesn't throw off the measurement.
        Vector3 prevScale = transform.localScale;
        transform.localScale = Vector3.one;

        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            _modelLength = Agent != null ? Agent.Length : 4.5f;
            transform.localScale = prevScale;
            return;
        }

        // Encapsulate all child renderer bounds
        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            combined.Encapsulate(renderers[i].bounds);

        // Use the largest horizontal extent as the "length"
        // (handles cars that face X or Z)
        _modelLength = Mathf.Max(combined.size.x, combined.size.z);

        if (_modelLength < 0.01f)
            _modelLength = Agent != null ? Agent.Length : 4.5f;

        transform.localScale = prevScale;
    }

    void FindRearRenderers()
    {
        var found       = new System.Collections.Generic.List<Renderer>();
        var foundIdx    = new System.Collections.Generic.List<int>();
        string search   = RearLightMaterialName.ToLower();

        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            for (int i = 0; i < r.sharedMaterials.Length; i++)
            {
                var mat = r.sharedMaterials[i];
                if (mat == null) continue;
                if (string.IsNullOrEmpty(search) || mat.name.ToLower().Contains(search))
                {
                    found.Add(r);
                    foundIdx.Add(i);
                    break;
                }
            }
        }

        _rearRenderers  = found.ToArray();
        _rearMatIndices = foundIdx.ToArray();

        if (_rearRenderers.Length == 0)
            UnityEngine.Debug.LogWarning(
                $"[VehicleView] No renderer found with material containing " +
                $"\"{RearLightMaterialName}\" on {gameObject.name}");
    }

    // ---------------------------------------------------------------
    void Update()
    {
        if (Agent == null || WorldView == null) return;

        ComputePosAndTangent(out Vector3 finalPos, out Vector3 tangent);

        _lastPos = finalPos;
        _hasPrev = true;

        transform.position = finalPos;
        if (tangent.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);

        // Scale so the prefab's actual measured length matches Agent.Length.
        // _modelLength is measured once in Start() from the combined renderer bounds.
        if (_modelLength > 0f)
        {
            float s = Agent.Length / _modelLength;
            transform.localScale = new Vector3(s, s, s);
        }

        UpdateWheels(tangent);
        UpdateRearLights();
        UpdateTurnSignals(tangent);
    }

    // ================================================================
    // Position / rotation (unchanged logic)
    // ================================================================
    void ComputePosAndTangent(out Vector3 finalPos, out Vector3 tangent)
    {
        float edgeLength = Agent.CurrentLane.Edge.Length;

        if (Agent.IsChangingLane)
        {
            var ov = WorldView.GetLaneView(Agent.LaneChangeOrigin);
            var dv = WorldView.GetLaneView(Agent.LaneChangeDest);

            if (ov != null && dv != null)
            {
                finalPos = Vector3.Lerp(
                    ov.Evaluate(Agent.Position, edgeLength),
                    dv.Evaluate(Agent.Position, edgeLength),
                    Agent.LaneChangeProgress);

                tangent = Vector3.Slerp(
                    ov.TangentAt(Agent.Position, edgeLength),
                    dv.TangentAt(Agent.Position, edgeLength),
                    Agent.LaneChangeProgress);
            }
            else
            {
                var v = WorldView.GetLaneView(Agent.CurrentLane);
                if (v == null) { finalPos = transform.position; tangent = transform.forward; return; }
                finalPos = v.Evaluate(Agent.Position, edgeLength);
                tangent  = v.TangentAt(Agent.Position, edgeLength);
            }

            _laneChangeFinalPos = finalPos;
            _laneChangeFinalTan = tangent;
            _wasChangingLane    = true;
        }
        else
        {
            if (_wasChangingLane)
            {
                finalPos         = _laneChangeFinalPos;
                tangent          = _laneChangeFinalTan;
                _wasChangingLane = false;
            }
            else
            {
                var lv = WorldView.GetLaneView(Agent.CurrentLane);
                if (lv == null) { finalPos = transform.position; tangent = transform.forward; return; }

                finalPos = lv.Evaluate(Agent.Position, edgeLength);
                tangent  = lv.TangentAt(Agent.Position, edgeLength);

                if (_hasPrev)
                {
                    float maxJump = Mathf.Max(Agent.Speed * Time.deltaTime * 4f, 1f);
                    if (Vector3.Distance(_lastPos, finalPos) > maxJump)
                        finalPos = Vector3.MoveTowards(_lastPos, finalPos, maxJump);
                }
            }
        }
    }

    // ================================================================
    // Wheels
    // ================================================================
    void UpdateWheels(Vector3 forwardTangent)
    {
        _wheelRollDeg += (Agent.Speed * Time.deltaTime / (2f * Mathf.PI * WheelRadius)) * 360f;

        float targetSteer = 0f;
        if (_hasPrev && forwardTangent.sqrMagnitude > 0.001f)
        {
            float yawDelta = Vector3.SignedAngle(
                new Vector3(transform.forward.x, 0, transform.forward.z),
                new Vector3(forwardTangent.x,    0, forwardTangent.z),
                Vector3.up);

            // Clamp raw yaw before scaling — connector/lane-change tangent
            // jumps can be 90°+ in one frame which would spike the steer angle.
            yawDelta = Mathf.Clamp(yawDelta, -45f, 45f);

            // Scale: gentle at speed, firmer at low speed
            float speedFactor = Mathf.Clamp(Agent.Speed / 8f, 0.1f, 1f);
            targetSteer = Mathf.Clamp(yawDelta * speedFactor * 0.6f,
                                      -MaxSteerAngle, MaxSteerAngle);
        }
        // Slow lerp so wheels don't snap — 3f feels physical
        _steerAngle = Mathf.LerpAngle(_steerAngle, targetSteer, Time.deltaTime * 3f);

        ApplyWheel(FrontLeftWheel,  _wheelRollDeg, _steerAngle);
        ApplyWheel(FrontRightWheel, _wheelRollDeg, _steerAngle);
        ApplyWheel(RearWheels,      _wheelRollDeg, 0f);
    }

    void ApplyWheel(Transform wheel, float rollDeg, float steerDeg)
    {
        if (wheel == null) return;
        // Compose: steer around Y first, then spin around X.
        // Doing Euler(roll, steer, 0) bakes both into a single rotation
        // which causes them to interfere when both are non-zero.
        Quaternion steer = Quaternion.AngleAxis(steerDeg, Vector3.up);
        Quaternion spin  = Quaternion.AngleAxis(rollDeg,  Vector3.right);
        wheel.localRotation = steer * spin;
    }

    // ================================================================
    // Rear lights — single renderer, color-only change via MPB
    // ================================================================
    void UpdateRearLights()
    {
        // Brake lights on when actively decelerating only.
        // Stopped-but-not-decelerating stays at tail light level so
        // cars idling at junctions don't permanently blaze bright red.
        bool shouldBrake = Agent.Speed < _prevSpeed - 0.05f;

        if (shouldBrake != _isBraking)
        {
            _isBraking = shouldBrake;
            SetRearLightColor(_isBraking ? BrakeLightColor : TailLightColor);
        }

        _prevSpeed = Agent.Speed;
    }

    void SetRearLightColor(Color color)
    {
        _mpb.SetColor("_EmissionColor", color);
        for (int i = 0; i < _rearRenderers.Length; i++)
            _rearRenderers[i].SetPropertyBlock(_mpb);
    }

    // ================================================================
    // Turn signals
    // ================================================================
    void UpdateTurnSignals(Vector3 forwardTangent)
    {
        SignalSide desired = DetermineSignalSide(forwardTangent);

        if (desired != _signalSide)
        {
            _signalSide   = desired;
            _blinkTimer   = 0f;
            _blinkVisible = desired != SignalSide.None;
            ApplyBlink();
        }

        if (_signalSide == SignalSide.None)
        {
            SetActive(TurnSignalLeft,  false);
            SetActive(TurnSignalRight, false);
            return;
        }

        _blinkTimer += Time.deltaTime;
        if (_blinkTimer >= BlinkInterval)
        {
            _blinkTimer   = 0f;
            _blinkVisible = !_blinkVisible;
            ApplyBlink();
        }
    }

    SignalSide DetermineSignalSide(Vector3 forwardTangent)
    {
        // Lane change in progress — side from lane number delta
        if (Agent.IsChangingLane &&
            Agent.LaneChangeOrigin != null &&
            Agent.LaneChangeDest   != null)
        {
            return Agent.LaneChangeDest.LaneNumber < Agent.LaneChangeOrigin.LaneNumber
                ? SignalSide.Left
                : SignalSide.Right;
        }

        // Approaching a non-straight junction — angle to destination lane
        if (Agent.TargetLink != null && !Agent.TargetLink.IsStraight)
        {
            var destView = WorldView.GetLaneView(Agent.TargetLink.DestLane);
            if (destView?.Waypoints != null && destView.Waypoints.Length >= 2)
            {
                Vector3 destDir = (destView.Waypoints[1] - destView.Waypoints[0]).normalized;
                float   angle   = Vector3.SignedAngle(
                    new Vector3(forwardTangent.x, 0, forwardTangent.z),
                    new Vector3(destDir.x,        0, destDir.z),
                    Vector3.up);

                if (angle >  15f) return SignalSide.Right;
                if (angle < -15f) return SignalSide.Left;
            }
        }

        return SignalSide.None;
    }

    void ApplyBlink()
    {
        SetActive(TurnSignalLeft,  _signalSide == SignalSide.Left  && _blinkVisible);
        SetActive(TurnSignalRight, _signalSide == SignalSide.Right && _blinkVisible);
    }

    static void SetActive(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }
}
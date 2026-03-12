using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Attach to the Main Camera.
/// FollowCamHUD (same GameObject) builds the UI and calls Init().
///
/// Controls (Follow mode):
///   Left / Right arrow keys  — previous / next vehicle / pedestrian
///   A / D                    — same as arrows
///   Tab                      — cycle filter (All → Taxis → Cars → Peds)
///   F                        — toggle Free Camera mode
///
/// Controls (Free Camera mode):
///   W / A / S / D / Q / E   — move
///   Right-click + drag       — look around
///   Shift                    — move faster
///   F                        — back to Follow mode
/// </summary>
public class CameraFollowController : MonoBehaviour
{
    [Header("Camera follow")]
    public float followDistance  = 8f;
    public float followHeight    = 3f;
    public float followSmoothing = 6f;

    [Header("Free camera")]
    public float freeCamSpeed     = 10f;
    public float freeCamFastMult  = 3f;
    public float freeCamSensitivity = 3f;

    [Header("Blackout transition")]
    public float fadeDuration = 0.4f;

    // Set by FollowCamHUD.Init() — not assigned in inspector
    [HideInInspector] public CanvasGroup hudGroup;
    [HideInInspector] public CanvasGroup blackoutGroup;
    [HideInInspector] public TMP_Text    labelText;
    [HideInInspector] public TMP_Text    filterLabel;
    [HideInInspector] public Button      filterButton;
    [HideInInspector] public Button      prevButton;
    [HideInInspector] public Button      nextButton;
    [HideInInspector] public Button      freeCamButton;
    [HideInInspector] public TMP_Text    freeCamLabel;
    [HideInInspector] public TMP_Text    taxiStatusText;
    [HideInInspector] public GameObject  taxiStatusPanel;

    // ----------------------------------------------------------------
    public enum FilterMode { All, TaxisOnly, CarsOnly, PedestriansOnly }

    FilterMode _filter        = FilterMode.All;
    int        _index         = 0;
    bool       _transitioning = false;
    bool       _snapNext      = false;

    // Vehicle tracking
    readonly List<VehicleAgent>                  _all        = new();
    readonly List<VehicleAgent>                  _filtered   = new();
    readonly Dictionary<VehicleAgent, Transform> _transforms = new();

    // Pedestrian tracking
    readonly List<Pedestrian>                  _allPeds       = new();
    readonly List<Pedestrian>                  _filteredPeds  = new();
    readonly Dictionary<Pedestrian, Transform> _pedTransforms = new();

    Transform _target;

    // Free camera state
    bool  _freeCam    = false;
    float _freeCamYaw;
    float _freeCamPitch;
    bool  _mouseHeld  = false;

    // ----------------------------------------------------------------
    // Called by FollowCamHUD after wiring all references
    // ----------------------------------------------------------------
    public void Init()
    {
        prevButton   ?.onClick.AddListener(() => RequestCycle(-1));
        nextButton   ?.onClick.AddListener(() => RequestCycle(+1));
        filterButton ?.onClick.AddListener(RequestFilterCycle);
        freeCamButton?.onClick.AddListener(ToggleFreeCam);

        if (blackoutGroup != null)
        {
            blackoutGroup.alpha          = 0f;
            blackoutGroup.blocksRaycasts = false;
        }

        UpdateLabel();
        UpdateFilterLabel();
        UpdateFreeCamLabel();
        UpdateTaxiStatusPanel();
    }

    // ----------------------------------------------------------------
    // Registration — called by SimulationManager
    // ----------------------------------------------------------------
    public void RegisterVehicle(VehicleAgent agent, GameObject go)
    {
        if (_all.Contains(agent)) return;
        _all.Add(agent);
        _transforms[agent] = go.transform;
        RebuildList();

        if (_target == null) ApplyTarget();
    }

    public void UnregisterVehicle(VehicleAgent agent)
    {
        _all.Remove(agent);
        _transforms.Remove(agent);
        RebuildList();
    }

    public void RegisterPedestrian(Pedestrian p, GameObject go)
    {
        if (_allPeds.Contains(p)) return;
        _allPeds.Add(p);
        _pedTransforms[p] = go.transform;
        if (_filter == FilterMode.PedestriansOnly) RebuildList();
    }

    public void UnregisterPedestrian(Pedestrian p)
    {
        _allPeds.Remove(p);
        _pedTransforms.Remove(p);
        if (_filter == FilterMode.PedestriansOnly) RebuildList();
    }

    // ----------------------------------------------------------------
    void Update()
    {
        if (_freeCam)
        {
            HandleFreeCam();
            return;
        }

        if (_transitioning) return;

        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
            RequestCycle(-1);

        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            RequestCycle(+1);

        if (Input.GetKeyDown(KeyCode.Tab))
            RequestFilterCycle();

        if (Input.GetKeyDown(KeyCode.F))
            ToggleFreeCam();

        UpdateTaxiStatusPanel();
    }

    void HandleFreeCam()
    {
        // Mouse look — right button
        if (Input.GetMouseButtonDown(1)) _mouseHeld = true;
        if (Input.GetMouseButtonUp(1))   _mouseHeld = false;

        if (_mouseHeld)
        {
            _freeCamYaw   += Input.GetAxis("Mouse X") * freeCamSensitivity;
            _freeCamPitch -= Input.GetAxis("Mouse Y") * freeCamSensitivity;
            _freeCamPitch  = Mathf.Clamp(_freeCamPitch, -80f, 80f);
            transform.rotation = Quaternion.Euler(_freeCamPitch, _freeCamYaw, 0f);
        }

        if (Input.GetKeyDown(KeyCode.F))
            ToggleFreeCam();

        float speed = freeCamSpeed * (Input.GetKey(KeyCode.LeftShift) ? freeCamFastMult : 1f);
        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += transform.forward;
        if (Input.GetKey(KeyCode.S)) move -= transform.forward;
        if (Input.GetKey(KeyCode.A)) move -= transform.right;
        if (Input.GetKey(KeyCode.D)) move += transform.right;
        if (Input.GetKey(KeyCode.E)) move += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
        transform.position += move * speed * Time.deltaTime;
    }

    // ----------------------------------------------------------------
    void LateUpdate()
    {
        if (_freeCam) return;
        if (_target == null) return;

        Vector3    behind  = _target.position - _target.forward * followDistance + Vector3.up * followHeight;
        Vector3    lookAt  = _target.position + Vector3.up * 0.5f;
        Quaternion lookRot = Quaternion.LookRotation(lookAt - behind, Vector3.up);

        if (_snapNext)
        {
            transform.SetPositionAndRotation(behind, lookRot);
            _snapNext = false;
        }
        else
        {
            float t = followSmoothing * Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, behind, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, t);
        }
    }

    // ----------------------------------------------------------------
    void RequestCycle(int dir)
    {
        int count = CurrentFilteredCount();
        if (count <= 1 || _transitioning) return;
        int next = (_index + dir + count) % count;
        StartCoroutine(TransitionTo(next));
    }

    void RequestFilterCycle()
    {
        if (_transitioning) return;
        _filter = _filter switch
        {
            FilterMode.All             => FilterMode.TaxisOnly,
            FilterMode.TaxisOnly       => FilterMode.CarsOnly,
            FilterMode.CarsOnly        => FilterMode.PedestriansOnly,
            _                          => FilterMode.All
        };
        _index = 0;
        RebuildList();
        UpdateFilterLabel();
        if (CurrentFilteredCount() > 0)
            StartCoroutine(TransitionTo(0));
    }

    public void ToggleFreeCam()
    {
        _freeCam = !_freeCam;
        if (_freeCam)
        {
            _freeCamYaw   = transform.eulerAngles.y;
            _freeCamPitch = transform.eulerAngles.x;
            _mouseHeld    = false;
        }
        UpdateFreeCamLabel();
        UpdateTaxiStatusPanel();
    }

    // ----------------------------------------------------------------
    IEnumerator TransitionTo(int newIndex)
    {
        _transitioning = true;

        yield return StartCoroutine(Fade(0f, 1f));

        _index    = newIndex;
        _snapNext = true;
        ApplyTarget();
        UpdateLabel();

        yield return new WaitForSeconds(0.05f);

        yield return StartCoroutine(Fade(1f, 0f));

        _transitioning = false;
    }

    IEnumerator Fade(float from, float to)
    {
        if (blackoutGroup == null) yield break;

        blackoutGroup.blocksRaycasts = true;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            blackoutGroup.alpha = Mathf.Lerp(from, to, elapsed / fadeDuration);
            yield return null;
        }

        blackoutGroup.alpha = to;
        if (to <= 0f) blackoutGroup.blocksRaycasts = false;
    }

    // ----------------------------------------------------------------
    void RebuildList()
    {
        if (_filter == FilterMode.PedestriansOnly)
        {
            _filteredPeds.Clear();
            foreach (var p in _allPeds)
                if (p.State != PedestrianState.Done && p.State != PedestrianState.Cancelled)
                    _filteredPeds.Add(p);

            _index = Mathf.Clamp(_index, 0, Mathf.Max(0, _filteredPeds.Count - 1));
            ApplyTarget();
            UpdateLabel();
            return;
        }

        _filtered.Clear();
        foreach (var v in _all)
        {
            bool isTaxi = v is AutonomousTaxi;
            bool keep   = _filter switch
            {
                FilterMode.TaxisOnly => isTaxi,
                FilterMode.CarsOnly  => !isTaxi,
                _                    => true
            };
            if (keep) _filtered.Add(v);
        }

        _index = Mathf.Clamp(_index, 0, Mathf.Max(0, _filtered.Count - 1));
        ApplyTarget();
        UpdateLabel();
    }

    void ApplyTarget()
    {
        if (_filter == FilterMode.PedestriansOnly)
        {
            if (_filteredPeds.Count == 0) { _target = null; return; }
            _index = Mathf.Clamp(_index, 0, _filteredPeds.Count - 1);
            _pedTransforms.TryGetValue(_filteredPeds[_index], out _target);
            return;
        }

        if (_filtered.Count == 0) { _target = null; return; }
        _index = Mathf.Clamp(_index, 0, _filtered.Count - 1);
        _transforms.TryGetValue(_filtered[_index], out _target);
    }

    int CurrentFilteredCount() =>
        _filter == FilterMode.PedestriansOnly ? _filteredPeds.Count : _filtered.Count;

    /// <summary>Returns the currently selected AutonomousTaxi, or null.</summary>
    public AutonomousTaxi CurrentTaxi =>
        (!_freeCam && _filter != FilterMode.PedestriansOnly && _filtered.Count > 0)
            ? _filtered[_index] as AutonomousTaxi
            : null;

    // ----------------------------------------------------------------
    void UpdateLabel()
    {
        if (labelText == null) return;

        if (_filter == FilterMode.PedestriansOnly)
        {
            if (_filteredPeds.Count == 0) { labelText.text = "No peds"; return; }
            var p = _filteredPeds[_index];
            string st = p.State switch
            {
                PedestrianState.Waiting  => "Esperando",
                PedestrianState.Matched  => "Asignado",
                PedestrianState.Riding   => "En taxi",
                _                        => p.State.ToString()
            };
            labelText.text = $"Ped {_index + 1}/{_filteredPeds.Count}  {st}";
            return;
        }

        if (_filtered.Count == 0) { labelText.text = "No vehicles"; return; }
        string type = _filtered[_index] is AutonomousTaxi ? "Taxi" : "Car";
        labelText.text = $"{type}  {_index + 1} / {_filtered.Count}";
    }

    void UpdateFilterLabel()
    {
        if (filterLabel == null) return;
        filterLabel.text = _filter switch
        {
            FilterMode.TaxisOnly       => "Taxis",
            FilterMode.CarsOnly        => "Cars",
            FilterMode.PedestriansOnly => "Peds",
            _                          => "All"
        };
    }

    void UpdateFreeCamLabel()
    {
        if (freeCamLabel != null)
            freeCamLabel.text = _freeCam ? "Follow" : "Free";
    }

    void UpdateTaxiStatusPanel()
    {
        if (taxiStatusPanel == null) return;

        bool isTaxiSelected = !_freeCam
            && _filter != FilterMode.PedestriansOnly
            && _filtered.Count > 0
            && _filtered[_index] is AutonomousTaxi;

        taxiStatusPanel.SetActive(isTaxiSelected);

        if (!isTaxiSelected || taxiStatusText == null) return;

        var taxi = (AutonomousTaxi)_filtered[_index];

        string dest = "";
        if (taxi.State == TaxiState.Carrying && taxi.DropoffNodeTarget != null)
        {
            var n = taxi.DropoffNodeTarget;
            dest = " → " + (string.IsNullOrEmpty(n.Label) ? $"#{n.id}" : n.Label);
        }
        else if (taxi.State == TaxiState.EnRouteToPickup && taxi.PickupNodeTarget != null)
        {
            var n = taxi.PickupNodeTarget;
            dest = " → " + (string.IsNullOrEmpty(n.Label) ? $"#{n.id}" : n.Label);
        }

        string distStr = "";
        if (taxi.State != TaxiState.Idle)
            distStr = "\n" + FormatDistance(taxi.EstimatedDistanceRemaining);

        taxiStatusText.text = taxi.State switch
        {
            TaxiState.Idle             => "Disponible",
            TaxiState.EnRouteToPickup  => $"En camino al pasajero{dest}{distStr}",
            TaxiState.Carrying         => $"En viaje{dest}{distStr}",
            _                          => ""
        };
    }

    static string FormatDistance(float meters)
    {
        if (meters >= 1000f) return $"{meters / 1000f:F1} km";
        if (meters >= 10f)   return $"{Mathf.RoundToInt(meters)} m";
        return "< 10 m";
    }
}

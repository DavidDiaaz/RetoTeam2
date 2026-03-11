using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Attach to the Main Camera.
/// FollowCamHUD (same GameObject) builds the UI and calls Init().
///
/// Controls:
///   Left / Right arrow keys  — previous / next vehicle
///   Tab                      — cycle filter (All → Taxis → Cars)
///   UI buttons do the same things
/// </summary>
public class CameraFollowController : MonoBehaviour
{
    [Header("Camera follow")]
    public float followDistance = 8f;
    public float followHeight   = 3f;
    public float followSmoothing = 6f;

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

    // ----------------------------------------------------------------
    public enum FilterMode { All, TaxisOnly, CarsOnly }

    FilterMode _filter       = FilterMode.All;
    int        _index        = 0;
    bool       _transitioning = false;
    bool       _snapNext      = false;

    readonly List<VehicleAgent>               _all        = new();
    readonly List<VehicleAgent>               _filtered   = new();
    readonly Dictionary<VehicleAgent, Transform> _transforms = new();

    Transform _target;

    // ----------------------------------------------------------------
    // Called by FollowCamHUD after wiring all references
    // ----------------------------------------------------------------
    public void Init()
    {
        prevButton?.onClick  .AddListener(() => RequestCycle(-1));
        nextButton?.onClick  .AddListener(() => RequestCycle(+1));
        filterButton?.onClick.AddListener(RequestFilterCycle);

        if (blackoutGroup != null)
        {
            blackoutGroup.alpha          = 0f;
            blackoutGroup.blocksRaycasts = false;
        }

        UpdateLabel();
        UpdateFilterLabel();
    }

    // ----------------------------------------------------------------
    // Called by SimulationManager for every spawned vehicle
    // ----------------------------------------------------------------
    public void RegisterVehicle(VehicleAgent agent, GameObject go)
    {
        if (_all.Contains(agent)) return;
        _all.Add(agent);
        _transforms[agent] = go.transform;
        RebuildList();

        // Auto-follow the first vehicle that registers
        if (_target == null) ApplyTarget();
    }

    public void UnregisterVehicle(VehicleAgent agent)
    {
        _all.Remove(agent);
        _transforms.Remove(agent);
        RebuildList();
    }

    // ----------------------------------------------------------------
    void Update()
    {
        if (_transitioning) return;

        if (Input.GetKeyDown(KeyCode.LeftArrow)  ||
            Input.GetKeyDown(KeyCode.A))
            RequestCycle(-1);

        if (Input.GetKeyDown(KeyCode.RightArrow) ||
            Input.GetKeyDown(KeyCode.D))
            RequestCycle(+1);

        if (Input.GetKeyDown(KeyCode.Tab))
            RequestFilterCycle();
    }

    // ----------------------------------------------------------------
    void LateUpdate()
    {
        if (_target == null) return;

        Vector3 behind = _target.position
                         - _target.forward * followDistance
                         + Vector3.up * followHeight;

        Vector3 lookAt    = _target.position + Vector3.up * 0.5f;
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
        if (_filtered.Count <= 1 || _transitioning) return;
        int next = (_index + dir + _filtered.Count) % _filtered.Count;
        StartCoroutine(TransitionTo(next));
    }

    void RequestFilterCycle()
    {
        if (_transitioning) return;
        _filter = _filter switch
        {
            FilterMode.All       => FilterMode.TaxisOnly,
            FilterMode.TaxisOnly => FilterMode.CarsOnly,
            _                    => FilterMode.All
        };
        _index = 0;
        RebuildList();
        UpdateFilterLabel();
        if (_filtered.Count > 0)
            StartCoroutine(TransitionTo(0));
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
        if (_filtered.Count == 0) { _target = null; return; }
        _index = Mathf.Clamp(_index, 0, _filtered.Count - 1);
        _transforms.TryGetValue(_filtered[_index], out _target);
    }

    void UpdateLabel()
    {
        if (labelText == null) return;
        if (_filtered.Count == 0) { labelText.text = "No vehicles"; return; }
        string type = _filtered[_index] is AutonomousTaxi ? "Taxi" : "Car";
        labelText.text = $"{type}  {_index + 1} / {_filtered.Count}";
    }

    void UpdateFilterLabel()
    {
        if (filterLabel == null) return;
        filterLabel.text = _filter switch
        {
            FilterMode.TaxisOnly => "Taxis",
            FilterMode.CarsOnly  => "Cars",
            _                    => "All"
        };
    }
}
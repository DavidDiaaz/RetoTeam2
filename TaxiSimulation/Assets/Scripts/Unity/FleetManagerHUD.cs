using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;

/// <summary>
/// Attach to the same GameObject as SimulationManager.
/// Builds a top-right panel at runtime showing fleet status:
///   - Per-taxi state (Idle / En camino / En viaje) + distance
///   - Pending pedestrians count and states
/// Refreshes every refreshInterval seconds.
/// </summary>
public class FleetManagerHUD : MonoBehaviour
{
    [Header("Colors")]
    public Color panelBackground = new Color(0.06f, 0.06f, 0.08f, 0.88f);
    public Color headerColor     = new Color(0.92f, 0.92f, 0.95f, 1.00f);
    public Color idleColor       = new Color(0.35f, 0.90f, 0.65f, 1.00f);  // green
    public Color enRouteColor    = new Color(1.00f, 0.80f, 0.20f, 1.00f);  // yellow
    public Color carryingColor   = new Color(0.40f, 0.70f, 1.00f, 1.00f);  // blue
    public Color pendingColor    = new Color(0.85f, 0.55f, 0.25f, 1.00f);  // orange

    [Header("Layout")]
    public float rightPadding = 20f;
    public float topPadding   = 20f;
    public float panelWidth   = 280f;

    [Header("Update")]
    [Tooltip("Seconds between UI refreshes")]
    public float refreshInterval = 0.25f;

    SimulationManager _sim;
    TMP_Text          _taxiText;
    TMP_Text          _pedText;
    float             _timer;

    // ----------------------------------------------------------------
    void Start()
    {
        _sim = GetComponent<SimulationManager>();
        if (_sim == null)
        {
            Debug.LogWarning("[FleetManagerHUD] No SimulationManager on this GameObject.");
            enabled = false;
            return;
        }
        BuildPanel();
    }

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < refreshInterval) return;
        _timer = 0f;
        RefreshPanel();
    }

    // ----------------------------------------------------------------
    void BuildPanel()
    {
        var canvasGO = new GameObject("FleetManagerHUD_Canvas");
        DontDestroyOnLoad(canvasGO);

        var canvas          = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 99;

        var scaler                 = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Panel ────────────────────────────────────────────────────
        var panelGO = new GameObject("FleetPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);

        var rt          = panelGO.AddComponent<RectTransform>();
        rt.anchorMin    = new Vector2(1f, 1f);
        rt.anchorMax    = new Vector2(1f, 1f);
        rt.pivot        = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-rightPadding, -topPadding);
        rt.sizeDelta    = new Vector2(panelWidth, 0f);

        var img         = panelGO.AddComponent<Image>();
        img.color       = panelBackground;

        var vlg                     = panelGO.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment          = TextAnchor.UpperLeft;
        vlg.padding                 = new RectOffset(12, 12, 10, 10);
        vlg.spacing                 = 4f;
        vlg.childForceExpandWidth   = true;
        vlg.childForceExpandHeight  = false;
        vlg.childControlWidth       = true;
        vlg.childControlHeight      = true;

        var csf           = panelGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // ── Header ───────────────────────────────────────────────────
        MakeLabel(panelGO.transform, "FLEET MANAGER", headerColor, 11f, FontStyles.Bold);

        // ── Taxi section ─────────────────────────────────────────────
        _taxiText = MakeLabel(panelGO.transform, "...", headerColor, 11f);

        // ── Separator ────────────────────────────────────────────────
        MakeLabel(panelGO.transform, "──────────────────────", new Color(1,1,1,0.15f), 9f);

        // ── Pedestrian section ───────────────────────────────────────
        _pedText = MakeLabel(panelGO.transform, "...", pendingColor, 11f);
    }

    void RefreshPanel()
    {
        if (_sim?.World == null) return;

        var fleet = _sim.World.FleetManager;
        if (fleet == null) return;

        // ── Taxis ────────────────────────────────────────────────────
        var sb = new StringBuilder();
        sb.AppendLine($"<color=#EAEAF2>Taxis: {fleet.Taxis.Count}</color>");

        for (int i = 0; i < fleet.Taxis.Count; i++)
        {
            var taxi = fleet.Taxis[i];
            string col;
            string line;

            switch (taxi.State)
            {
                case TaxiState.Idle:
                    col  = ColorHex(idleColor);
                    line = "Disponible";
                    break;

                case TaxiState.EnRouteToPickup:
                    col  = ColorHex(enRouteColor);
                    string pickup = NodeLabel(taxi.PickupNodeTarget);
                    line = $"→ Pasajero {pickup}  {Fmt(taxi.EstimatedDistanceRemaining)}";
                    break;

                case TaxiState.Carrying:
                    col  = ColorHex(carryingColor);
                    string dest = NodeLabel(taxi.DropoffNodeTarget);
                    line = $"En viaje {dest}  {Fmt(taxi.EstimatedDistanceRemaining)}";
                    break;

                default:
                    col  = ColorHex(headerColor);
                    line = taxi.State.ToString();
                    break;
            }

            sb.AppendLine($"<color={col}>T{i + 1}  {line}</color>");
        }

        _taxiText.text = sb.ToString().TrimEnd();

        // ── Pedestrians ──────────────────────────────────────────────
        sb.Clear();
        int waiting  = 0;
        int matched  = 0;
        int riding   = 0;

        foreach (var p in fleet.Pending)
        {
            switch (p.State)
            {
                case PedestrianState.Waiting: waiting++;  break;
                case PedestrianState.Matched: matched++;  break;
                case PedestrianState.Riding:  riding++;   break;
            }
        }

        int total = waiting + matched + riding;
        sb.AppendLine($"<color=#EAEAF2>Pasajeros activos: {total}</color>");
        if (waiting > 0) sb.AppendLine($"<color={ColorHex(pendingColor)}>  Esperando taxi: {waiting}</color>");
        if (matched > 0) sb.AppendLine($"<color={ColorHex(enRouteColor)}>  Taxi asignado:  {matched}</color>");
        if (riding  > 0) sb.AppendLine($"<color={ColorHex(carryingColor)}>  En viaje:       {riding}</color>");

        _pedText.text = sb.ToString().TrimEnd();
    }

    // ----------------------------------------------------------------
    static TMP_Text MakeLabel(Transform parent, string text, Color color,
                              float fontSize, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);

        go.AddComponent<RectTransform>();

        var le           = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;

        var tmp           = go.AddComponent<TextMeshProUGUI>();
        tmp.text          = text;
        tmp.fontSize      = fontSize;
        tmp.fontStyle     = style;
        tmp.color         = color;
        tmp.alignment     = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
        tmp.overflowMode  = TextOverflowModes.Overflow;
        tmp.enableWordWrapping = false;

        return tmp;
    }

    static string ColorHex(Color c)
        => $"#{ColorUtility.ToHtmlStringRGB(c)}";

    static string NodeLabel(TrafficNode node)
    {
        if (node == null) return "";
        return string.IsNullOrEmpty(node.Label) ? $"#{node.id}" : node.Label;
    }

    static string Fmt(float meters)
    {
        if (meters >= 1000f) return $"{meters / 1000f:F1}km";
        if (meters >= 10f)   return $"{Mathf.RoundToInt(meters)}m";
        return "<10m";
    }
}

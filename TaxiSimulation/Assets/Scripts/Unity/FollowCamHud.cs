using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to the same GameObject as CameraFollowController (on the Main Camera).
/// Builds the entire HUD at runtime — no manual Canvas work needed.
///
/// Bottom-centre pill:
///   [Filter]  [◀]  Car 1 / 12  [▶]  [Free]
///
/// Top-left panel (only when a taxi is selected):
///   Disponible / En camino al pasajero / En viaje → #3
/// </summary>
[RequireComponent(typeof(CameraFollowController))]
public class FollowCamHUD : MonoBehaviour
{
    [Header("Colors")]
    public Color hudBackground = new Color(0.06f, 0.06f, 0.08f, 0.90f);
    public Color buttonColor   = new Color(0.18f, 0.18f, 0.22f, 1.00f);
    public Color accentColor   = new Color(0.35f, 0.90f, 0.65f, 1.00f);
    public Color textColor     = new Color(0.92f, 0.92f, 0.95f, 1.00f);
    public Color taxiIdleColor    = new Color(0.35f, 0.90f, 0.65f, 1.00f);  // green
    public Color taxiEnRouteColor = new Color(1.00f, 0.80f, 0.20f, 1.00f);  // yellow
    public Color taxiRidingColor  = new Color(0.40f, 0.70f, 1.00f, 1.00f);  // blue

    [Header("Layout")]
    public float bottomPadding = 32f;
    public float pillHeight    = 52f;
    public float statusPadding = 20f;

    CameraFollowController _cam;

    // ----------------------------------------------------------------
    void Awake()
    {
        _cam = GetComponent<CameraFollowController>();
        BuildHUD();
    }

    // ----------------------------------------------------------------
    void BuildHUD()
    {
        // ── Canvas ──────────────────────────────────────────────────
        var canvasGO = new GameObject("FollowCamHUD_Canvas");
        DontDestroyOnLoad(canvasGO);

        var canvas          = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler                 = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Blackout (full-screen, on top) ───────────────────────────
        var blackoutGO             = MakeStretchChild(canvasGO.transform, "Blackout", Color.black);
        blackoutGO.transform.SetAsLastSibling();
        var blackoutCG             = blackoutGO.AddComponent<CanvasGroup>();
        blackoutCG.alpha           = 0f;
        blackoutCG.blocksRaycasts  = false;
        blackoutCG.interactable    = false;

        // ── Taxi status panel (top-left) ─────────────────────────────
        var statusGO = new GameObject("TaxiStatusPanel");
        statusGO.transform.SetParent(canvasGO.transform, false);

        var statusRT             = statusGO.AddComponent<RectTransform>();
        statusRT.anchorMin       = new Vector2(0f, 1f);
        statusRT.anchorMax       = new Vector2(0f, 1f);
        statusRT.pivot           = new Vector2(0f, 1f);
        statusRT.anchoredPosition = new Vector2(statusPadding, -statusPadding);
        statusRT.sizeDelta       = new Vector2(260f, 64f);

        var statusImg  = statusGO.AddComponent<Image>();
        statusImg.color = hudBackground;

        var statusVlg                    = statusGO.AddComponent<VerticalLayoutGroup>();
        statusVlg.childAlignment         = TextAnchor.MiddleLeft;
        statusVlg.padding                = new RectOffset(14, 14, 8, 8);
        statusVlg.spacing                = 2f;
        statusVlg.childForceExpandWidth  = true;
        statusVlg.childForceExpandHeight = false;
        statusVlg.childControlWidth      = true;
        statusVlg.childControlHeight     = true;

        var statusCsf              = statusGO.AddComponent<ContentSizeFitter>();
        statusCsf.horizontalFit    = ContentSizeFitter.FitMode.Unconstrained;
        statusCsf.verticalFit      = ContentSizeFitter.FitMode.PreferredSize;

        var titleLbl = MakeLabelInLayout(statusGO.transform, "TaxiTitle", "TAXI",
                                         textColor, 10f, FontStyles.Bold, TextAlignmentOptions.Left);

        var statusLbl = MakeLabelInLayout(statusGO.transform, "TaxiStatus", "Disponible",
                                          taxiIdleColor, 14f, FontStyles.Bold, TextAlignmentOptions.Left);

        statusGO.SetActive(false);  // hidden until a taxi is selected

        // ── Bottom-centre pill ───────────────────────────────────────
        var pillGO  = new GameObject("Pill");
        pillGO.transform.SetParent(canvasGO.transform, false);

        var pillRT              = pillGO.AddComponent<RectTransform>();
        pillRT.anchorMin        = new Vector2(0.5f, 0f);
        pillRT.anchorMax        = new Vector2(0.5f, 0f);
        pillRT.pivot            = new Vector2(0.5f, 0f);
        pillRT.anchoredPosition = new Vector2(0f, bottomPadding);
        pillRT.sizeDelta        = new Vector2(0f, pillHeight);

        var pillImg   = pillGO.AddComponent<Image>();
        pillImg.color = hudBackground;

        var hlg                    = pillGO.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.padding                = new RectOffset(12, 12, 0, 0);
        hlg.spacing                = 6f;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;

        var csf             = pillGO.AddComponent<ContentSizeFitter>();
        csf.horizontalFit   = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit     = ContentSizeFitter.FitMode.Unconstrained;

        var pillCG = pillGO.AddComponent<CanvasGroup>();

        // ── Filter button ────────────────────────────────────────────
        var (filterBtn, filterLbl) = MakeButton(pillGO.transform, "Filter", "All",
                                                 90f, pillHeight, buttonColor, accentColor);

        // ── Left arrow ───────────────────────────────────────────────
        var (prevBtn, _) = MakeButton(pillGO.transform, "Prev", "◀",
                                       44f, pillHeight, Color.clear, textColor);

        // ── Label ────────────────────────────────────────────────────
        var labelLbl = MakeLabel(pillGO.transform, "Label", "No vehicles",
                                  150f, pillHeight, textColor, 14f);

        // ── Right arrow ──────────────────────────────────────────────
        var (nextBtn, _) = MakeButton(pillGO.transform, "Next", "▶",
                                       44f, pillHeight, Color.clear, textColor);

        // ── Free cam toggle ──────────────────────────────────────────
        var (freeCamBtn, freeCamLbl) = MakeButton(pillGO.transform, "FreeCam", "Free",
                                                   70f, pillHeight, buttonColor, textColor);

        // ── Wire into controller ─────────────────────────────────────
        _cam.hudGroup        = pillCG;
        _cam.blackoutGroup   = blackoutCG;
        _cam.labelText       = labelLbl;
        _cam.filterButton    = filterBtn;
        _cam.filterLabel     = filterLbl;
        _cam.prevButton      = prevBtn;
        _cam.nextButton      = nextBtn;
        _cam.freeCamButton   = freeCamBtn;
        _cam.freeCamLabel    = freeCamLbl;
        _cam.taxiStatusText  = statusLbl;
        _cam.taxiStatusPanel = statusGO;

        _cam.Init();

        // Also wire the title/status colours — update in LateUpdate via a helper component
        var colourHelper = statusGO.AddComponent<TaxiStatusColorHelper>();
        colourHelper.cam         = _cam;
        colourHelper.titleText   = titleLbl;
        colourHelper.statusText  = statusLbl;
        colourHelper.idleColor   = taxiIdleColor;
        colourHelper.enRouteColor = taxiEnRouteColor;
        colourHelper.ridingColor = taxiRidingColor;
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    static GameObject MakeStretchChild(Transform parent, string name, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    static (Button btn, TMP_Text lbl) MakeButton(
        Transform parent, string name, string text,
        float preferredWidth, float preferredHeight,
        Color bgColor, Color txtColor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        go.AddComponent<RectTransform>();

        var le             = go.AddComponent<LayoutElement>();
        le.preferredWidth  = preferredWidth;
        le.preferredHeight = preferredHeight;
        le.flexibleWidth   = 0f;

        var img           = go.AddComponent<Image>();
        img.color         = bgColor;

        var btn           = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var cb              = ColorBlock.defaultColorBlock;
        cb.normalColor      = bgColor;
        cb.highlightedColor = bgColor == Color.clear ? new Color(1f,1f,1f,0.12f) : bgColor * 1.35f;
        cb.pressedColor     = bgColor == Color.clear ? new Color(1f,1f,1f,0.06f) : bgColor * 0.70f;
        cb.selectedColor    = bgColor;
        cb.colorMultiplier  = 1f;
        cb.fadeDuration     = 0.1f;
        btn.colors          = cb;

        var lblGO = new GameObject("Text");
        lblGO.transform.SetParent(go.transform, false);

        var lblRT        = lblGO.AddComponent<RectTransform>();
        lblRT.anchorMin  = Vector2.zero;
        lblRT.anchorMax  = Vector2.one;
        lblRT.offsetMin  = Vector2.zero;
        lblRT.offsetMax  = Vector2.zero;

        var tmp           = lblGO.AddComponent<TextMeshProUGUI>();
        tmp.text          = text;
        tmp.fontSize      = 14f;
        tmp.fontStyle     = FontStyles.Bold;
        tmp.color         = txtColor;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.overflowMode  = TextOverflowModes.Overflow;

        return (btn, tmp);
    }

    static TMP_Text MakeLabel(
        Transform parent, string name, string text,
        float preferredWidth, float preferredHeight,
        Color txtColor, float fontSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var le             = go.AddComponent<LayoutElement>();
        le.preferredWidth  = preferredWidth;
        le.preferredHeight = preferredHeight;
        le.flexibleWidth   = 0f;

        go.AddComponent<RectTransform>();

        var tmp           = go.AddComponent<TextMeshProUGUI>();
        tmp.text          = text;
        tmp.fontSize      = fontSize;
        tmp.color         = txtColor;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.overflowMode  = TextOverflowModes.Overflow;

        return tmp;
    }

    static TMP_Text MakeLabelInLayout(
        Transform parent, string name, string text,
        Color txtColor, float fontSize,
        FontStyles style = FontStyles.Normal,
        TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        go.AddComponent<RectTransform>();

        var le             = go.AddComponent<LayoutElement>();
        le.flexibleWidth   = 1f;

        var tmp           = go.AddComponent<TextMeshProUGUI>();
        tmp.text          = text;
        tmp.fontSize      = fontSize;
        tmp.fontStyle     = style;
        tmp.color         = txtColor;
        tmp.alignment     = align;
        tmp.raycastTarget = false;
        tmp.overflowMode  = TextOverflowModes.Ellipsis;

        return tmp;
    }
}

/// <summary>
/// Updates the colour of the taxi status text to match the current TaxiState.
/// Runs on the same GameObject as the status panel.
/// </summary>
public class TaxiStatusColorHelper : MonoBehaviour
{
    public CameraFollowController cam;
    public TMP_Text titleText;
    public TMP_Text statusText;
    public Color idleColor;
    public Color enRouteColor;
    public Color ridingColor;

    void Update()
    {
        if (cam == null || statusText == null) return;

        // Access the current taxi through the public API — we need to peek at the
        // controller's internal state. We do it by checking the text prefix.
        // A clean way: expose a method on the camera controller.
        // We add a lightweight public getter for this.
        var taxi = cam.CurrentTaxi;
        if (taxi == null) return;

        statusText.color = taxi.State switch
        {
            TaxiState.EnRouteToPickup => enRouteColor,
            TaxiState.Carrying        => ridingColor,
            _                         => idleColor
        };
    }
}

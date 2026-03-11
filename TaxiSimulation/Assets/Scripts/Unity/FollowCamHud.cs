using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to the same GameObject as CameraFollowController (on the Main Camera).
/// Builds the entire HUD at runtime — no manual Canvas work needed.
///
/// Layout (bottom-centre of screen):
///   [Filter]  [◀]  Car 1 / 12  [▶]
///
/// All children use HorizontalLayoutGroup so positioning is automatic.
/// </summary>
[RequireComponent(typeof(CameraFollowController))]
public class FollowCamHUD : MonoBehaviour
{
    [Header("Colors")]
    public Color hudBackground = new Color(0.06f, 0.06f, 0.08f, 0.90f);
    public Color buttonColor   = new Color(0.18f, 0.18f, 0.22f, 1.00f);
    public Color accentColor   = new Color(0.35f, 0.90f, 0.65f, 1.00f);
    public Color textColor     = new Color(0.92f, 0.92f, 0.95f, 1.00f);

    [Header("Layout")]
    public float bottomPadding = 32f;
    public float pillHeight    = 52f;

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

        var scaler             = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode     = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Blackout (full-screen, on top) ───────────────────────────
        var blackoutGO = MakeStretchChild(canvasGO.transform, "Blackout", Color.black);
        blackoutGO.transform.SetAsLastSibling();
        var blackoutCG            = blackoutGO.AddComponent<CanvasGroup>();
        blackoutCG.alpha          = 0f;
        blackoutCG.blocksRaycasts = false;
        blackoutCG.interactable   = false;

        // ── Pill container ───────────────────────────────────────────
        // Anchored bottom-centre, auto-sizes width to content via ContentSizeFitter
        var pillGO   = new GameObject("Pill");
        pillGO.transform.SetParent(canvasGO.transform, false);

        var pillRT               = pillGO.AddComponent<RectTransform>();
        pillRT.anchorMin         = new Vector2(0.5f, 0f);
        pillRT.anchorMax         = new Vector2(0.5f, 0f);
        pillRT.pivot             = new Vector2(0.5f, 0f);
        pillRT.anchoredPosition  = new Vector2(0f, bottomPadding);
        pillRT.sizeDelta         = new Vector2(0f, pillHeight); // width driven by CSF

        var pillImg  = pillGO.AddComponent<Image>();
        pillImg.color = hudBackground;

        // HorizontalLayoutGroup lays out children left→right automatically
        var hlg                  = pillGO.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment       = TextAnchor.MiddleCenter;
        hlg.padding              = new RectOffset(12, 12, 0, 0);
        hlg.spacing              = 6f;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth    = true;
        hlg.childControlHeight   = true;

        // Width follows content
        var csf                  = pillGO.AddComponent<ContentSizeFitter>();
        csf.horizontalFit        = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit          = ContentSizeFitter.FitMode.Unconstrained;

        var pillCG               = pillGO.AddComponent<CanvasGroup>();

        // ── Filter button ────────────────────────────────────────────
        var (filterBtn, filterLbl) = MakeButton(pillGO.transform, "Filter", "All",
                                                 90f, pillHeight, buttonColor, accentColor);

        // ── Left arrow ───────────────────────────────────────────────
        var (prevBtn, _) = MakeButton(pillGO.transform, "Prev", "◀",
                                       44f, pillHeight, Color.clear, textColor);

        // ── Label ────────────────────────────────────────────────────
        var labelLbl = MakeLabel(pillGO.transform, "Label", "No vehicles",
                                  130f, pillHeight, textColor, 15f);

        // ── Right arrow ──────────────────────────────────────────────
        var (nextBtn, _) = MakeButton(pillGO.transform, "Next", "▶",
                                       44f, pillHeight, Color.clear, textColor);

        // ── Wire into controller ─────────────────────────────────────
        _cam.hudGroup      = pillCG;
        _cam.blackoutGroup = blackoutCG;
        _cam.labelText     = labelLbl;
        _cam.filterButton  = filterBtn;
        _cam.filterLabel   = filterLbl;
        _cam.prevButton    = prevBtn;
        _cam.nextButton    = nextBtn;

        // All references are now set — safe to add listeners
        _cam.Init();
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    /// Full-stretch child with a plain Image background
    static GameObject MakeStretchChild(Transform parent, string name, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin  = Vector2.zero;
        rt.anchorMax  = Vector2.one;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    /// Button with a centered TMP label. Returns (Button, TMP_Text).
    static (Button btn, TMP_Text lbl) MakeButton(
        Transform parent, string name, string text,
        float preferredWidth, float preferredHeight,
        Color bgColor, Color txtColor)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt  = go.AddComponent<RectTransform>();

        // LayoutElement tells HLG how wide this child wants to be
        var le  = go.AddComponent<LayoutElement>();
        le.preferredWidth  = preferredWidth;
        le.preferredHeight = preferredHeight;
        le.flexibleWidth   = 0f;

        var img             = go.AddComponent<Image>();
        img.color           = bgColor;

        var btn             = go.AddComponent<Button>();
        btn.targetGraphic   = img;

        var cb              = ColorBlock.defaultColorBlock;
        cb.normalColor      = bgColor;
        cb.highlightedColor = bgColor == Color.clear
            ? new Color(1f, 1f, 1f, 0.12f)
            : bgColor * 1.35f;
        cb.pressedColor     = bgColor == Color.clear
            ? new Color(1f, 1f, 1f, 0.06f)
            : bgColor * 0.70f;
        cb.selectedColor    = bgColor;
        cb.colorMultiplier  = 1f;
        cb.fadeDuration     = 0.1f;
        btn.colors          = cb;

        // TMP child — stretch to fill button
        var lblGO = new GameObject("Text");
        lblGO.transform.SetParent(go.transform, false);

        var lblRT            = lblGO.AddComponent<RectTransform>();
        lblRT.anchorMin      = Vector2.zero;
        lblRT.anchorMax      = Vector2.one;
        lblRT.offsetMin      = Vector2.zero;
        lblRT.offsetMax      = Vector2.zero;

        var tmp              = lblGO.AddComponent<TextMeshProUGUI>();
        tmp.text             = text;
        tmp.fontSize         = 14f;
        tmp.fontStyle        = FontStyles.Bold;
        tmp.color            = txtColor;
        tmp.alignment        = TextAlignmentOptions.Center;
        tmp.raycastTarget    = false;
        tmp.overflowMode     = TextOverflowModes.Overflow;

        return (btn, tmp);
    }

    /// Non-interactive label — just a TMP_Text sized by LayoutElement.
    static TMP_Text MakeLabel(
        Transform parent, string name, string text,
        float preferredWidth, float preferredHeight,
        Color txtColor, float fontSize)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);

        var le             = go.AddComponent<LayoutElement>();
        le.preferredWidth  = preferredWidth;
        le.preferredHeight = preferredHeight;
        le.flexibleWidth   = 0f;

        var rt             = go.AddComponent<RectTransform>();

        var tmp            = go.AddComponent<TextMeshProUGUI>();
        tmp.text           = text;
        tmp.fontSize       = fontSize;
        tmp.color          = txtColor;
        tmp.alignment      = TextAlignmentOptions.Center;
        tmp.raycastTarget  = false;
        tmp.overflowMode   = TextOverflowModes.Overflow;

        return tmp;
    }
}
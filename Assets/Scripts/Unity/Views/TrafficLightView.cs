using UnityEngine;

/// <summary>
/// Attach to the traffic light prefab root.
/// Watches a TrafficLight data model and drives the visual heads.
///
/// Prefab structure expected:
///   TrafficLightPole (root — has this component)
///     GreenLight   (child MeshRenderer or any GameObject)
///     YellowLight
///     RedLight
///
/// Assign the three child references in the inspector.
/// The component finds its TrafficLight via TrafficNode, which is
/// injected by NavGraphBuilder after the graph is built.
/// </summary>
public class TrafficLightView : MonoBehaviour
{
    [Header("Light head GameObjects")]
    public GameObject GreenLight;
    public GameObject YellowLight;
    public GameObject RedLight;

    [Header("Optional emissive materials (swapped instead of toggling)")]
    public Material GreenOnMaterial;
    public Material GreenOffMaterial;
    public Material YellowOnMaterial;
    public Material YellowOffMaterial;
    public Material RedOnMaterial;
    public Material RedOffMaterial;

    // Injected by NavGraphBuilder
    public TrafficLight Light { get; private set; }

    TrafficLight.State _lastState = (TrafficLight.State)(-1); // force first refresh

    public void Bind(TrafficLight light)
    {
        Light = light;
        _lastState = (TrafficLight.State)(-1);
        Refresh();
    }

    void Update()
    {
        if (Light == null) return;
        if (Light.CurrentState != _lastState)
            Refresh();
    }

    void Refresh()
    {
        if (Light == null) return;
        _lastState = Light.CurrentState;

        bool g = _lastState == TrafficLight.State.Green;
        bool y = _lastState == TrafficLight.State.Yellow;
        bool r = _lastState == TrafficLight.State.Red;

        SetHead(GreenLight,  GreenOnMaterial,  GreenOffMaterial,  g);
        SetHead(YellowLight, YellowOnMaterial, YellowOffMaterial, y);
        SetHead(RedLight,    RedOnMaterial,    RedOffMaterial,    r);
    }

    void SetHead(GameObject head, Material onMat, Material offMat, bool on)
    {
        if (head == null) return;

        // If materials are assigned, swap them on the MeshRenderer
        if (onMat != null && offMat != null)
        {
            var mr = head.GetComponent<MeshRenderer>();
            if (mr != null) mr.material = on ? onMat : offMat;
        }

        // Always toggle activity so the object can use emission / point lights
        head.SetActive(on);
    }
}
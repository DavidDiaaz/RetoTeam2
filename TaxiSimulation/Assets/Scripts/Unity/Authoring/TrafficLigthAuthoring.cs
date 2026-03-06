using UnityEngine;

public class TrafficLightAuthoring : MonoBehaviour
{
    [Header("Light")]
    public TrafficLight.State InitialState = TrafficLight.State.Green;

    [Header("Zone")]
    [Tooltip("Any lane endpoint within this radius gets this traffic light assigned.")]
    public float Radius = 2f;

    // Runtime reference — set by NavGraphBuilder
    [HideInInspector] public TrafficLight Light;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = InitialState == TrafficLight.State.Green ? Color.green
                     : InitialState == TrafficLight.State.Red   ? Color.red
                     : Color.yellow;

        Gizmos.DrawWireSphere(transform.position, Radius);
        Gizmos.DrawSphere(transform.position, 0.3f);
    }
#endif
}
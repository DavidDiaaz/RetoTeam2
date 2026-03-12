using UnityEngine;

/// <summary>
/// Attach to the pedestrian prefab root.
/// Placed at Pedestrian.WorldPosition; shows which state the pedestrian is in.
/// Destroyed by WorldView when state reaches Done or Cancelled.
///
/// Prefab child structure (all optional, assign in inspector):
///   WaitingIndicator  — visible while Waiting  (e.g. "!" billboard)
///   MatchedIndicator  — visible while Matched   (e.g. animated arrow)
///   RidingIndicator   — visible while Riding    (hide for most setups)
/// </summary>
public class PedestrianView : MonoBehaviour
{
    [Header("State indicator child objects (optional)")]
    public GameObject WaitingIndicator;
    public GameObject MatchedIndicator;
    public GameObject RidingIndicator;

    public Pedestrian Pedestrian { get; private set; }

    PedestrianState _lastState = (PedestrianState)(-1);

    public void Bind(Pedestrian p)
    {
        Pedestrian = p;
        transform.position = p.WorldPosition;
        Refresh();
    }

    void Update()
    {
        if (Pedestrian == null) return;
        if (Pedestrian.State != _lastState) Refresh();
    }

    void Refresh()
    {
        if (Pedestrian == null) return;
        _lastState = Pedestrian.State;

        // Hide the whole object once the passenger boards the taxi — they are
        // no longer standing at their origin, so the prefab should disappear.
        bool onStreet = _lastState == PedestrianState.Waiting
                     || _lastState == PedestrianState.Matched;
        gameObject.SetActive(onStreet);
        if (!onStreet) return;

        SetActive(WaitingIndicator, _lastState == PedestrianState.Waiting);
        SetActive(MatchedIndicator, _lastState == PedestrianState.Matched);
    }

    static void SetActive(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }
}
using System.Collections.Generic;
using UnityEngine;

public class RoadWaypoint : MonoBehaviour
{
    [Tooltip("Waypoints a los que puede ir un vehículo desde este punto. Pon 2-3 en intersecciones.")]
    public List<RoadWaypoint> nextWaypoints = new List<RoadWaypoint>();

    // devuelve un siguiente waypoint al azar
    public RoadWaypoint GetNextRandom()
    {
        if (nextWaypoints == null || nextWaypoints.Count == 0) return null;
        nextWaypoints.RemoveAll(w => w == null);
        if (nextWaypoints.Count == 0) return null;
        return nextWaypoints[Random.Range(0, nextWaypoints.Count)];
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.9f, 0f, 0.8f);
        Gizmos.DrawSphere(transform.position, 0.4f);

        if (nextWaypoints == null) return;
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.9f);
        foreach (var next in nextWaypoints)
        {
            if (next == null) continue;
            Gizmos.DrawLine(transform.position, next.transform.position);
            Vector3 mid = Vector3.Lerp(transform.position, next.transform.position, 0.7f);
            Gizmos.DrawSphere(mid, 0.2f);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.6f);
    }
}

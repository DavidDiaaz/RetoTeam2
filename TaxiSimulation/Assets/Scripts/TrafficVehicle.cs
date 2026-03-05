using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class TrafficVehicle : MonoBehaviour
{
    [Header("Movimiento")]
    public float moveSpeed     = 2f;
    public float rotationSpeed = 5f;

    [Header("Detección de vehículos adelante")]
    public float brakeDistance = 7f;
    public float brakeWidth    = 0.8f;
    public float maxBrakeTime  = 2.5f;

    [Header("Waypoints")]
    [Tooltip("Waypoint inicial. Si se deja vacío busca el más cercano automáticamente.")]
    public RoadWaypoint startWaypoint;

    [Header("Fallback si no hay waypoints")]
    public Vector3 patrolAreaMin = new Vector3(-50f, 0f, -50f);
    public Vector3 patrolAreaMax = new Vector3( 50f, 0f,  50f);
    public float   searchRadius  = 20f;

    private NavMeshAgent nav;
    private RoadWaypoint currentTarget;
    private bool         usingWaypoints = false;

    private bool  isBraking  = false;
    private float brakeTimer = 0f;

    void Start()
    {
        nav              = GetComponent<NavMeshAgent>();
        nav.speed        = moveSpeed;
        nav.angularSpeed = 0f;
        nav.acceleration = 8f;
        nav.autoBraking  = false;
        nav.radius       = 0.5f;
        nav.avoidancePriority = Random.Range(30, 70);

        RoadWaypoint[] all = FindObjectsByType<RoadWaypoint>(FindObjectsSortMode.None);

        if (all.Length > 0)
        {
            usingWaypoints = true;
            currentTarget  = startWaypoint != null ? startWaypoint : FindClosestWaypoint(all);
            nav.SetDestination(currentTarget.transform.position);
        }
        else
        {
            usingWaypoints = false;
            SetRandomDestination();
        }
    }

    void Update()
    {
        HandleBraking();
        if (isBraking) return;

        SmoothRotation();

        if (!nav.pathPending && nav.remainingDistance <= nav.stoppingDistance + 0.3f)
        {
            if (usingWaypoints) AdvanceWaypoint();
            else                SetRandomDestination();
        }
    }

    void HandleBraking()
    {
        bool blocked = DetectVehicleAhead();

        if (blocked)
        {
            nav.speed = 0f;
            isBraking = true;
            brakeTimer += Time.deltaTime;

            if (brakeTimer >= maxBrakeTime)
            {
                isBraking  = false;
                brakeTimer = 0f;
                nav.speed  = moveSpeed;
            }
        }
        else if (isBraking)
        {
            isBraking  = false;
            brakeTimer = 0f;
            nav.speed  = moveSpeed;
        }
    }

    bool DetectVehicleAhead()
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        RaycastHit[] hits = Physics.SphereCastAll(origin, brakeWidth, transform.forward, brakeDistance);
        foreach (var hit in hits)
        {
            if (hit.collider.gameObject == gameObject) continue;
            if (hit.collider.GetComponent<NavMeshAgent>() != null) return true;
        }
        return false;
    }

    void SmoothRotation()
    {
        Vector3 vel = nav.velocity;
        if (vel.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation,
                                              Quaternion.LookRotation(vel.normalized),
                                              rotationSpeed * Time.deltaTime);
    }

    void AdvanceWaypoint()
    {
        if (currentTarget == null) return;
        RoadWaypoint next = currentTarget.GetNextRandom();
        if (next == null) { nav.SetDestination(currentTarget.transform.position); return; }
        currentTarget = next;
        nav.SetDestination(currentTarget.transform.position);
    }

    RoadWaypoint FindClosestWaypoint(RoadWaypoint[] waypoints)
    {
        RoadWaypoint best = null;
        float bestDist = float.MaxValue;
        foreach (var wp in waypoints)
        {
            if (wp == null) continue;
            float d = Vector3.Distance(transform.position, wp.transform.position);
            if (d < bestDist) { bestDist = d; best = wp; }
        }
        return best;
    }

    void SetRandomDestination()
    {
        Vector3 candidate = new Vector3(
            Random.Range(patrolAreaMin.x, patrolAreaMax.x),
            0f,
            Random.Range(patrolAreaMin.z, patrolAreaMax.z));

        NavMeshHit hit;
        if (NavMesh.SamplePosition(candidate, out hit, searchRadius, NavMesh.AllAreas))
            nav.SetDestination(hit.position);
        else
            Invoke(nameof(SetRandomDestination), 0.5f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.5f,
                        transform.position + Vector3.up * 0.5f + transform.forward * brakeDistance);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class TaxiAgent : MonoBehaviour
{
    [Header("Identidad")]
    public string taxiId = "Taxi_01";

    public enum TaxiState { Disponible, Asignado, YendoAPickup, PasajeroABordo, EnViaje, Esperando }
    [Header("Estado actual")]
    public TaxiState state = TaxiState.Disponible;

    [Header("Movimiento")]
    public float moveSpeed          = 2f;
    public float stoppingDist       = 1f;
    public float rotationSpeed      = 5f;
    [Tooltip("Distancia al destino desde la que deja waypoints y va directo (tramo final)")]
    public float switchToDirectDist = 12f;

    [Header("Detección de vehículos adelante")]
    public float brakeDistance = 5f;
    public float brakeWidth    = 0.8f;
    public float maxBrakeTime  = 2f;

    [HideInInspector] public Vector3        belief_pickupPos;
    [HideInInspector] public Vector3        belief_dropoffPos;
    [HideInInspector] public bool           belief_passengerOnBoard = false;
    [HideInInspector] public PassengerAgent assignedPassenger;

    private FleetDispatcher dispatcher;
    private NavMeshAgent    nav;

    private RoadWaypoint currentWaypoint;
    private bool         hasWaypoints = false;

    private Vector3? tripTarget    = null;
    private bool     finalApproach = false;
    private bool     goingToPickup = false;

    private bool  isBraking  = false;
    private float brakeTimer = 0f;

    void Start()
    {
        nav                   = GetComponent<NavMeshAgent>();
        nav.speed             = moveSpeed;
        nav.stoppingDistance  = stoppingDist;
        nav.angularSpeed      = 0f;
        nav.acceleration      = 12f;
        nav.autoBraking       = true;
        nav.radius            = 0.5f;
        nav.avoidancePriority = Random.Range(0, 30);

        dispatcher = FindFirstObjectByType<FleetDispatcher>();
        dispatcher?.RegisterTaxi(this);

        InitWaypoints();
    }

    void InitWaypoints()
    {
        RoadWaypoint[] all = FindObjectsByType<RoadWaypoint>(FindObjectsSortMode.None);
        hasWaypoints = all.Length > 0;
        if (!hasWaypoints) return;

        currentWaypoint = FindClosestWaypoint(all);
        nav.SetDestination(currentWaypoint.transform.position);
    }

    void Update()
    {
        HandleBraking();
        if (isBraking) return;

        SmoothRotation();

        if (finalApproach)
            UpdateFinalApproach();
        else
            UpdateWaypointNavigation();
    }

    void UpdateWaypointNavigation()
    {
        if (!hasWaypoints || currentWaypoint == null) return;
        if (nav.pathPending) return;
        if (nav.remainingDistance > nav.stoppingDistance + 0.3f) return;

        // si hay destino y ya estamos cerca, cambiar a tramo final directo
        if (tripTarget.HasValue &&
            Vector3.Distance(transform.position, tripTarget.Value) <= switchToDirectDist)
        {
            BeginFinalApproach();
            return;
        }

        // elegir siguiente waypoint
        RoadWaypoint next = tripTarget.HasValue
            ? GetNextWaypointToward(tripTarget.Value)
            : currentWaypoint.GetNextRandom();

        if (next == null) return;
        currentWaypoint = next;
        nav.SetDestination(currentWaypoint.transform.position);
    }

    // en intersecciones: elige el waypoint que más acerca al destino
    RoadWaypoint GetNextWaypointToward(Vector3 target)
    {
        var nexts = currentWaypoint.nextWaypoints;
        if (nexts == null || nexts.Count == 0) return null;
        if (nexts.Count == 1) return nexts[0];

        RoadWaypoint best = null;
        float bestDist = float.MaxValue;
        foreach (var wp in nexts)
        {
            if (wp == null) continue;
            float d = Vector3.Distance(wp.transform.position, target);
            if (d < bestDist) { bestDist = d; best = wp; }
        }
        return best;
    }

    void BeginFinalApproach()
    {
        finalApproach = true;
        NavMeshHit hit;
        Vector3 dest = tripTarget.Value;
        if (NavMesh.SamplePosition(dest, out hit, 5f, NavMesh.AllAreas))
            dest = hit.position;
        nav.SetDestination(dest);
    }

    void UpdateFinalApproach()
    {
        if (nav.pathPending) return;
        if (nav.remainingDistance > stoppingDist + 0.2f) return;

        finalApproach = false;
        tripTarget = null;

        if (goingToPickup) PickUpPassenger();
        else               DropOffPassenger();
    }

    void PickUpPassenger()
    {
        if (assignedPassenger == null) return;
        belief_passengerOnBoard = true;
        state = TaxiState.PasajeroABordo;
        assignedPassenger.OnTaxiArrived();
        StartTripLeg(belief_dropoffPos, isPickup: false);
    }

    void DropOffPassenger()
    {
        assignedPassenger?.OnTripCompleted();
        assignedPassenger       = null;
        belief_passengerOnBoard = false;
        state                   = TaxiState.Disponible;
        dispatcher?.OnTaxiAvailable(this);

        // retomar patrulla desde el waypoint más cercano
        RoadWaypoint[] all = FindObjectsByType<RoadWaypoint>(FindObjectsSortMode.None);
        if (all.Length > 0)
        {
            currentWaypoint = FindClosestWaypoint(all);
            nav.SetDestination(currentWaypoint.transform.position);
        }
    }

    public void AssignTrip(PassengerAgent passenger, Vector3 pickup, Vector3 dropoff)
    {
        assignedPassenger = passenger;
        belief_pickupPos  = pickup;
        belief_dropoffPos = dropoff;
        state             = TaxiState.Asignado;
        StartTripLeg(pickup, isPickup: true);
    }

    void StartTripLeg(Vector3 destination, bool isPickup)
    {
        goingToPickup = isPickup;
        tripTarget    = destination;
        finalApproach = false;
        state = isPickup ? TaxiState.YendoAPickup : TaxiState.EnViaje;

        // redirigir al waypoint actual para que empiece a encaminarse
        if (hasWaypoints && currentWaypoint != null)
            nav.SetDestination(currentWaypoint.transform.position);
    }

    void HandleBraking()
    {
        bool blocked = DetectVehicleAhead();

        if (blocked)
        {
            if (!isBraking)
            {
                isBraking     = true;
                brakeTimer    = 0f;
                nav.isStopped = true;
                if (state != TaxiState.Disponible) state = TaxiState.Esperando;
            }
            else
            {
                brakeTimer += Time.deltaTime;
                if (brakeTimer >= maxBrakeTime)
                {
                    brakeTimer    = 0f;
                    nav.isStopped = false;
                }
            }
        }
        else if (isBraking)
        {
            isBraking     = false;
            brakeTimer    = 0f;
            nav.isStopped = false;
            if (state == TaxiState.Esperando)
                state = tripTarget.HasValue
                    ? (goingToPickup ? TaxiState.YendoAPickup : TaxiState.EnViaje)
                    : TaxiState.Disponible;
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

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;    Gizmos.DrawWireSphere(belief_pickupPos,  0.5f);
        Gizmos.color = Color.magenta; Gizmos.DrawWireSphere(belief_dropoffPos, 0.5f);
        if (tripTarget.HasValue) { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(tripTarget.Value, 0.8f); }
    }
}

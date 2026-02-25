using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// TaxiAgent v4 — NavMesh Navigation (versión corregida)
///
/// • Usa NavMeshAgent para seguir calles automáticamente.
/// • Raycast solo detecta OTROS TAXIS (no edificios — el NavMesh ya los evita).
/// • Si detecta taxi adelante, frena. Si sigue bloqueado, el NavMesh recalcula.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class TaxiAgent : MonoBehaviour
{
    [Header("Identidad")]
    public string taxiId = "Taxi_01";


    public enum TaxiState { Disponible, Asignado, YendoAPickup, PasajeroABordo, EnViaje, Esperando }
    [Header("Estado actual")]
    public TaxiState state = TaxiState.Disponible;

    [Header("Movimiento")]
    public float moveSpeed    = 6f;
    public float stoppingDist = 1f;

    [Header("Evitar colisiones — solo otros taxis")]
    [Tooltip("Asigna ÚNICAMENTE la capa 'Taxi' aquí, NO la de edificios")]
    public LayerMask taxiLayer;
    public float     brakeDistance = 3f;


    [HideInInspector] public Vector3 belief_pickupPos;
    [HideInInspector] public Vector3 belief_dropoffPos;
    [HideInInspector] public bool    belief_passengerOnBoard = false;

    [HideInInspector] public PassengerAgent assignedPassenger;
    private FleetDispatcher dispatcher;
    private NavMeshAgent    nav;

    private bool  isBraking      = false;
    private float brakeTimer     = 0f;
    public  float maxBrakeTime   = 2f;
    private bool  hasDestination = false;
    private bool  goingToPickup  = false;

    void Start()
    {
        nav                  = GetComponent<NavMeshAgent>();
        nav.speed            = moveSpeed;
        nav.stoppingDistance = stoppingDist;
        nav.angularSpeed     = 300f;
        nav.acceleration     = 12f;
        nav.autoBraking      = true;

        dispatcher = FindFirstObjectByType<FleetDispatcher>();
        dispatcher?.RegisterTaxi(this);
    }

    void Update()
    {
        // Capa reactiva
        bool taxiAhead = DetectTaxiAhead();

        if (taxiAhead)
        {
            // Frenar
            if (!isBraking)
            {
                isBraking        = true;
                brakeTimer       = 0f;
                nav.isStopped    = true;
                state            = TaxiState.Esperando;
            }
            else
            {
                brakeTimer += Time.deltaTime;
                // Si lleva mucho tiempo frenado, dejar que el NavMesh
                // recalcule solo reactivándolo
                if (brakeTimer >= maxBrakeTime)
                {
                    brakeTimer    = 0f;
                    nav.isStopped = false;   // NavMesh buscará ruta alternativa
                }
            }
            return;
        }

        // Camino libre
        if (isBraking)
        {
            isBraking     = false;
            brakeTimer    = 0f;
            nav.isStopped = false;
            state         = goingToPickup ? TaxiState.YendoAPickup : TaxiState.EnViaje;
        }

        // 2. Capa Deliberativa 
        if (hasDestination && !nav.pathPending && nav.remainingDistance <= stoppingDist + 0.2f)
        {
            OnDestinationReached();
        }
    }

    bool DetectTaxiAhead()
    {
        // Si no hay capa asignada, no detectar nada
        if (taxiLayer.value == 0) return false;

        Vector3    origin = transform.position + Vector3.up * 0.3f;
        RaycastHit hit;
        bool blocked = Physics.Raycast(origin, transform.forward,
                                       out hit, brakeDistance, taxiLayer);

        // Ignorarse a sí mismo
        if (blocked && hit.collider.gameObject == gameObject)
            blocked = false;

        Debug.DrawRay(origin, transform.forward * brakeDistance,
                      blocked ? Color.red : Color.green);
        return blocked;
    }

    void OnDestinationReached()
    {
        hasDestination = false;

        if (goingToPickup)
            PickUpPassenger();
        else
            DropOffPassenger();
    }

    void PickUpPassenger()
    {
        if (assignedPassenger == null) return;
        belief_passengerOnBoard = true;
        state = TaxiState.PasajeroABordo;
        assignedPassenger.OnTaxiArrived();
        Debug.Log($"[{taxiId}] Pasajero recogido → yendo al destino.");
        SetDestination(belief_dropoffPos, isPickup: false);
    }

    void DropOffPassenger()
    {
        Debug.Log($"[{taxiId}] Pasajero entregado.");
        assignedPassenger?.OnTripCompleted();
        assignedPassenger       = null;
        belief_passengerOnBoard = false;
        state                   = TaxiState.Disponible;
        dispatcher?.OnTaxiAvailable(this);
    }

    public void AssignTrip(PassengerAgent passenger, Vector3 pickup, Vector3 dropoff)
    {
        assignedPassenger = passenger;
        belief_pickupPos  = pickup;
        belief_dropoffPos = dropoff;
        state             = TaxiState.Asignado;
        Debug.Log($"[{taxiId}] Viaje asignado → yendo a pickup.");
        SetDestination(pickup, isPickup: true);
    }

    void SetDestination(Vector3 destination, bool isPickup)
    {
        goingToPickup = isPickup;

        NavMeshHit hit;
        Vector3    target = destination;

        if (NavMesh.SamplePosition(destination, out hit, 5f, NavMesh.AllAreas))
            target = hit.position;
        else
        {
            Debug.LogWarning($"[{taxiId}] Destino fuera del NavMesh: {destination}\n" +
                             "Asegúrate de que el punto esté encima de una calle.");
            return;
        }

        nav.isStopped = false;
        nav.SetDestination(target);
        hasDestination = true;
        state = isPickup ? TaxiState.YendoAPickup : TaxiState.EnViaje;

        Debug.Log($"[{taxiId}] Navegando a {target}");
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(belief_pickupPos, 0.5f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(belief_dropoffPos, 0.5f);
    }
}
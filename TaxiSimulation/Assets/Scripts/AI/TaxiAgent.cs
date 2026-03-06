// ============================================================================
// TaxiAgent.cs — Agente Taxi con Arquitectura BDI
// ============================================================================
//
// DESCRIPCIÓN:
//   Agente taxi que implementa el modelo BDI para tomar decisiones de
//   movimiento, recogida y transporte de pasajeros. Hereda de BDIAgent
//   y conserva toda la lógica de navegación por NavMesh y waypoints.
//
// CICLO BDI DEL TAXI:
//
//   Percepción    → Posición actual, estado del tráfico, pasajero cercano
//   Creencias     → Estado propio, destino, nivel de congestión, pasajero asignado
//   Deseos        → Completar viaje, recoger pasajero, minimizar tiempo
//   Intenciones   → Moverse a pickup, recoger, transportar, liberarse
//   Ejecución     → Configura destino de navegación
//
// ESTADOS DEL TAXI (máquina de estados BDI):
//
//   [Idle] ──asignación──▶ [GoingToPassenger] ──llega──▶ [TransportingPassenger]
//     ▲                                                         │
//     └───────────────────────llega a destino──────────────────┘
//
// NAVEGACIÓN:
//   Usa combinación de waypoints (RoadWaypoint) para tramos largos
//   y final approach directo con NavMesh para tramos cortos.
//   Incluye detección de vehículos y frenado automático.
//
// USO EN UNITY:
//   1. Agregar al prefab del taxi (debe tener NavMeshAgent)
//   2. Se registra automáticamente con el DispatcherAgent
//   3. La navegación usa los RoadWaypoint de la escena
//
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Agente taxi BDI que navega por la ciudad, recoge pasajeros y los
/// transporta a su destino usando el modelo Belief-Desire-Intention.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class TaxiAgent : BDIAgent
{
    // ═════════════════════════════════════════════
    //  IDENTIDAD
    // ═════════════════════════════════════════════

    [Header("Identidad")]
    public string taxiId = "Taxi_01";

    // ═════════════════════════════════════════════
    //  ESTADOS DEL TAXI
    // ═════════════════════════════════════════════

    /// <summary>
    /// Estados posibles del taxi dentro del ciclo BDI.
    /// </summary>
    public enum TaxiState
    {
        /// <summary>Disponible, patrullando por waypoints.</summary>
        Idle,
        /// <summary>Dirigiéndose al punto de recogida del pasajero.</summary>
        GoingToPassenger,
        /// <summary>Transportando pasajero al destino.</summary>
        TransportingPassenger,
        /// <summary>Regresando a zona de patrulla (extensible).</summary>
        ReturningToZone,
        /// <summary>Frenado temporalmente por vehículo adelante.</summary>
        Waiting
    }

    [Header("Estado actual")]
    public TaxiState state = TaxiState.Idle;

    // ═════════════════════════════════════════════
    //  CREENCIAS (Beliefs) — Modelo mental del taxi
    // ═════════════════════════════════════════════

    /// <summary>Posición actual del taxi (actualizada cada ciclo BDI).</summary>
    [HideInInspector] public Vector3 belief_currentPosition;

    /// <summary>Posición de recogida del pasajero asignado.</summary>
    [HideInInspector] public Vector3 belief_pickupPos;

    /// <summary>Posición de destino del pasajero.</summary>
    [HideInInspector] public Vector3 belief_dropoffPos;

    /// <summary>¿Hay un pasajero a bordo?</summary>
    [HideInInspector] public bool belief_passengerOnBoard = false;

    /// <summary>Nivel de congestión en la posición actual.</summary>
    [HideInInspector] public float belief_congestionLevel = 0f;

    /// <summary>Distancia al destino actual.</summary>
    [HideInInspector] public float belief_distanceToTarget = 0f;

    /// <summary>¿Hay un vehículo bloqueando el paso adelante?</summary>
    [HideInInspector] public bool belief_vehicleAhead = false;

    /// <summary>Referencia al pasajero asignado.</summary>
    [HideInInspector] public PassengerAgent assignedPassenger;

    // ═════════════════════════════════════════════
    //  DESEOS (Desires)
    // ═════════════════════════════════════════════

    /// <summary>Deseos posibles del taxi.</summary>
    public enum TaxiDesire
    {
        /// <summary>Patrullar la ciudad esperando asignación.</summary>
        Patrol,
        /// <summary>Llegar al punto de recogida del pasajero.</summary>
        ReachPickup,
        /// <summary>Transportar pasajero al destino.</summary>
        DeliverPassenger,
        /// <summary>Completar el viaje y liberarse.</summary>
        CompleteTrip,
        /// <summary>Evitar zona congestionada.</summary>
        AvoidCongestion
    }

    /// <summary>Deseo activo en este ciclo.</summary>
    private TaxiDesire desire_current = TaxiDesire.Patrol;

    // ═════════════════════════════════════════════
    //  INTENCIONES (Intentions)
    // ═════════════════════════════════════════════

    /// <summary>Intenciones concretas del taxi.</summary>
    public enum TaxiIntention
    {
        /// <summary>Seguir patrullando por waypoints.</summary>
        FollowWaypoints,
        /// <summary>Navegar hacia el pasajero.</summary>
        NavigateToPickup,
        /// <summary>Recoger al pasajero.</summary>
        PickUpPassenger,
        /// <summary>Navegar hacia el destino.</summary>
        NavigateToDropoff,
        /// <summary>Dejar al pasajero.</summary>
        DropOffPassenger,
        /// <summary>Quedarse quieto (frenando).</summary>
        Wait
    }

    /// <summary>Intención activa seleccionada por deliberación.</summary>
    private TaxiIntention intention_current = TaxiIntention.FollowWaypoints;

    // ═════════════════════════════════════════════
    //  CONFIGURACIÓN DE MOVIMIENTO
    // ═════════════════════════════════════════════

    [Header("Movimiento")]
    public float moveSpeed          = 2f;
    public float stoppingDist       = 1f;
    public float rotationSpeed      = 5f;

    [Tooltip("Distancia al destino desde la que deja waypoints y va directo (tramo final)")]
    public float switchToDirectDist = 12f;

    [Header("Detección de vehículos adelante")]
    public float brakeDistance       = 4f;
    public float brakeWidth          = 0.4f;
    [Tooltip("Segundos frenado antes de intentar desatascarse")]
    public float stuckTimeout        = 5f;

    // ═════════════════════════════════════════════
    //  COMPONENTES Y ESTADO INTERNO
    // ═════════════════════════════════════════════

    private DispatcherAgent dispatcher;
    private NavMeshAgent    nav;

    // ── Waypoints ──
    private RoadWaypoint currentWaypoint;
    private bool         hasWaypoints = false;

    // ── Navegación a destino ──
    private Vector3? tripTarget    = null;
    private bool     finalApproach = false;
    private bool     goingToPickup = false;

    // ── Frenado y anti-deadlock ──
    private bool  isBraking  = false;
    private float brakeTimer = 0f;
    private float stuckTimer = 0f;
    private Vector3 lastRecordedPos;
    private float movementCheckTimer = 0f;

    // ═════════════════════════════════════════════
    //  INICIALIZACIÓN
    // ═════════════════════════════════════════════

    void Start()
    {
        // Configurar NavMeshAgent con radio proporcional a la escala
        nav                       = GetComponent<NavMeshAgent>();
        nav.speed                 = moveSpeed;
        nav.stoppingDistance      = stoppingDist;
        nav.angularSpeed          = 0f;
        nav.acceleration          = 12f;
        nav.autoBraking           = true;
        nav.avoidancePriority     = Random.Range(10, 40);
        nav.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

        // Radio proporcional a la escala del vehículo
        float scaleFactor = Mathf.Max(transform.localScale.x, transform.localScale.z);
        nav.radius = Mathf.Clamp(scaleFactor * 3f, 0.3f, 1.0f);

        // Configurar BDI
        agentName       = taxiId;
        bdiTickInterval = 0.3f;

        // Registrarse con el dispatcher
        dispatcher = FindFirstObjectByType<DispatcherAgent>();
        dispatcher?.RegisterTaxi(this);

        // Inicializar sistema de waypoints
        InitWaypoints();
        lastRecordedPos = transform.position;

        Debug.Log($"[BDI-Taxi] {taxiId} iniciado. Waypoints={hasWaypoints}");
    }

    /// <summary>
    /// Busca el waypoint más cercano e inicia la navegación.
    /// </summary>
    void InitWaypoints()
    {
        RoadWaypoint[] all = FindObjectsByType<RoadWaypoint>(FindObjectsSortMode.None);
        hasWaypoints = all.Length > 0;
        if (!hasWaypoints) return;

        currentWaypoint = FindClosestWaypoint(all);
        nav.SetDestination(currentWaypoint.transform.position);
    }

    // ═════════════════════════════════════════════
    //  IMPLEMENTACIÓN DEL CICLO BDI
    // ═════════════════════════════════════════════

    /// <summary>
    /// PASO 1: Percepción del entorno.
    /// El taxi observa su posición, estado del tráfico y vehículos cercanos.
    /// </summary>
    protected override void PerceiveEnvironment()
    {
        // Percibir posición actual
        belief_currentPosition = transform.position;

        // Percibir distancia al destino actual
        belief_distanceToTarget = tripTarget.HasValue
            ? Vector3.Distance(belief_currentPosition, tripTarget.Value)
            : 0f;

        // Percibir congestión en la posición actual
        if (TrafficManager.Instance != null)
            belief_congestionLevel = TrafficManager.Instance
                .GetCongestionValue(belief_currentPosition);

        // Percibir vehículos adelante
        belief_vehicleAhead = DetectVehicleAhead();

        if (debugBDI)
            Debug.Log($"[BDI-Taxi] {taxiId} Percepción: " +
                      $"estado={state}, congestión={belief_congestionLevel:F2}, " +
                      $"distancia={belief_distanceToTarget:F1}, " +
                      $"bloqueado={belief_vehicleAhead}");
    }

    /// <summary>
    /// PASO 2: Actualización de creencias.
    /// El taxi actualiza su modelo mental con la información percibida.
    /// </summary>
    protected override void UpdateBeliefs()
    {
        // Verificar si el pasajero asignado sigue existiendo
        if (assignedPassenger != null && !assignedPassenger.gameObject.activeInHierarchy)
        {
            // El pasajero desapareció → limpiar creencias
            assignedPassenger       = null;
            belief_passengerOnBoard = false;
            tripTarget              = null;
            state                   = TaxiState.Idle;

            Debug.Log($"[BDI-Taxi] {taxiId} Pasajero desapareció, volviendo a Idle.");
        }
    }

    /// <summary>
    /// PASO 3: Generación de deseos.
    /// El taxi determina qué quiere lograr según su estado actual.
    /// </summary>
    protected override void GenerateDesires()
    {
        switch (state)
        {
            case TaxiState.Idle:
                // Sin misión → desea patrullar
                desire_current = TaxiDesire.Patrol;
                break;

            case TaxiState.GoingToPassenger:
                // En camino al pickup → desea llegar al pasajero
                desire_current = TaxiDesire.ReachPickup;
                break;

            case TaxiState.TransportingPassenger:
                // Con pasajero → desea entregarlo al destino
                desire_current = TaxiDesire.DeliverPassenger;
                break;

            case TaxiState.ReturningToZone:
                // Regresando → desea patrullar
                desire_current = TaxiDesire.Patrol;
                break;

            case TaxiState.Waiting:
                // Frenado → mantener deseo anterior
                break;
        }

        // Si estamos en zona muy congestionada y hay alternativa,
        // podemos desear evitar congestión
        if (belief_congestionLevel > 0.8f && tripTarget.HasValue)
        {
            // Solo si la congestión es extrema, considerar desvío
            // (en esta versión base se prioriza el viaje directo)
        }

        if (debugBDI)
            Debug.Log($"[BDI-Taxi] {taxiId} Deseo: {desire_current}");
    }

    /// <summary>
    /// PASO 4: Selección de intenciones (deliberación).
    /// El taxi elige la acción concreta basándose en su deseo y creencias.
    /// </summary>
    protected override void SelectIntentions()
    {
        switch (desire_current)
        {
            case TaxiDesire.Patrol:
                intention_current = TaxiIntention.FollowWaypoints;
                break;

            case TaxiDesire.ReachPickup:
                // ¿Ya llegamos al punto de recogida?
                if (belief_distanceToTarget <= stoppingDist + 0.5f && finalApproach)
                    intention_current = TaxiIntention.PickUpPassenger;
                else
                    intention_current = TaxiIntention.NavigateToPickup;
                break;

            case TaxiDesire.DeliverPassenger:
                // ¿Ya llegamos al destino?
                if (belief_distanceToTarget <= stoppingDist + 0.5f && finalApproach)
                    intention_current = TaxiIntention.DropOffPassenger;
                else
                    intention_current = TaxiIntention.NavigateToDropoff;
                break;

            case TaxiDesire.CompleteTrip:
                intention_current = TaxiIntention.DropOffPassenger;
                break;

            case TaxiDesire.AvoidCongestion:
                // Extensible: en esta versión, seguimos la ruta directa
                intention_current = tripTarget.HasValue
                    ? TaxiIntention.NavigateToPickup
                    : TaxiIntention.FollowWaypoints;
                break;
        }

        if (debugBDI)
            Debug.Log($"[BDI-Taxi] {taxiId} Intención: {intention_current}");
    }

    /// <summary>
    /// PASO 5: Ejecución de intenciones.
    /// El taxi ejecuta la acción decidida por la deliberación.
    /// Nota: La navegación frame-a-frame se realiza en OnBDIUpdate().
    /// </summary>
    protected override void ExecuteIntentions()
    {
        // Las intenciones de pickup/dropoff se ejecutan cuando
        // la navegación frame-a-frame detecta la llegada.
        // Aquí solo gestionamos transiciones de alto nivel.

        switch (intention_current)
        {
            case TaxiIntention.FollowWaypoints:
                // La navegación frame-a-frame ya lo gestiona
                break;

            case TaxiIntention.NavigateToPickup:
                // Verificar que tenemos destino configurado
                if (!tripTarget.HasValue && assignedPassenger != null)
                {
                    StartTripLeg(belief_pickupPos, isPickup: true);
                }
                break;

            case TaxiIntention.NavigateToDropoff:
                if (!tripTarget.HasValue && belief_passengerOnBoard)
                {
                    StartTripLeg(belief_dropoffPos, isPickup: false);
                }
                break;

            case TaxiIntention.PickUpPassenger:
                // Se maneja en UpdateFinalApproach cuando el nav llega
                break;

            case TaxiIntention.DropOffPassenger:
                // Se maneja en UpdateFinalApproach cuando el nav llega
                break;

            case TaxiIntention.Wait:
                // El frenado se gestiona por OnBDIUpdate
                break;
        }
    }

    // ═════════════════════════════════════════════
    //  LÓGICA FRAME-A-FRAME (OnBDIUpdate)
    //  Se ejecuta CADA FRAME, no solo cada ciclo BDI
    // ═════════════════════════════════════════════

    /// <summary>
    /// Lógica de movimiento continuo. Se ejecuta cada frame.
    /// Gestiona: frenado, rotación, navegación por waypoints y final approach.
    /// </summary>
    protected override void OnBDIUpdate()
    {
        // ── Frenado (prioridad máxima) ──
        HandleBraking();

        // ── Rotación suave ──
        SmoothRotation();

        // ── Navegación ──
        if (!isBraking)
        {
            if (finalApproach)
                UpdateFinalApproach();
            else
                UpdateWaypointNavigation();
        }

        // ── Anti-deadlock ──
        DetectStuck();
    }

    // ═════════════════════════════════════════════
    //  NAVEGACIÓN POR WAYPOINTS
    // ═════════════════════════════════════════════

    /// <summary>
    /// Gestiona la navegación secuencial por waypoints.
    /// Si tiene un destino (tripTarget), elige el waypoint que
    /// más lo acerca; si no, patrulla al azar.
    /// </summary>
    void UpdateWaypointNavigation()
    {
        if (!hasWaypoints || currentWaypoint == null) return;
        if (nav.pathPending) return;
        if (nav.remainingDistance > nav.stoppingDistance + 0.3f) return;

        // Si hay destino y estamos cerca, cambiar a tramo final directo
        if (tripTarget.HasValue &&
            Vector3.Distance(transform.position, tripTarget.Value) <= switchToDirectDist)
        {
            BeginFinalApproach();
            return;
        }

        // Elegir siguiente waypoint
        RoadWaypoint next = tripTarget.HasValue
            ? GetNextWaypointToward(tripTarget.Value)
            : currentWaypoint.GetNextRandom();

        if (next == null) return;
        currentWaypoint = next;
        nav.SetDestination(currentWaypoint.transform.position);
    }

    /// <summary>
    /// En intersecciones: elige el waypoint que más acerca al destino.
    /// Implementa deliberación local de ruta a nivel de waypoint.
    /// </summary>
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

    /// <summary>
    /// Inicia el tramo final: navegación directa por NavMesh al destino.
    /// Se activa cuando el taxi está a menos de switchToDirectDist del objetivo.
    /// </summary>
    void BeginFinalApproach()
    {
        finalApproach = true;
        NavMeshHit hit;
        Vector3 dest = tripTarget.Value;
        if (NavMesh.SamplePosition(dest, out hit, 5f, NavMesh.AllAreas))
            dest = hit.position;
        nav.SetDestination(dest);
    }

    /// <summary>
    /// Verifica si el taxi llegó al destino durante el tramo final.
    /// Al llegar, ejecuta pickup o dropoff según corresponda.
    /// </summary>
    void UpdateFinalApproach()
    {
        if (nav.pathPending) return;
        if (nav.remainingDistance > stoppingDist + 0.2f) return;

        finalApproach = false;
        tripTarget = null;

        if (goingToPickup) PickUpPassenger();
        else               DropOffPassenger();
    }

    // ═════════════════════════════════════════════
    //  ACCIONES DE PASAJERO
    // ═════════════════════════════════════════════

    /// <summary>
    /// Recoge al pasajero. Cambia estado a TransportingPassenger
    /// e inicia el tramo hacia el destino.
    /// </summary>
    void PickUpPassenger()
    {
        if (assignedPassenger == null) return;

        belief_passengerOnBoard = true;
        state = TaxiState.TransportingPassenger;

        Debug.Log($"[BDI-Taxi] {taxiId} Pasajero recogido: {assignedPassenger.passengerId}");

        assignedPassenger.OnTaxiArrived();
        StartTripLeg(belief_dropoffPos, isPickup: false);
    }

    /// <summary>
    /// Deja al pasajero en el destino. Cambia estado a Idle
    /// y notifica al dispatcher que está disponible.
    /// </summary>
    void DropOffPassenger()
    {
        Debug.Log($"[BDI-Taxi] {taxiId} Pasajero entregado en destino.");

        assignedPassenger?.OnTripCompleted();
        assignedPassenger       = null;
        belief_passengerOnBoard = false;
        state                   = TaxiState.Idle;
        dispatcher?.OnTaxiAvailable(this);

        // Retomar patrulla desde el waypoint más cercano
        RoadWaypoint[] all = FindObjectsByType<RoadWaypoint>(FindObjectsSortMode.None);
        if (all.Length > 0)
        {
            currentWaypoint = FindClosestWaypoint(all);
            nav.SetDestination(currentWaypoint.transform.position);
        }
    }

    // ═════════════════════════════════════════════
    //  API PÚBLICA — Asignación de viajes
    // ═════════════════════════════════════════════

    /// <summary>
    /// Recibe una asignación de viaje del dispatcher.
    /// Actualiza las creencias y comienza la navegación al pickup.
    /// </summary>
    /// <param name="passenger">Pasajero asignado.</param>
    /// <param name="pickup">Posición de recogida.</param>
    /// <param name="dropoff">Posición de destino.</param>
    public void AssignTrip(PassengerAgent passenger, Vector3 pickup, Vector3 dropoff)
    {
        assignedPassenger = passenger;
        belief_pickupPos  = pickup;
        belief_dropoffPos = dropoff;
        state             = TaxiState.GoingToPassenger;

        Debug.Log($"[BDI-Taxi] {taxiId} Viaje asignado → {passenger.passengerId}");

        StartTripLeg(pickup, isPickup: true);
    }

    /// <summary>
    /// Configura la navegación hacia un destino (pickup o dropoff).
    /// Establece el tripTarget y redirige los waypoints.
    /// </summary>
    void StartTripLeg(Vector3 destination, bool isPickup)
    {
        goingToPickup = isPickup;
        tripTarget    = destination;
        finalApproach = false;
        state = isPickup ? TaxiState.GoingToPassenger : TaxiState.TransportingPassenger;

        // Redirigir al waypoint actual para que empiece a encaminarse
        if (hasWaypoints && currentWaypoint != null)
            nav.SetDestination(currentWaypoint.transform.position);
    }

    // ═════════════════════════════════════════════
    //  SISTEMA DE FRENADO
    // ═════════════════════════════════════════════

    /// <summary>
    /// Gestiona el frenado automático cuando hay un vehículo adelante.
    /// Si está bloqueado más de maxBrakeTime, avanza forzadamente.
    /// </summary>
    void HandleBraking()
    {
        float closestDist = DetectVehicleAheadDistance();

        if (closestDist < brakeDistance)
        {
            // Frenado gradual proporcional a la distancia
            float t = closestDist / brakeDistance;
            float targetSpeed = moveSpeed * t * t;
            targetSpeed = Mathf.Max(targetSpeed, moveSpeed * 0.05f);

            // Si está MUY cerca, detener completamente
            if (closestDist < brakeDistance * 0.25f)
                targetSpeed = 0f;

            nav.speed = Mathf.Lerp(nav.speed, targetSpeed, Time.deltaTime * 5f);

            if (!isBraking)
            {
                isBraking  = true;
                brakeTimer = 0f;
                if (state != TaxiState.Idle) state = TaxiState.Waiting;
            }
            brakeTimer += Time.deltaTime;
        }
        else if (isBraking)
        {
            isBraking  = false;
            brakeTimer = 0f;
            nav.speed  = moveSpeed;

            if (state == TaxiState.Waiting)
                state = tripTarget.HasValue
                    ? (goingToPickup ? TaxiState.GoingToPassenger : TaxiState.TransportingPassenger)
                    : TaxiState.Idle;
        }
    }

    /// <summary>
    /// Detecta si el taxi no se ha movido y lo desatasca.
    /// </summary>
    void DetectStuck()
    {
        movementCheckTimer += Time.deltaTime;
        if (movementCheckTimer >= 1f)
        {
            float distMoved = Vector3.Distance(transform.position, lastRecordedPos);
            lastRecordedPos = transform.position;
            movementCheckTimer = 0f;

            if (distMoved < 0.1f)
            {
                stuckTimer += 1f;
                if (stuckTimer >= stuckTimeout)
                {
                    UnstickTaxi();
                    stuckTimer = 0f;
                }
            }
            else
            {
                stuckTimer = 0f;
            }
        }
    }

    /// <summary>
    /// Desatasca el taxi: salta al waypoint más cercano y reinicia navegación.
    /// </summary>
    void UnstickTaxi()
    {
        nav.speed = moveSpeed;
        isBraking = false;

        // Intentar hacer warp a una posición libre cercana
        NavMeshHit hit;
        Vector3 nudge = transform.position + transform.forward * 1.5f;
        if (NavMesh.SamplePosition(nudge, out hit, 5f, NavMesh.AllAreas))
            nav.Warp(hit.position);

        // Re-adquirir waypoint más cercano
        RoadWaypoint[] all = FindObjectsByType<RoadWaypoint>(FindObjectsSortMode.None);
        if (all.Length > 0)
        {
            currentWaypoint = FindClosestWaypoint(all);
            nav.SetDestination(currentWaypoint.transform.position);
        }

        if (state == TaxiState.Waiting)
            state = tripTarget.HasValue
                ? (goingToPickup ? TaxiState.GoingToPassenger : TaxiState.TransportingPassenger)
                : TaxiState.Idle;

        Debug.Log($"[BDI-Taxi] {taxiId} desatascado.");
    }

    /// <summary>
    /// Detecta la distancia al vehículo más cercano adelante.
    /// Usa dot-product para solo detectar vehículos realmente en el camino,
    /// ignorando los que circulan por carriles paralelos.
    /// </summary>
    float DetectVehicleAheadDistance()
    {
        float closest = float.MaxValue;
        Vector3 origin = transform.position + Vector3.up * 0.5f;

        RaycastHit[] hits = Physics.SphereCastAll(
            origin, brakeWidth, transform.forward, brakeDistance);

        foreach (var hit in hits)
        {
            if (hit.collider.gameObject == gameObject) continue;

            NavMeshAgent otherNav = hit.collider.GetComponent<NavMeshAgent>();
            if (otherNav == null) continue;

            // Verificar que está realmente adelante (arco de ~60°)
            Vector3 toOther = (hit.collider.transform.position - transform.position).normalized;
            float dot = Vector3.Dot(transform.forward, toOther);
            if (dot > 0.5f)
                closest = Mathf.Min(closest, hit.distance);
        }

        return closest;
    }

    /// <summary>
    /// Wrapper de compatibilidad para el ciclo BDI.
    /// </summary>
    bool DetectVehicleAhead()
    {
        return DetectVehicleAheadDistance() < brakeDistance;
    }

    // ═════════════════════════════════════════════
    //  UTILIDADES
    // ═════════════════════════════════════════════

    /// <summary>
    /// Rotación suave del modelo 3D en la dirección de movimiento.
    /// Compensar el angularSpeed=0 del NavMeshAgent para apariencia natural.
    /// </summary>
    void SmoothRotation()
    {
        Vector3 vel = nav.velocity;
        if (vel.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation,
                                              Quaternion.LookRotation(vel.normalized),
                                              rotationSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Encuentra el waypoint más cercano a la posición actual del taxi.
    /// </summary>
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

    // ═════════════════════════════════════════════
    //  GIZMOS — Visualización en el editor
    // ═════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        // Punto de recogida (cian)
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(belief_pickupPos, 0.5f);

        // Punto de destino (magenta)
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(belief_dropoffPos, 0.5f);

        // Destino de navegación actual (amarillo)
        if (tripTarget.HasValue)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(tripTarget.Value, 0.8f);
        }

        // Rayo de detección de frenado (rojo)
        Gizmos.color = Color.red;
        Gizmos.DrawLine(
            transform.position + Vector3.up * 0.5f,
            transform.position + Vector3.up * 0.5f + transform.forward * brakeDistance);
    }
}

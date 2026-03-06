// ============================================================================
// DispatcherAgent.cs — Agente Despachador con Arquitectura BDI
// ============================================================================
//
// DESCRIPCIÓN:
//   Agente despachador que coordina la asignación de taxis a pasajeros
//   usando el modelo BDI. Reemplaza al FleetDispatcher original con
//   una arquitectura formal de creencias, deseos e intenciones.
//
// CICLO BDI DEL DISPATCHER:
//
//   Percepción    → Detecta taxis, pasajeros, congestión
//   Creencias     → Actualiza listas de taxis disponibles, solicitudes pendientes
//   Deseos        → Minimizar espera, maximizar utilización, reducir congestión
//   Intenciones   → Asignar taxi más cercano, reasignar si hay congestión
//   Ejecución     → Ejecuta la asignación seleccionada
//
// INTERACCIÓN CON OTROS AGENTES:
//
//   PassengerAgent ──solicitud──▶ DispatcherAgent ──asignación──▶ TaxiAgent
//                                       ▲                            │
//                                       └────────disponible──────────┘
//
// USO EN UNITY:
//   1. Crear un GameObject vacío llamado "Dispatcher"
//   2. Agregar este componente
//   3. Los taxis se registran automáticamente al iniciar
//
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Agente despachador BDI que coordina la asignación de taxis a solicitudes
/// de transporte, tomando decisiones basadas en el modelo Belief-Desire-Intention.
/// </summary>
public class DispatcherAgent : BDIAgent
{
    // ═════════════════════════════════════════════
    //  CREENCIAS (Beliefs) — Representación del mundo
    // ═════════════════════════════════════════════

    /// <summary>Lista de todos los taxis registrados en el sistema.</summary>
    private List<TaxiAgent> belief_allTaxis = new List<TaxiAgent>();

    /// <summary>Lista de taxis actualmente disponibles (estado Idle).</summary>
    private List<TaxiAgent> belief_availableTaxis = new List<TaxiAgent>();

    /// <summary>Diccionario de posición actual de cada taxi.</summary>
    private Dictionary<TaxiAgent, Vector3> belief_taxiPositions
        = new Dictionary<TaxiAgent, Vector3>();

    /// <summary>Cola de solicitudes pendientes sin asignar.</summary>
    private List<TaxiRequest> belief_pendingRequests = new List<TaxiRequest>();

    /// <summary>Lista de solicitudes activas (asignadas o en progreso).</summary>
    private List<TaxiRequest> belief_activeRequests = new List<TaxiRequest>();

    /// <summary>Nivel de congestión global percibido.</summary>
    private float belief_globalCongestion = 0f;

    /// <summary>Número total de viajes completados.</summary>
    private int belief_tripsCompleted = 0;

    // ═════════════════════════════════════════════
    //  DESEOS (Desires) — Objetivos que persigue el agente
    // ═════════════════════════════════════════════

    /// <summary>
    /// Enumeración de los deseos posibles del despachador.
    /// Cada deseo tiene una prioridad asociada que la deliberación evalúa.
    /// </summary>
    public enum DispatcherDesire
    {
        /// <summary>Asignar taxis a solicitudes pendientes para minimizar espera.</summary>
        MinimizeWaitTime,
        /// <summary>Maximizar la utilización de la flota de taxis.</summary>
        MaximizeTaxiUtilization,
        /// <summary>Reasignar taxis a zonas menos congestionadas.</summary>
        ReduceCongestion
    }

    /// <summary>Deseo de mayor prioridad en este ciclo.</summary>
    private DispatcherDesire desire_current = DispatcherDesire.MinimizeWaitTime;

    /// <summary>Prioridad del deseo actual (0=mínima, 1=máxima).</summary>
    private float desire_priority = 0f;

    // ═════════════════════════════════════════════
    //  INTENCIONES (Intentions) — Plan de acción seleccionado
    // ═════════════════════════════════════════════

    /// <summary>
    /// Acciones concretas que el despachador puede ejecutar.
    /// </summary>
    public enum DispatcherIntention
    {
        /// <summary>No hay acción necesaria (todo atendido).</summary>
        Idle,
        /// <summary>Asignar el taxi más cercano a una solicitud pendiente.</summary>
        AssignNearestTaxi,
        /// <summary>Reasignar taxis a zonas de menor congestión.</summary>
        RebalanceFleet,
        /// <summary>Priorizar la solicitud más antigua.</summary>
        PrioritizeOldestRequest
    }

    /// <summary>Intención seleccionada para ejecutar en este ciclo.</summary>
    private DispatcherIntention intention_current = DispatcherIntention.Idle;

    /// <summary>Solicitud objetivo de la intención actual.</summary>
    private TaxiRequest intention_targetRequest = null;

    /// <summary>Taxi seleccionado para la intención actual.</summary>
    private TaxiAgent intention_targetTaxi = null;

    // ═════════════════════════════════════════════
    //  CONFIGURACIÓN
    // ═════════════════════════════════════════════

    [Header("Despachador — Configuración")]
    [Tooltip("Cada cuántos segundos intenta reasignar solicitudes pendientes.")]
    public float retryInterval = 3f;

    [Tooltip("Tiempo máximo de espera (seg) antes de escalar la prioridad de una solicitud.")]
    public float escalationThreshold = 15f;

    // ═════════════════════════════════════════════
    //  ESTADO VISIBLE EN INSPECTOR
    // ═════════════════════════════════════════════

    public enum DispatcherState
    {
        Monitoreando,
        ProcesandoSolicitud,
        AsignandoTaxi,
        SupervisandoViaje
    }

    [Header("Estado")]
    public DispatcherState dispatcherState = DispatcherState.Monitoreando;

    // ═════════════════════════════════════════════
    //  INICIALIZACIÓN
    // ═════════════════════════════════════════════

    void Start()
    {
        agentName = "Dispatcher";
        bdiTickInterval = 0.5f; // El dispatcher delibera cada 0.5s

        // Registrar todos los taxis existentes en la escena
        foreach (var taxi in FindObjectsByType<TaxiAgent>(FindObjectsSortMode.None))
            RegisterTaxi(taxi);

        Debug.Log($"[BDI-Dispatcher] Iniciado con {belief_allTaxis.Count} taxis registrados.");
    }

    // ═════════════════════════════════════════════
    //  IMPLEMENTACIÓN DEL CICLO BDI
    // ═════════════════════════════════════════════

    /// <summary>
    /// PASO 1: Percepción del entorno.
    /// El dispatcher observa el estado de todos los taxis, solicitudes
    /// pendientes y nivel de congestión global.
    /// </summary>
    protected override void PerceiveEnvironment()
    {
        // Percibir posiciones actuales de los taxis
        belief_taxiPositions.Clear();
        foreach (var taxi in belief_allTaxis)
        {
            if (taxi != null)
                belief_taxiPositions[taxi] = taxi.transform.position;
        }

        // Percibir nivel de congestión global
        if (TrafficManager.Instance != null)
        {
            belief_globalCongestion = 0f;
            int samples = 0;
            foreach (var taxi in belief_allTaxis)
            {
                if (taxi == null) continue;
                belief_globalCongestion += TrafficManager.Instance
                    .GetCongestionValue(taxi.transform.position);
                samples++;
            }
            if (samples > 0)
                belief_globalCongestion /= samples;
        }

        if (debugBDI)
            Debug.Log($"[BDI-Dispatcher] Percepción: {belief_allTaxis.Count} taxis, " +
                      $"{belief_pendingRequests.Count} solicitudes pendientes, " +
                      $"congestión={belief_globalCongestion:F2}");
    }

    /// <summary>
    /// PASO 2: Actualización de creencias.
    /// Actualiza la lista de taxis disponibles y limpia solicitudes obsoletas.
    /// </summary>
    protected override void UpdateBeliefs()
    {
        // Actualizar lista de taxis disponibles
        belief_availableTaxis.Clear();
        foreach (var taxi in belief_allTaxis)
        {
            if (taxi != null && taxi.state == TaxiAgent.TaxiState.Idle)
                belief_availableTaxis.Add(taxi);
        }

        // Limpiar solicitudes completadas o canceladas de la lista activa
        belief_activeRequests.RemoveAll(r =>
            r.status == TaxiRequest.RequestStatus.Completed ||
            r.status == TaxiRequest.RequestStatus.Cancelled);

        // Limpiar solicitudes pendientes cuyo pasajero ya no existe
        belief_pendingRequests.RemoveAll(r =>
            r.passenger == null || !r.passenger.gameObject.activeInHierarchy);

        if (debugBDI)
            Debug.Log($"[BDI-Dispatcher] Creencias: {belief_availableTaxis.Count} taxis disponibles, " +
                      $"{belief_pendingRequests.Count} pendientes, {belief_activeRequests.Count} activas");
    }

    /// <summary>
    /// PASO 3: Generación de deseos.
    /// El dispatcher evalúa qué desea lograr basándose en sus creencias.
    /// </summary>
    protected override void GenerateDesires()
    {
        // ── Deseo 1: Minimizar tiempo de espera ──
        // Si hay solicitudes pendientes, este es el deseo principal
        if (belief_pendingRequests.Count > 0 && belief_availableTaxis.Count > 0)
        {
            desire_current  = DispatcherDesire.MinimizeWaitTime;
            desire_priority = 1.0f; // Máxima prioridad

            // Escalar prioridad si hay solicitudes antiguas
            foreach (var req in belief_pendingRequests)
            {
                if (req.ElapsedTime() > escalationThreshold)
                {
                    desire_priority = 1.0f; // ¡Urgente!
                    break;
                }
            }
            return;
        }

        // ── Deseo 2: Reducir congestión ──
        // Si la congestión es alta y hay taxis disponibles
        if (belief_globalCongestion > 0.6f && belief_availableTaxis.Count > 1)
        {
            desire_current  = DispatcherDesire.ReduceCongestion;
            desire_priority = 0.7f;
            return;
        }

        // ── Deseo 3: Maximizar utilización ──
        // Si hay muchos taxis ociosos, redistribuir
        float utilizationRate = belief_allTaxis.Count > 0
            ? 1f - (float)belief_availableTaxis.Count / belief_allTaxis.Count
            : 1f;

        if (utilizationRate < 0.5f)
        {
            desire_current  = DispatcherDesire.MaximizeTaxiUtilization;
            desire_priority = 0.3f;
            return;
        }

        // Sin deseos urgentes
        desire_current  = DispatcherDesire.MinimizeWaitTime;
        desire_priority = 0f;
    }

    /// <summary>
    /// PASO 4: Selección de intenciones (deliberación).
    /// Elige la acción concreta a ejecutar basándose en el deseo prioritario.
    /// </summary>
    protected override void SelectIntentions()
    {
        intention_targetRequest = null;
        intention_targetTaxi    = null;

        switch (desire_current)
        {
            case DispatcherDesire.MinimizeWaitTime:
                if (belief_pendingRequests.Count > 0 && belief_availableTaxis.Count > 0)
                {
                    // Seleccionar la solicitud más antigua (FIFO con prioridad)
                    intention_targetRequest = GetHighestPriorityRequest();

                    if (intention_targetRequest != null)
                    {
                        // Seleccionar el taxi más cercano al pickup
                        intention_targetTaxi = SelectBestTaxi(
                            intention_targetRequest.pickupPosition);

                        intention_current = intention_targetTaxi != null
                            ? DispatcherIntention.AssignNearestTaxi
                            : DispatcherIntention.Idle;
                    }
                    else
                    {
                        intention_current = DispatcherIntention.Idle;
                    }
                }
                else
                {
                    intention_current = DispatcherIntention.Idle;
                }
                break;

            case DispatcherDesire.ReduceCongestion:
                intention_current = DispatcherIntention.RebalanceFleet;
                break;

            case DispatcherDesire.MaximizeTaxiUtilization:
                intention_current = DispatcherIntention.Idle;
                break;
        }

        if (debugBDI && intention_current != DispatcherIntention.Idle)
            Debug.Log($"[BDI-Dispatcher] Intención: {intention_current} " +
                      $"(deseo={desire_current}, prioridad={desire_priority:F2})");
    }

    /// <summary>
    /// PASO 5: Ejecución de intenciones.
    /// Realiza la acción seleccionada durante la deliberación.
    /// </summary>
    protected override void ExecuteIntentions()
    {
        switch (intention_current)
        {
            case DispatcherIntention.AssignNearestTaxi:
                if (intention_targetTaxi != null && intention_targetRequest != null)
                {
                    AssignTaxiToRequest(intention_targetTaxi, intention_targetRequest);
                }
                break;

            case DispatcherIntention.RebalanceFleet:
                // En esta versión base, la reasignación se limita a intentar
                // asignar solicitudes pendientes a taxis disponibles.
                // Extensible para redistribuir taxis ociosos a zonas estratégicas.
                TryAssignPendingRequests();
                break;

            case DispatcherIntention.PrioritizeOldestRequest:
                TryAssignPendingRequests();
                break;

            case DispatcherIntention.Idle:
                dispatcherState = DispatcherState.Monitoreando;
                break;
        }
    }

    // ═════════════════════════════════════════════
    //  API PÚBLICA — Interacción con otros agentes
    // ═════════════════════════════════════════════

    /// <summary>
    /// Registra un taxi en la flota del despachador.
    /// Llamado automáticamente por cada TaxiAgent al iniciar.
    /// </summary>
    public void RegisterTaxi(TaxiAgent taxi)
    {
        if (taxi != null && !belief_allTaxis.Contains(taxi))
        {
            belief_allTaxis.Add(taxi);
            Debug.Log($"[BDI-Dispatcher] Taxi registrado: {taxi.taxiId}");
        }
    }

    /// <summary>
    /// Recibe una solicitud de transporte de un pasajero.
    /// Retorna true si se asignó un taxi de inmediato.
    /// </summary>
    public bool ReceiveRequest(PassengerAgent passenger, Vector3 pickup, Vector3 dropoff)
    {
        dispatcherState = DispatcherState.ProcesandoSolicitud;

        // Crear nueva solicitud con modelo TaxiRequest
        TaxiRequest request = new TaxiRequest(passenger, pickup, dropoff);

        Debug.Log($"[BDI-Dispatcher] Nueva solicitud: {request.requestId}");

        // Intentar asignación inmediata (fuera del ciclo BDI para respuesta rápida)
        TaxiAgent best = SelectBestTaxi(pickup);

        if (best != null)
        {
            AssignTaxiToRequest(best, request);
            dispatcherState = DispatcherState.SupervisandoViaje;
            return true;
        }

        // Sin taxi disponible → encolar para el ciclo BDI
        belief_pendingRequests.Add(request);
        dispatcherState = DispatcherState.Monitoreando;
        Debug.Log($"[BDI-Dispatcher] Sin taxis disponibles. Cola: {belief_pendingRequests.Count}");
        return false;
    }

    /// <summary>
    /// Notifica al dispatcher que un taxi está disponible para nuevas asignaciones.
    /// Llamado por TaxiAgent al completar un viaje.
    /// </summary>
    public void OnTaxiAvailable(TaxiAgent taxi)
    {
        Debug.Log($"[BDI-Dispatcher] {taxi.taxiId} disponible.");
        belief_tripsCompleted++;

        // Intentar asignar solicitudes pendientes inmediatamente
        TryAssignPendingRequests();
    }

    // ═════════════════════════════════════════════
    //  LÓGICA DE ASIGNACIÓN
    // ═════════════════════════════════════════════

    /// <summary>
    /// Selecciona el taxi disponible más cercano a una posición.
    /// Considera la congestión en la ruta si TrafficManager está disponible.
    /// </summary>
    private TaxiAgent SelectBestTaxi(Vector3 pickupPos)
    {
        TaxiAgent best      = null;
        float     bestScore = float.MaxValue;

        foreach (var taxi in belief_allTaxis)
        {
            if (taxi == null) continue;
            if (taxi.state != TaxiAgent.TaxiState.Idle) continue;

            float distance = Vector3.Distance(taxi.transform.position, pickupPos);

            // Penalizar ligeramente si el taxi está en zona congestionada
            float congestionPenalty = 0f;
            if (TrafficManager.Instance != null)
                congestionPenalty = TrafficManager.Instance
                    .GetCongestionValue(taxi.transform.position) * 5f;

            float score = distance + congestionPenalty;

            if (score < bestScore)
            {
                bestScore = score;
                best      = taxi;
            }
        }

        return best;
    }

    /// <summary>
    /// Asigna un taxi a una solicitud específica.
    /// </summary>
    private void AssignTaxiToRequest(TaxiAgent taxi, TaxiRequest request)
    {
        dispatcherState = DispatcherState.AsignandoTaxi;

        // Actualizar modelo de solicitud
        request.Assign(taxi);
        belief_activeRequests.Add(request);
        belief_pendingRequests.Remove(request);

        // Notificar al taxi
        Debug.Log($"[BDI-Dispatcher] Asignando {taxi.taxiId} → {request.passenger.passengerId}");
        taxi.AssignTrip(request.passenger, request.pickupPosition, request.destinationPosition);

        dispatcherState = DispatcherState.SupervisandoViaje;
    }

    /// <summary>
    /// Intenta asignar taxis a todas las solicitudes pendientes.
    /// Ejecutado cuando un taxi se libera o periódicamente por el ciclo BDI.
    /// </summary>
    private void TryAssignPendingRequests()
    {
        // Trabajar con una copia para iterar de forma segura
        var pending = new List<TaxiRequest>(belief_pendingRequests);

        foreach (var request in pending)
        {
            if (request.passenger == null || !request.passenger.gameObject.activeInHierarchy)
            {
                belief_pendingRequests.Remove(request);
                continue;
            }

            TaxiAgent best = SelectBestTaxi(request.pickupPosition);
            if (best != null)
            {
                AssignTaxiToRequest(best, request);
            }
            else
            {
                break; // No hay más taxis disponibles
            }
        }
    }

    /// <summary>
    /// Obtiene la solicitud pendiente con mayor prioridad.
    /// Las solicitudes más antiguas tienen mayor prioridad.
    /// Solicitudes que superan el umbral de escalación son urgentes.
    /// </summary>
    private TaxiRequest GetHighestPriorityRequest()
    {
        if (belief_pendingRequests.Count == 0) return null;

        TaxiRequest best      = null;
        float       bestScore = float.MinValue;

        foreach (var req in belief_pendingRequests)
        {
            if (req.passenger == null || !req.passenger.gameObject.activeInHierarchy)
                continue;

            // Puntaje basado en tiempo de espera (más espera = mayor prioridad)
            float score = req.ElapsedTime();

            // Bonus si supera umbral de escalación
            if (score > escalationThreshold)
                score *= 2f;

            if (score > bestScore)
            {
                bestScore = score;
                best      = req;
            }
        }

        return best;
    }

    // ═════════════════════════════════════════════
    //  GIZMOS — Visualización en el editor
    // ═════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        // Dibujar líneas hacia cada taxi registrado
        Gizmos.color = Color.blue;
        foreach (var taxi in belief_allTaxis)
            if (taxi != null)
                Gizmos.DrawLine(transform.position, taxi.transform.position);

        // Dibujar solicitudes pendientes en rojo
        Gizmos.color = Color.red;
        foreach (var req in belief_pendingRequests)
            Gizmos.DrawWireSphere(req.pickupPosition, 1f);
    }
}

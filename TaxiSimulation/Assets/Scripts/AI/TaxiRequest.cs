// ============================================================================
// TaxiRequest.cs — Modelo de Datos para Solicitudes de Transporte
// ============================================================================
//
// DESCRIPCIÓN:
//   Representa una solicitud de viaje generada por un pasajero.
//   Contiene toda la información necesaria para que el Dispatcher
//   pueda asignar un taxi y para que el taxi pueda ejecutar el viaje.
//
// DIAGRAMA DE ESTADOS DE UNA SOLICITUD:
//
//   [Pending] ──asignar──▶ [Assigned] ──pickup──▶ [InProgress]
//       │                       │                       │
//       ▼                       ▼                       ▼
//   [Cancelled]            [Cancelled]            [Completed]
//
// USO EN UNITY:
//   Esta clase NO es un MonoBehaviour, no se agrega a GameObjects.
//   Se usa como objeto de datos que el DispatcherAgent y TaxiAgent
//   intercambian para coordinar las asignaciones.
//
// EJEMPLO:
//   TaxiRequest req = new TaxiRequest(passenger, pickupPos, dropoffPos);
//   dispatcher.ReceiveRequest(req);
//
// ============================================================================

using UnityEngine;

/// <summary>
/// Modelo de datos que representa una solicitud de transporte.
/// Contiene la información completa del viaje: origen, destino,
/// pasajero, taxi asignado y estado de la solicitud.
/// </summary>
[System.Serializable]
public class TaxiRequest
{
    // ─────────────────────────────────────────────
    // ESTADO DE LA SOLICITUD
    // ─────────────────────────────────────────────

    /// <summary>
    /// Estados posibles de una solicitud de transporte.
    /// </summary>
    public enum RequestStatus
    {
        /// <summary>Solicitud creada, esperando asignación de taxi.</summary>
        Pending,
        /// <summary>Un taxi ha sido asignado y se dirige al punto de recogida.</summary>
        Assigned,
        /// <summary>El pasajero ha sido recogido y está en camino al destino.</summary>
        InProgress,
        /// <summary>El viaje fue completado exitosamente.</summary>
        Completed,
        /// <summary>La solicitud fue cancelada (timeout u otra razón).</summary>
        Cancelled
    }

    // ─────────────────────────────────────────────
    // DATOS DEL VIAJE
    // ─────────────────────────────────────────────

    /// <summary>Identificador único de la solicitud.</summary>
    public string requestId;

    /// <summary>Posición de recogida del pasajero (pickup).</summary>
    public Vector3 pickupPosition;

    /// <summary>Posición de destino del pasajero (dropoff).</summary>
    public Vector3 destinationPosition;

    /// <summary>Momento (Time.time) en que se creó la solicitud.</summary>
    public float requestTime;

    /// <summary>Estado actual de la solicitud.</summary>
    public RequestStatus status;

    // ─────────────────────────────────────────────
    // REFERENCIAS A AGENTES
    // ─────────────────────────────────────────────

    /// <summary>Referencia al pasajero que generó la solicitud.</summary>
    public PassengerAgent passenger;

    /// <summary>Referencia al taxi asignado (null si no se ha asignado).</summary>
    public TaxiAgent assignedTaxi;

    // ─────────────────────────────────────────────
    // MÉTRICAS DEL VIAJE
    // ─────────────────────────────────────────────

    /// <summary>Tiempo de espera del pasajero antes de la recogida.</summary>
    public float waitTime;

    /// <summary>Tiempo total del viaje (desde recogida hasta destino).</summary>
    public float tripDuration;

    // ─────────────────────────────────────────────
    // CONSTRUCTOR
    // ─────────────────────────────────────────────

    /// <summary>
    /// Crea una nueva solicitud de transporte.
    /// </summary>
    /// <param name="passenger">Pasajero que solicita el viaje.</param>
    /// <param name="pickup">Posición de recogida.</param>
    /// <param name="destination">Posición de destino.</param>
    public TaxiRequest(PassengerAgent passenger, Vector3 pickup, Vector3 destination)
    {
        this.requestId           = $"REQ_{passenger.passengerId}_{Time.time:F0}";
        this.passenger           = passenger;
        this.pickupPosition      = pickup;
        this.destinationPosition = destination;
        this.requestTime         = Time.time;
        this.status              = RequestStatus.Pending;
        this.assignedTaxi        = null;
        this.waitTime            = 0f;
        this.tripDuration        = 0f;
    }

    // ─────────────────────────────────────────────
    // MÉTODOS DE CICLO DE VIDA
    // ─────────────────────────────────────────────

    /// <summary>
    /// Marca la solicitud como asignada a un taxi específico.
    /// </summary>
    public void Assign(TaxiAgent taxi)
    {
        assignedTaxi = taxi;
        status       = RequestStatus.Assigned;
    }

    /// <summary>
    /// Marca la solicitud como en progreso (pasajero recogido).
    /// </summary>
    public void StartTrip()
    {
        waitTime = Time.time - requestTime;
        status   = RequestStatus.InProgress;
    }

    /// <summary>
    /// Marca la solicitud como completada (pasajero llegó al destino).
    /// </summary>
    public void Complete()
    {
        tripDuration = Time.time - requestTime - waitTime;
        status       = RequestStatus.Completed;
    }

    /// <summary>
    /// Marca la solicitud como cancelada.
    /// </summary>
    public void Cancel()
    {
        status = RequestStatus.Cancelled;
    }

    /// <summary>
    /// Calcula la distancia en línea recta entre pickup y destino.
    /// Útil para estimar el tiempo del viaje.
    /// </summary>
    public float EstimatedTripDistance()
    {
        return Vector3.Distance(pickupPosition, destinationPosition);
    }

    /// <summary>
    /// Tiempo transcurrido desde que se creó la solicitud.
    /// </summary>
    public float ElapsedTime()
    {
        return Time.time - requestTime;
    }

    public override string ToString()
    {
        return $"[{requestId}] Estado={status}, Pasajero={passenger?.passengerId}, " +
               $"Taxi={assignedTaxi?.taxiId ?? "N/A"}";
    }
}

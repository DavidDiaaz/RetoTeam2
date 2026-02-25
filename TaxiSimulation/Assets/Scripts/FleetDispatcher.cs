using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FleetDispatcher — Arquitectura Deliberativa BDI
/// 
/// Creencias : lista de taxis y sus estados, solicitudes activas.
/// Deseos    : asignar el taxi más cercano a cada solicitud.
/// Intenciones: ejecutar SelectBestTaxi() y AssignTrip() en cada solicitud recibida.
/// </summary>
public class FleetDispatcher : MonoBehaviour
{
    // ── BDI – Creencias: flota registrada ────────────────────────────────────
    private List<TaxiAgent> allTaxis = new List<TaxiAgent>();

    // ── Cola de solicitudes pendientes ────────────────────────────────────────
    private Queue<TripRequest> pendingRequests = new Queue<TripRequest>();

    // ── Estado del despachador ────────────────────────────────────────────────
    public enum DispatcherState { Monitoreando, ProcesandoSolicitud, AsignandoTaxi, SupervisandoViaje }
    [Header("Estado")]
    public DispatcherState dispatcherState = DispatcherState.Monitoreando;

    // ── Parámetros ────────────────────────────────────────────────────────────
    [Header("Configuración")]
    [Tooltip("Cada cuántos segundos el despachador intenta reasignar solicitudes pendientes")]
    public float retryInterval = 3f;

    // ─────────────────────────────────────────────────────────────────────────
    void Start()
    {
        // Auto-registrar taxis que ya existan en la escena
        foreach (var taxi in FindObjectsOfType<TaxiAgent>())
            RegisterTaxi(taxi);

        StartCoroutine(RetryPendingRequests());
    }

    // ── Registro de taxis ──────────────────────────────────────────────────────
    public void RegisterTaxi(TaxiAgent taxi)
    {
        if (!allTaxis.Contains(taxi))
        {
            allTaxis.Add(taxi);
            Debug.Log($"[Despachador] Taxi registrado: {taxi.taxiId}");
        }
    }

    // ── Recibir solicitud de pasajero ──────────────────────────────────────────
    /// <returns>true si se asignó un taxi de inmediato</returns>
    public bool ReceiveRequest(PassengerAgent passenger, Vector3 pickup, Vector3 dropoff)
    {
        dispatcherState = DispatcherState.ProcesandoSolicitud;
        Debug.Log($"[Despachador] Solicitud recibida de {passenger.passengerId}");

        TaxiAgent best = SelectBestTaxi(pickup);

        if (best != null)
        {
            AssignTrip(best, passenger, pickup, dropoff);
            dispatcherState = DispatcherState.SupervisandoViaje;
            return true;
        }

        // Sin taxi disponible: encolar para reintentar
        pendingRequests.Enqueue(new TripRequest(passenger, pickup, dropoff));
        dispatcherState = DispatcherState.Monitoreando;
        Debug.Log($"[Despachador] Sin taxis disponibles. Solicitud en cola. ({pendingRequests.Count} pendientes)");
        return false;
    }

    // ── BDI – SelectBestTaxi (deliberación) ───────────────────────────────────
    /// <summary>
    /// Selecciona el taxi disponible más cercano al punto de recogida.
    /// Criterio simple de distancia euclidiana (sustituir por A* en versión avanzada).
    /// </summary>
    TaxiAgent SelectBestTaxi(Vector3 pickupPos)
    {
        TaxiAgent best     = null;
        float     bestDist = float.MaxValue;

        foreach (var taxi in allTaxis)
        {
            if (taxi.state != TaxiAgent.TaxiState.Disponible) continue;

            float d = Vector3.Distance(taxi.transform.position, pickupPos);
            if (d < bestDist)
            {
                bestDist = d;
                best     = taxi;
            }
        }

        return best;
    }

    // ── BDI – AssignTrip (intención) ───────────────────────────────────────────
    void AssignTrip(TaxiAgent taxi, PassengerAgent passenger, Vector3 pickup, Vector3 dropoff)
    {
        dispatcherState = DispatcherState.AsignandoTaxi;
        Debug.Log($"[Despachador] Asignando {taxi.taxiId} a {passenger.passengerId}");
        taxi.AssignTrip(passenger, pickup, dropoff);
    }

    // ── Taxi vuelve a estar disponible ─────────────────────────────────────────
    public void OnTaxiAvailable(TaxiAgent taxi)
    {
        Debug.Log($"[Despachador] {taxi.taxiId} disponible de nuevo.");
        // Intentar asignar solicitudes pendientes inmediatamente
        TryAssignPendingRequests();
    }

    // ── Reintento periódico de solicitudes pendientes ─────────────────────────
    IEnumerator RetryPendingRequests()
    {
        while (true)
        {
            yield return new WaitForSeconds(retryInterval);
            TryAssignPendingRequests();
        }
    }

    void TryAssignPendingRequests()
    {
        int attempts = pendingRequests.Count;
        for (int i = 0; i < attempts; i++)
        {
            TripRequest req  = pendingRequests.Dequeue();
            TaxiAgent   best = SelectBestTaxi(req.pickup);

            if (best != null)
            {
                AssignTrip(best, req.passenger, req.pickup, req.dropoff);
            }
            else
            {
                pendingRequests.Enqueue(req); // volver a encolar
                break;                        // sin taxis libres, parar
            }
        }
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        foreach (var taxi in allTaxis)
        {
            if (taxi != null)
                Gizmos.DrawLine(transform.position, taxi.transform.position);
        }
    }

    // ── Clase auxiliar interna ────────────────────────────────────────────────
    private class TripRequest
    {
        public PassengerAgent passenger;
        public Vector3        pickup;
        public Vector3        dropoff;

        public TripRequest(PassengerAgent p, Vector3 pk, Vector3 dr)
        {
            passenger = p; pickup = pk; dropoff = dr;
        }
    }
}

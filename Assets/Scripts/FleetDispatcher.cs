using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FleetDispatcher : MonoBehaviour
{
    private List<TaxiAgent> allTaxis = new List<TaxiAgent>();
    private Queue<TripRequest> pendingRequests = new Queue<TripRequest>();

    public enum DispatcherState { Monitoreando, ProcesandoSolicitud, AsignandoTaxi, SupervisandoViaje }
    [Header("Estado")]
    public DispatcherState dispatcherState = DispatcherState.Monitoreando;

    [Header("Configuración")]
    [Tooltip("Cada cuántos segundos intenta reasignar solicitudes pendientes")]
    public float retryInterval = 3f;

    void Start()
    {
        foreach (var taxi in FindObjectsByType<TaxiAgent>(FindObjectsSortMode.None))
            RegisterTaxi(taxi);

        StartCoroutine(RetryPendingRequests());
    }

    public void RegisterTaxi(TaxiAgent taxi)
    {
        if (!allTaxis.Contains(taxi))
        {
            allTaxis.Add(taxi);
            Debug.Log($"[Despachador] Taxi registrado: {taxi.taxiId}");
        }
    }

    // retorna true si se asignó un taxi de inmediato
    public bool ReceiveRequest(PassengerAgent passenger, Vector3 pickup, Vector3 dropoff)
    {
        dispatcherState = DispatcherState.ProcesandoSolicitud;
        Debug.Log($"[Despachador] Solicitud de {passenger.passengerId}");

        TaxiAgent best = SelectBestTaxi(pickup);

        if (best != null)
        {
            AssignTrip(best, passenger, pickup, dropoff);
            dispatcherState = DispatcherState.SupervisandoViaje;
            return true;
        }

        // sin taxi disponible, encolar para reintentar
        pendingRequests.Enqueue(new TripRequest(passenger, pickup, dropoff));
        dispatcherState = DispatcherState.Monitoreando;
        Debug.Log($"[Despachador] Sin taxis. Cola: {pendingRequests.Count}");
        return false;
    }

    // elige el taxi disponible más cercano al pickup
    TaxiAgent SelectBestTaxi(Vector3 pickupPos)
    {
        TaxiAgent best = null;
        float bestDist = float.MaxValue;

        foreach (var taxi in allTaxis)
        {
            if (taxi.state != TaxiAgent.TaxiState.Disponible) continue;
            float d = Vector3.Distance(taxi.transform.position, pickupPos);
            if (d < bestDist) { bestDist = d; best = taxi; }
        }

        return best;
    }

    void AssignTrip(TaxiAgent taxi, PassengerAgent passenger, Vector3 pickup, Vector3 dropoff)
    {
        dispatcherState = DispatcherState.AsignandoTaxi;
        Debug.Log($"[Despachador] {taxi.taxiId} → {passenger.passengerId}");
        taxi.AssignTrip(passenger, pickup, dropoff);
    }

    public void OnTaxiAvailable(TaxiAgent taxi)
    {
        Debug.Log($"[Despachador] {taxi.taxiId} disponible.");
        TryAssignPendingRequests();
    }

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
            TripRequest req = pendingRequests.Dequeue();
            TaxiAgent best = SelectBestTaxi(req.pickup);

            if (best != null)
                AssignTrip(best, req.passenger, req.pickup, req.dropoff);
            else
            {
                pendingRequests.Enqueue(req);
                break; // no hay taxis libres, parar
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        foreach (var taxi in allTaxis)
            if (taxi != null)
                Gizmos.DrawLine(transform.position, taxi.transform.position);
    }

    private class TripRequest
    {
        public PassengerAgent passenger;
        public Vector3 pickup;
        public Vector3 dropoff;

        public TripRequest(PassengerAgent p, Vector3 pk, Vector3 dr)
        {
            passenger = p; pickup = pk; dropoff = dr;
        }
    }
}

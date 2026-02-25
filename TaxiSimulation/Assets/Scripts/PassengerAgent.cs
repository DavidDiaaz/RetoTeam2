using System.Collections;
using UnityEngine;

/// <summary>
/// PassengerAgent — Arquitectura Reactiva Simple
/// 
/// Genera una solicitud de viaje, espera al taxi, aborda y confirma llegada.
/// No planifica: solo reacciona a eventos del sistema.
/// </summary>
public class PassengerAgent : MonoBehaviour
{
    [Header("Identidad")]
    public string passengerId = "Pasajero_01";

    public enum PassengerState { Inactivo, SolicitandoViaje, EsperandoTaxi, EnViaje, ViajeCompletado, SolicitudCancelada }
    [Header("Estado")]
    public PassengerState state = PassengerState.Inactivo;

    [Header("Viaje")]
    public Transform pickupPoint;    // arrastra un GameObject vacío en el Inspector
    public Transform dropoffPoint;   // arrastra un GameObject vacío en el Inspector
    public float     maxWaitTime = 30f;  // segundos antes de cancelar

    private float waitTimer = 0f;

    private FleetDispatcher dispatcher;

    void Start()
    {
        dispatcher = FindObjectOfType<FleetDispatcher>();

        // Si los puntos no están asignados en el Inspector, usa la posición del GameObject
        if (pickupPoint  == null) pickupPoint  = transform;
        if (dropoffPoint == null) dropoffPoint = transform;

        // Iniciar solicitud después de un pequeño delay (simula llegada del pasajero)
        StartCoroutine(RequestTripAfterDelay(1f));
    }

    void Update()
    {
        // Control del tiempo de espera (solo mientras espera taxi)
        if (state == PassengerState.EsperandoTaxi)
        {
            waitTimer += Time.deltaTime;
            if (waitTimer >= maxWaitTime)
                CancelRequest();
        }
    }

    IEnumerator RequestTripAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        CreateRequest();
    }

    void CreateRequest()
    {
        if (pickupPoint == null || dropoffPoint == null)
        {
            Debug.LogWarning($"[{passengerId}] Faltan puntos de pickup/dropoff.");
            return;
        }

        state     = PassengerState.SolicitandoViaje;
        waitTimer = 0f;

        Debug.Log($"[{passengerId}] Solicitando viaje de {pickupPoint.position} a {dropoffPoint.position}");

        // Enviar solicitud al Despachador
        bool accepted = dispatcher != null &&
                        dispatcher.ReceiveRequest(this, pickupPoint.position, dropoffPoint.position);

        if (accepted)
        {
            state = PassengerState.EsperandoTaxi;
            Debug.Log($"[{passengerId}] Solicitud aceptada. Esperando taxi...");
        }
        else
        {
            Debug.Log($"[{passengerId}] No hay taxis disponibles. Esperando reintento...");
            // Reintentar después de 5 segundos
            StartCoroutine(RetryRequest(5f));
        }
    }

    IEnumerator RetryRequest(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (state != PassengerState.ViajeCompletado && state != PassengerState.SolicitudCancelada)
            CreateRequest();
    }

    void CancelRequest()
    {
        state = PassengerState.SolicitudCancelada;
        Debug.Log($"[{passengerId}] Tiempo de espera excedido. Solicitud cancelada.");
        // Ocultar visualmente al pasajero
        gameObject.SetActive(false);
    }

    public void OnTaxiArrived()
    {
        state = PassengerState.EnViaje;
        Debug.Log($"[{passengerId}] Taxi llegó. ¡En viaje!");
        // El pasajero "sube" al taxi: desactivamos su renderer para que no se vea en el suelo
        GetComponent<Renderer>()?.gameObject.SetActive(false);
    }

    public void OnTripCompleted()
    {
        state = PassengerState.ViajeCompletado;
        Debug.Log($"[{passengerId}] Viaje completado. ¡Destino alcanzado!");
        // Mover al pasajero al destino y mostrarlo
        transform.position = dropoffPoint.position;
        gameObject.SetActive(true);
        GetComponent<Renderer>()?.gameObject.SetActive(true);
    }

    void OnDrawGizmosSelected()
    {
        if (pickupPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(pickupPoint.position, 0.4f);
        }
        if (dropoffPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(dropoffPoint.position, 0.4f);
        }
        if (pickupPoint != null && dropoffPoint != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(pickupPoint.position, dropoffPoint.position);
        }
    }
}

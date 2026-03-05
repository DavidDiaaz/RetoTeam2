using System.Collections;
using UnityEngine;

public class PassengerAgent : MonoBehaviour
{
    [Header("Identidad")]
    public string passengerId = "Pasajero_01";

    public enum PassengerState { Inactivo, SolicitandoViaje, EsperandoTaxi, EnViaje, ViajeCompletado, SolicitudCancelada }
    [Header("Estado")]
    public PassengerState state = PassengerState.Inactivo;

    [Header("Viaje")]
    public Transform pickupPoint;
    public Transform dropoffPoint;
    public float maxWaitTime = 30f;

    private float waitTimer = 0f;
    private FleetDispatcher dispatcher;

    void Start()
    {
        dispatcher = FindFirstObjectByType<FleetDispatcher>();

        if (pickupPoint  == null) pickupPoint  = transform;
        if (dropoffPoint == null) dropoffPoint = transform;

        StartCoroutine(RequestTripAfterDelay(1f));
    }

    void Update()
    {
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

        state = PassengerState.SolicitandoViaje;
        waitTimer = 0f;

        bool accepted = dispatcher != null &&
                        dispatcher.ReceiveRequest(this, pickupPoint.position, dropoffPoint.position);

        if (accepted)
            state = PassengerState.EsperandoTaxi;
        else
            StartCoroutine(RetryRequest(5f));
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
        Debug.Log($"[{passengerId}] Cancelado por tiempo de espera.");
        gameObject.SetActive(false);
    }

    public void OnTaxiArrived()
    {
        state = PassengerState.EnViaje;
        GetComponent<Renderer>()?.gameObject.SetActive(false);
    }

    public void OnTripCompleted()
    {
        state = PassengerState.ViajeCompletado;
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

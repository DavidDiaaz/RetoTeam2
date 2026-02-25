using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SimController — Gestor de la simulación en tiempo de ejecución.
/// HUD ampliado y más legible.
/// </summary>
public class SimController : MonoBehaviour
{
    [Header("Generación dinámica de pasajeros")]
    public bool spawnPassengers = false;
    public float spawnInterval = 15f;
    public int maxPassengers = 6;

    [Header("Zona de spawn (deben estar en calles)")]
    public Vector3 spawnAreaMin = new Vector3(-20, 0.3f, -20);
    public Vector3 spawnAreaMax = new Vector3(20, 0.3f, 20);

    private int tripsCompleted = 0;
    private int tripsCancelled = 0;
    private int passengerCount = 0;

    private FleetDispatcher dispatcher;
    private TaxiAgent[] taxis;

    private GUIStyle boxStyle;
    private GUIStyle titleStyle;
    private GUIStyle bigLabelStyle;
    private GUIStyle taxiStyle;

    void Start()
    {
        dispatcher = FindObjectOfType<FleetDispatcher>();
        taxis = FindObjectsOfType<TaxiAgent>();

        if (spawnPassengers)
            StartCoroutine(SpawnPassengersRoutine());
    }

    IEnumerator SpawnPassengersRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(spawnInterval);

            if (passengerCount >= maxPassengers) continue;

            Vector3 pickup = RandomStreetPoint();
            Vector3 dropoff = RandomStreetPoint();

            int attempts = 0;
            while (Vector3.Distance(pickup, dropoff) < 8f && attempts++ < 10)
                dropoff = RandomStreetPoint();

            SpawnPassenger($"Pasajero_Auto_{passengerCount + 1}", pickup, dropoff);
            passengerCount++;
        }
    }

    Vector3 RandomStreetPoint()
    {
        bool horizontal = Random.value > 0.5f;
        float x = horizontal ? Random.Range(spawnAreaMin.x, spawnAreaMax.x) : 0f;
        float z = horizontal ? 0f : Random.Range(spawnAreaMin.z, spawnAreaMax.z);
        return new Vector3(x, 0.3f, z);
    }

    void SpawnPassenger(string id, Vector3 pickup, Vector3 dropoff)
    {
        GameObject p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        p.name = id;
        p.transform.position = pickup;
        p.transform.localScale = Vector3.one * 0.6f;

        Renderer r = p.GetComponent<Renderer>();
        if (r)
        {
            Material m = new Material(Shader.Find("Standard"));
            m.color = new Color(Random.value, Random.value, Random.value);
            r.material = m;
        }

        GameObject pkGO = new GameObject("Pickup");
        pkGO.transform.position = pickup;
        pkGO.transform.SetParent(p.transform);

        GameObject drGO = new GameObject("Dropoff");
        drGO.transform.position = dropoff;
        drGO.transform.SetParent(p.transform);

        PassengerAgent agent = p.AddComponent<PassengerAgent>();
        agent.passengerId = id;
        agent.pickupPoint = pkGO.transform;
        agent.dropoffPoint = drGO.transform;
        agent.maxWaitTime = 30f;

        Debug.Log($"[SimController] Pasajero generado: {id}");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            Time.timeScale = Time.timeScale > 0 ? 0f : 1f;

        if (Input.GetKeyDown(KeyCode.Alpha1)) Time.timeScale = 1f;
        if (Input.GetKeyDown(KeyCode.Alpha2)) Time.timeScale = 2f;
        if (Input.GetKeyDown(KeyCode.Alpha3)) Time.timeScale = 3f;
    }

    void OnGUI()
    {
        if (boxStyle == null) InitStyles();

        float panelWidth = 700;
        float panelHeight = 700;

        GUI.Box(new Rect(20, 20, panelWidth, panelHeight), "", boxStyle);

        GUI.Label(new Rect(40, 30, 400, 40),
            "🚖 ROBOTAXI SIMULATOR",
            titleStyle);

        GUI.Label(new Rect(40, 80, 380, 35),
            $"Velocidad: x{Time.timeScale:F0}",
            bigLabelStyle);

        GUI.Label(new Rect(40, 115, 380, 30),
            $"Viajes completados: {tripsCompleted}",
            bigLabelStyle);

        GUI.Label(new Rect(40, 145, 380, 30),
            $"Viajes cancelados: {tripsCancelled}",
            bigLabelStyle);

        GUI.Label(new Rect(40, 175, 380, 30),
            $"Pasajeros activos: {passengerCount}",
            bigLabelStyle);

        GUI.Label(new Rect(40, 215, 380, 30),
            "ESTADO DE TAXIS:",
            titleStyle);

        float y = 250;

        if (taxis != null)
        {
            foreach (var taxi in taxis)
            {
                if (taxi == null) continue;

                taxiStyle.normal.textColor = StateToColor(taxi.state);

                GUI.Label(new Rect(50, y, 350, 30),
                    $"• {taxi.taxiId} → {taxi.state}",
                    taxiStyle);

                y += 30;
            }
        }

        GUI.Label(new Rect(20, Screen.height - 40, 600, 30),
            "SPACE = Pausa | 1/2/3 = Velocidad",
            bigLabelStyle);
    }

    void InitStyles()
    {
        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = MakeTex(2, 2, new Color(0, 0, 0, 0.85f));

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 30;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = Color.white;

        bigLabelStyle = new GUIStyle(GUI.skin.label);
        bigLabelStyle.fontSize = 24;
        bigLabelStyle.normal.textColor = Color.white;

        taxiStyle = new GUIStyle(GUI.skin.label);
        taxiStyle.fontSize = 24;
    }

    Texture2D MakeTex(int w, int h, Color col)
    {
        Color[] pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;

        Texture2D t = new Texture2D(w, h);
        t.SetPixels(pix);
        t.Apply();
        return t;
    }

    Color StateToColor(TaxiAgent.TaxiState s)
    {
        switch (s)
        {
            case TaxiAgent.TaxiState.Disponible: return Color.green;
            case TaxiAgent.TaxiState.Asignado: return Color.yellow;
            case TaxiAgent.TaxiState.YendoAPickup: return new Color(1f, 0.6f, 0f);
            case TaxiAgent.TaxiState.EnViaje: return Color.cyan;
            case TaxiAgent.TaxiState.PasajeroABordo: return new Color(0f, 0.8f, 1f);
            case TaxiAgent.TaxiState.Esperando: return Color.red;
            default: return Color.white;
        }
    }

    public void ReportTripCompleted() => tripsCompleted++;
    public void ReportTripCancelled() => tripsCancelled++;
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class SimController : MonoBehaviour
{
    [Header("Tráfico")]
    [Tooltip("Prefabs de tráfico: sedan-sports, cybertruck, delivery-flat, police...")]
    public GameObject[] trafficPrefabs;
    public int          trafficCountPerType = 3;
    public Vector3      trafficScale        = new Vector3(0.2f, 0.2f, 0.2f);

    [Header("Taxis")]
    [Tooltip("Prefab del taxi")]
    public GameObject taxiPrefab;
    public int        taxiCount = 3;
    public Vector3    taxiScale = new Vector3(0.2f, 0.2f, 0.2f);
    [Tooltip("Velocidad uniforme para todos los taxis")]
    public float      taxiSpeed = 4f;

    [Header("Pasajeros")]
    public bool  spawnPassengers = false;
    public float spawnInterval   = 15f;
    public int   maxPassengers   = 6;

    [Header("Zona de spawn")]
    public Vector3 spawnAreaMin = new Vector3(-50f, 0f, -50f);
    public Vector3 spawnAreaMax = new Vector3( 50f, 0f,  50f);
    public float   navMeshSearchRadius = 20f;

    private int tripsCompleted = 0;
    private int tripsCancelled = 0;
    private int passengerCount = 0;

    private FleetDispatcher dispatcher;
    private TaxiAgent[]     taxis;

    private GUIStyle boxStyle;
    private GUIStyle titleStyle;
    private GUIStyle bigLabelStyle;
    private GUIStyle taxiStyle;

    void Start()
    {
        dispatcher = FindFirstObjectByType<FleetDispatcher>();

        SpawnTraffic();
        SpawnTaxis();

        taxis = FindObjectsByType<TaxiAgent>(FindObjectsSortMode.None);

        if (spawnPassengers)
            StartCoroutine(SpawnPassengersRoutine());
    }

    void SpawnTraffic()
    {
        if (trafficPrefabs == null || trafficPrefabs.Length == 0) return;

        foreach (GameObject prefab in trafficPrefabs)
        {
            if (prefab == null) continue;

            for (int i = 0; i < trafficCountPerType; i++)
            {
                Vector3 pos;
                if (!TryGetNavMeshPoint(out pos)) continue;

                GameObject v = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                v.name = $"{prefab.name}_{i + 1}";
                v.transform.localScale = trafficScale;

                if (v.GetComponent<NavMeshAgent>() == null)
                    v.AddComponent<NavMeshAgent>();

                TrafficVehicle tv = v.GetComponent<TrafficVehicle>();
                if (tv == null) tv = v.AddComponent<TrafficVehicle>();
                tv.patrolAreaMin = spawnAreaMin;
                tv.patrolAreaMax = spawnAreaMax;
            }
        }
    }

    void SpawnTaxis()
    {
        if (taxiPrefab == null) return;

        for (int i = 0; i < taxiCount; i++)
        {
            Vector3 pos;
            if (!TryGetNavMeshPoint(out pos)) continue;

            GameObject taxi = Instantiate(taxiPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            taxi.name = $"Taxi_{i + 1:00}";
            taxi.transform.localScale = taxiScale;

            if (taxi.GetComponent<NavMeshAgent>() == null)
                taxi.AddComponent<NavMeshAgent>();

            TaxiAgent agent = taxi.GetComponent<TaxiAgent>();
            if (agent == null) agent = taxi.AddComponent<TaxiAgent>();
            agent.taxiId    = taxi.name;
            agent.moveSpeed = taxiSpeed; // todos con la misma velocidad
        }
    }

    IEnumerator SpawnPassengersRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(spawnInterval);
            if (passengerCount >= maxPassengers) continue;

            Vector3 pickup  = RandomStreetPoint();
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
        for (int i = 0; i < 15; i++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(spawnAreaMin.x, spawnAreaMax.x),
                0f,
                Random.Range(spawnAreaMin.z, spawnAreaMax.z));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, navMeshSearchRadius, NavMesh.AllAreas))
                return hit.position + Vector3.up * 0.3f;
        }

        Debug.LogWarning("[SimController] No se encontró punto NavMesh. ¿Está el NavMesh bakeado?");
        return (spawnAreaMin + spawnAreaMax) * 0.5f + Vector3.up * 0.3f;
    }

    void SpawnPassenger(string id, Vector3 pickup, Vector3 dropoff)
    {
        GameObject p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        p.name = id;
        p.transform.position   = pickup;
        p.transform.localScale = Vector3.one * 0.6f;

        Renderer r = p.GetComponent<Renderer>();
        if (r)
        {
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material m = new Material(urpShader);
            Color c = new Color(Random.value, Random.value, Random.value);
            m.SetColor("_BaseColor", c);
            m.color = c;
            r.material = m;
        }

        GameObject pkGO = new GameObject("Pickup");
        pkGO.transform.position = pickup;
        pkGO.transform.SetParent(p.transform);

        GameObject drGO = new GameObject("Dropoff");
        drGO.transform.position = dropoff;
        drGO.transform.SetParent(p.transform);

        PassengerAgent agent = p.AddComponent<PassengerAgent>();
        agent.passengerId  = id;
        agent.pickupPoint  = pkGO.transform;
        agent.dropoffPoint = drGO.transform;
        agent.maxWaitTime  = 30f;
    }

    bool TryGetNavMeshPoint(out Vector3 result)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(spawnAreaMin.x, spawnAreaMax.x),
                0f,
                Random.Range(spawnAreaMin.z, spawnAreaMax.z));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, navMeshSearchRadius, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }

        result = Vector3.zero;
        Debug.LogWarning("[SimController] No se encontró punto NavMesh válido.");
        return false;
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

        GUI.Box(new Rect(20, 20, 700, 700), "", boxStyle);
        GUI.Label(new Rect(40, 30,  400, 40), "ROBOTAXI SIMULATOR",                      titleStyle);
        GUI.Label(new Rect(40, 80,  380, 30), $"Velocidad: x{Time.timeScale:F0}",        bigLabelStyle);
        GUI.Label(new Rect(40, 110, 380, 30), $"Viajes completados: {tripsCompleted}",   bigLabelStyle);
        GUI.Label(new Rect(40, 140, 380, 30), $"Viajes cancelados: {tripsCancelled}",    bigLabelStyle);
        GUI.Label(new Rect(40, 170, 380, 30), $"Pasajeros activos: {passengerCount}",    bigLabelStyle);
        GUI.Label(new Rect(40, 210, 380, 30), "ESTADO DE TAXIS:",                        titleStyle);

        float y = 245;
        if (taxis != null)
        {
            foreach (var taxi in taxis)
            {
                if (taxi == null) continue;
                taxiStyle.normal.textColor = StateToColor(taxi.state);
                GUI.Label(new Rect(50, y, 350, 28), $"• {taxi.taxiId} → {taxi.state}", taxiStyle);
                y += 28;
            }
        }

        GUI.Label(new Rect(20, Screen.height - 40, 700, 30),
            "SPACE=Pausa | 1/2/3=Velocidad | Cam: 1-9=Taxi / 0=Libre", bigLabelStyle);
    }

    void InitStyles()
    {
        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = MakeTex(2, 2, new Color(0, 0, 0, 0.85f));

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize  = 26;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = Color.white;

        bigLabelStyle = new GUIStyle(GUI.skin.label);
        bigLabelStyle.fontSize = 20;
        bigLabelStyle.normal.textColor = Color.white;

        taxiStyle = new GUIStyle(GUI.skin.label);
        taxiStyle.fontSize = 20;
    }

    Texture2D MakeTex(int w, int h, Color col)
    {
        Color[]   pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = col;
        Texture2D t   = new Texture2D(w, h);
        t.SetPixels(pix);
        t.Apply();
        return t;
    }

    Color StateToColor(TaxiAgent.TaxiState s)
    {
        switch (s)
        {
            case TaxiAgent.TaxiState.Disponible:     return Color.green;
            case TaxiAgent.TaxiState.Asignado:       return Color.yellow;
            case TaxiAgent.TaxiState.YendoAPickup:   return new Color(1f, 0.6f, 0f);
            case TaxiAgent.TaxiState.EnViaje:        return Color.cyan;
            case TaxiAgent.TaxiState.PasajeroABordo: return new Color(0f, 0.8f, 1f);
            case TaxiAgent.TaxiState.Esperando:      return Color.red;
            default:                                  return Color.white;
        }
    }

    public void ReportTripCompleted() => tripsCompleted++;
    public void ReportTripCancelled() => tripsCancelled++;
}

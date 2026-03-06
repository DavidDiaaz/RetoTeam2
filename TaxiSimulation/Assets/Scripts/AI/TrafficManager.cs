// ============================================================================
// TrafficManager.cs — Gestor Centralizado de Estado del Tráfico
// ============================================================================
//
// DESCRIPCIÓN:
//   Singleton que provee información global sobre el tráfico a todos
//   los agentes del sistema. Centraliza la detección de congestión
//   y puede ser consultado por cualquier agente BDI durante su fase
//   de percepción.
//
// FUNCIONALIDADES:
//   • Detecta zonas congestionadas contando vehículos en un radio
//   • Provee nivel de congestión para cualquier punto del mapa
//   • Extensible para rutas alternativas, semáforos, etc.
//
// USO EN UNITY:
//   1. Agregar este script a un GameObject vacío en la escena
//      (o dejarlo y SimController lo creará automáticamente).
//   2. Los agentes acceden vía: TrafficManager.Instance.GetCongestionLevel(pos)
//
// EXTENSIBILIDAD:
//   • Agregar semáforos inteligentes: registrar semáforos y su estado
//   • Rutas alternas: implementar GetAlternativeRoute() con pathfinding
//   • Coordinación vehicular: agregar canal de comunicación entre agentes
//
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Singleton que centraliza la información de tráfico para todos los agentes.
/// Permite consultar niveles de congestión y provee datos para la toma
/// de decisiones BDI.
/// </summary>
public class TrafficManager : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // PATRÓN SINGLETON
    // ─────────────────────────────────────────────

    /// <summary>Instancia única del TrafficManager.</summary>
    public static TrafficManager Instance { get; private set; }

    // ─────────────────────────────────────────────
    // CONFIGURACIÓN
    // ─────────────────────────────────────────────

    [Header("Detección de Congestión")]
    [Tooltip("Radio en metros para detectar vehículos al evaluar congestión.")]
    public float congestionDetectionRadius = 15f;

    [Tooltip("Número de vehículos en el radio para considerar congestión MEDIA.")]
    public int mediumCongestionThreshold = 3;

    [Tooltip("Número de vehículos en el radio para considerar congestión ALTA.")]
    public int highCongestionThreshold = 6;

    [Tooltip("Cada cuántos segundos se recalculan las zonas de congestión.")]
    public float congestionUpdateInterval = 2f;

    [Header("Mapa de Congestión")]
    [Tooltip("Tamaño de la celda del grid de congestión (metros).")]
    public float gridCellSize = 20f;

    // ─────────────────────────────────────────────
    // DATOS DEL TRÁFICO
    // ─────────────────────────────────────────────

    /// <summary>
    /// Niveles de congestión posibles.
    /// </summary>
    public enum CongestionLevel
    {
        Low,     // Tráfico fluido
        Medium,  // Congestión moderada
        High     // Congestión severa
    }

    /// <summary>Mapa de congestión: posición de celda → nivel.</summary>
    private Dictionary<Vector2Int, CongestionLevel> congestionMap
        = new Dictionary<Vector2Int, CongestionLevel>();

    /// <summary>Todos los NavMeshAgents en la escena (vehículos).</summary>
    private NavMeshAgent[] allVehicles;

    /// <summary>Temporizador para actualización periódica.</summary>
    private float updateTimer = 0f;

    // ─────────────────────────────────────────────
    // MÉTRICAS GLOBALES
    // ─────────────────────────────────────────────

    [Header("Métricas (solo lectura)")]
    [SerializeField] private int totalVehicles = 0;
    [SerializeField] private int congestedZones = 0;

    /// <summary>Total de vehículos rastreados en la simulación.</summary>
    public int TotalVehicles => totalVehicles;

    /// <summary>Número de zonas con congestión media o alta.</summary>
    public int CongestedZones => congestedZones;

    // ─────────────────────────────────────────────
    // CICLO DE VIDA
    // ─────────────────────────────────────────────

    void Awake()
    {
        // Implementar Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        RefreshVehicleList();
    }

    void Update()
    {
        updateTimer += Time.deltaTime;
        if (updateTimer >= congestionUpdateInterval)
        {
            updateTimer = 0f;
            UpdateCongestionMap();
        }
    }

    // ─────────────────────────────────────────────
    // API PÚBLICA — Consultas de los agentes BDI
    // ─────────────────────────────────────────────

    /// <summary>
    /// Obtiene el nivel de congestión en una posición del mapa.
    /// Los agentes BDI llaman este método durante PerceiveEnvironment().
    /// </summary>
    /// <param name="position">Posición mundial a consultar.</param>
    /// <returns>Nivel de congestión (Low, Medium, High).</returns>
    public CongestionLevel GetCongestionLevel(Vector3 position)
    {
        Vector2Int cell = WorldToGrid(position);
        if (congestionMap.TryGetValue(cell, out CongestionLevel level))
            return level;
        return CongestionLevel.Low;
    }

    /// <summary>
    /// Obtiene el nivel de congestión como valor numérico normalizado [0, 1].
    /// Útil para cálculos de prioridad y ponderación en la deliberación BDI.
    /// </summary>
    /// <param name="position">Posición mundial a consultar.</param>
    /// <returns>0 = sin congestión, 0.5 = media, 1.0 = alta.</returns>
    public float GetCongestionValue(Vector3 position)
    {
        CongestionLevel level = GetCongestionLevel(position);
        switch (level)
        {
            case CongestionLevel.High:   return 1.0f;
            case CongestionLevel.Medium: return 0.5f;
            default:                     return 0.0f;
        }
    }

    /// <summary>
    /// Cuenta los vehículos dentro de un radio alrededor de una posición.
    /// Usado internamente y disponible para agentes que necesiten información
    /// más granular que el nivel de congestión del grid.
    /// </summary>
    public int CountVehiclesInRadius(Vector3 position, float radius)
    {
        if (allVehicles == null) return 0;

        int count = 0;
        float sqrRadius = radius * radius;
        foreach (var vehicle in allVehicles)
        {
            if (vehicle == null) continue;
            if ((vehicle.transform.position - position).sqrMagnitude <= sqrRadius)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Sugiere una posición alternativa para evitar una zona congestionada.
    /// Esta es una implementación base que desplaza ligeramente la posición.
    /// Puede ser extendida con pathfinding avanzado.
    /// </summary>
    /// <param name="from">Posición de origen.</param>
    /// <param name="to">Posición de destino original.</param>
    /// <returns>Posición de destino ajustada (o la misma si no hay congestión).</returns>
    public Vector3 GetAlternativeRoute(Vector3 from, Vector3 to)
    {
        CongestionLevel level = GetCongestionLevel(to);

        if (level == CongestionLevel.High)
        {
            // Intentar desviar la ruta 10m perpendicular a la dirección de viaje
            Vector3 direction = (to - from).normalized;
            Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;
            Vector3 alternative = to + perpendicular * 10f;

            // Verificar que la posición alternativa sea válida en el NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(alternative, out hit, 15f, NavMesh.AllAreas))
                return hit.position;
        }

        return to; // Sin desviación necesaria o no se encontró alternativa
    }

    /// <summary>
    /// Fuerza una actualización de la lista de vehículos.
    /// Llamar cuando se agregan o eliminan vehículos de la escena.
    /// </summary>
    public void RefreshVehicleList()
    {
        allVehicles   = FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None);
        totalVehicles = allVehicles.Length;
    }

    // ─────────────────────────────────────────────
    // LÓGICA INTERNA
    // ─────────────────────────────────────────────

    /// <summary>
    /// Recalcula el mapa de congestión basándose en la posición
    /// actual de todos los vehículos.
    /// </summary>
    private void UpdateCongestionMap()
    {
        // Refrescar lista de vehículos periódicamente
        allVehicles   = FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None);
        totalVehicles = allVehicles.Length;

        // Limpiar mapa anterior
        congestionMap.Clear();
        congestedZones = 0;

        // Contar vehículos por celda del grid
        Dictionary<Vector2Int, int> vehicleCounts = new Dictionary<Vector2Int, int>();

        foreach (var vehicle in allVehicles)
        {
            if (vehicle == null) continue;
            Vector2Int cell = WorldToGrid(vehicle.transform.position);

            if (!vehicleCounts.ContainsKey(cell))
                vehicleCounts[cell] = 0;
            vehicleCounts[cell]++;
        }

        // Asignar nivel de congestión a cada celda
        foreach (var kvp in vehicleCounts)
        {
            CongestionLevel level;
            if (kvp.Value >= highCongestionThreshold)
                level = CongestionLevel.High;
            else if (kvp.Value >= mediumCongestionThreshold)
                level = CongestionLevel.Medium;
            else
                level = CongestionLevel.Low;

            congestionMap[kvp.Key] = level;

            if (level != CongestionLevel.Low)
                congestedZones++;
        }
    }

    /// <summary>
    /// Convierte una posición mundial a coordenadas de celda del grid.
    /// </summary>
    private Vector2Int WorldToGrid(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / gridCellSize),
            Mathf.FloorToInt(worldPos.z / gridCellSize)
        );
    }

    // ─────────────────────────────────────────────
    // GIZMOS — Visualización en el editor
    // ─────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        // Dibujar zonas congestionadas en el editor
        foreach (var kvp in congestionMap)
        {
            if (kvp.Value == CongestionLevel.Low) continue;

            Vector3 center = new Vector3(
                (kvp.Key.x + 0.5f) * gridCellSize,
                1f,
                (kvp.Key.y + 0.5f) * gridCellSize
            );

            Gizmos.color = kvp.Value == CongestionLevel.High
                ? new Color(1f, 0f, 0f, 0.3f)
                : new Color(1f, 1f, 0f, 0.2f);

            Gizmos.DrawCube(center, new Vector3(gridCellSize, 0.2f, gridCellSize));
        }
    }
}

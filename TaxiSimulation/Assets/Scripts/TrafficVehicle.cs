// ============================================================================
// TrafficVehicle.cs — Vehículo de Tráfico NPC con Flujo Realista
// ============================================================================
//
// DESCRIPCIÓN:
//   Controla vehículos NPC (policía, sedanes, camionetas, etc.) que circulan
//   por la ciudad siguiendo waypoints. Implementa un sistema de tráfico
//   realista con:
//     • Frenado gradual proporcional a la distancia del obstáculo
//     • Anti-deadlock: cuando dos vehículos se bloquean mutuamente,
//       uno redirige a un waypoint alternativo
//     • Detección frontal focalizada (no lateral excesiva)
//     • NavMeshAgent con radio apropiado para la escala del vehículo
//
// USO EN UNITY:
//   Se agrega automáticamente a prefabs de tráfico por SimController.
//   Requiere NavMeshAgent y un Collider en el prefab.
//
// ============================================================================

using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class TrafficVehicle : MonoBehaviour
{
    [Header("Movimiento")]
    public float moveSpeed     = 2f;
    public float rotationSpeed = 5f;

    [Header("Detección de vehículos adelante")]
    [Tooltip("Distancia de detección de frenado (metros reales del mundo)")]
    public float brakeDistance = 4f;
    [Tooltip("Ancho del rayo de detección frontal")]
    public float brakeWidth   = 0.4f;
    [Tooltip("Segundos frenado antes de intentar esquivar/redirigir")]
    public float stuckTimeout = 4f;

    [Header("Waypoints")]
    [Tooltip("Waypoint inicial. Si se deja vacío busca el más cercano automáticamente.")]
    public RoadWaypoint startWaypoint;

    [Header("Fallback si no hay waypoints")]
    public Vector3 patrolAreaMin = new Vector3(-50f, 0f, -50f);
    public Vector3 patrolAreaMax = new Vector3( 50f, 0f,  50f);
    public float   searchRadius  = 20f;

    // ─── Componentes ───
    private NavMeshAgent nav;
    private RoadWaypoint currentTarget;
    private bool         usingWaypoints = false;

    // ─── Frenado y anti-deadlock ───
    private float brakeTimer   = 0f;
    private float stuckTimer   = 0f;
    private bool  isBraking    = false;
    private Vector3 lastPosition;
    private float movementCheckTimer = 0f;

    void Start()
    {
        nav              = GetComponent<NavMeshAgent>();
        nav.speed        = moveSpeed;
        nav.angularSpeed = 0f;
        nav.acceleration = 8f;
        nav.autoBraking  = false;

        // Radio proporcional a la escala del vehículo
        // Los vehículos están a escala ~0.2, radio real ≈ 0.6-0.8m del mundo
        float scaleFactor = Mathf.Max(transform.localScale.x, transform.localScale.z);
        nav.radius = Mathf.Clamp(scaleFactor * 3f, 0.3f, 1.0f);

        nav.avoidancePriority     = Random.Range(30, 70);
        nav.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

        lastPosition = transform.position;

        RoadWaypoint[] all = FindObjectsByType<RoadWaypoint>(FindObjectsSortMode.None);

        if (all.Length > 0)
        {
            usingWaypoints = true;
            currentTarget  = startWaypoint != null ? startWaypoint : FindClosestWaypoint(all);
            nav.SetDestination(currentTarget.transform.position);
        }
        else
        {
            usingWaypoints = false;
            SetRandomDestination();
        }
    }

    void Update()
    {
        HandleTrafficFlow();
        SmoothRotation();

        // Avanzar waypoints cuando llegamos al actual
        if (!isBraking && !nav.pathPending && nav.remainingDistance <= nav.stoppingDistance + 0.3f)
        {
            if (usingWaypoints) AdvanceWaypoint();
            else                SetRandomDestination();
        }

        // Anti-deadlock: detectar si el vehículo no se ha movido
        DetectStuck();
    }

    // ═════════════════════════════════════════════
    //  FLUJO DE TRÁFICO REALISTA
    // ═════════════════════════════════════════════

    /// <summary>
    /// Sistema de frenado que simula flujo de tráfico real:
    /// - Reduce velocidad gradualmente al acercarse a otro vehículo
    /// - Mantiene distancia de seguimiento como un conductor real
    /// - Si está bloqueado mucho tiempo, intenta redirigir
    /// </summary>
    void HandleTrafficFlow()
    {
        float closestDist = DetectVehicleAheadDistance();

        if (closestDist < brakeDistance)
        {
            // ── Frenado gradual proporcional a la distancia ──
            // Simula cómo un conductor real reduce la velocidad
            float t = closestDist / brakeDistance; // 0 = encima, 1 = lejos
            float targetSpeed = moveSpeed * t * t; // Curva cuadrática: frena más rápido al acercarse

            // Velocidad mínima de arrastre para no detenerse al 100%
            // (permite que NavMesh siga calculando evasiones)
            targetSpeed = Mathf.Max(targetSpeed, moveSpeed * 0.05f);

            // Si está MUY cerca (< 25% del brakeDistance), detenerse
            if (closestDist < brakeDistance * 0.25f)
                targetSpeed = 0f;

            nav.speed = Mathf.Lerp(nav.speed, targetSpeed, Time.deltaTime * 5f);
            isBraking = true;
            brakeTimer += Time.deltaTime;
        }
        else
        {
            // Sin obstáculo → acelerar gradualmente a velocidad normal
            nav.speed = Mathf.Lerp(nav.speed, moveSpeed, Time.deltaTime * 3f);
            isBraking = false;
            brakeTimer = 0f;
        }
    }

    /// <summary>
    /// Detecta si el vehículo está atascado (no se mueve) y lo desatasca.
    /// </summary>
    void DetectStuck()
    {
        movementCheckTimer += Time.deltaTime;

        if (movementCheckTimer >= 1f)
        {
            float distMoved = Vector3.Distance(transform.position, lastPosition);
            lastPosition = transform.position;
            movementCheckTimer = 0f;

            if (distMoved < 0.1f) // Prácticamente no se ha movido en 1 segundo
            {
                stuckTimer += 1f;

                if (stuckTimer >= stuckTimeout)
                {
                    Unstick();
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
    /// Desatasca el vehículo cuando está bloqueado.
    /// Estrategia: redirigir a un waypoint alternativo o punto aleatorio.
    /// </summary>
    void Unstick()
    {
        nav.speed = moveSpeed;

        if (usingWaypoints && currentTarget != null)
        {
            // Intentar tomar una ruta alternativa en la intersección
            var alternatives = currentTarget.nextWaypoints;
            if (alternatives != null && alternatives.Count > 1)
            {
                // Elegir un waypoint diferente al actual destino
                RoadWaypoint alt = alternatives[Random.Range(0, alternatives.Count)];
                currentTarget = alt;
                nav.SetDestination(alt.transform.position);
            }
            else
            {
                // Sin alternativas en esta intersección → saltar al siguiente
                RoadWaypoint next = currentTarget.GetNextRandom();
                if (next != null)
                {
                    currentTarget = next;
                    nav.SetDestination(next.transform.position);
                }
                else
                {
                    // Último recurso: ir a un waypoint aleatorio cercano
                    RoadWaypoint[] all = FindObjectsByType<RoadWaypoint>(FindObjectsSortMode.None);
                    if (all.Length > 0)
                    {
                        currentTarget = all[Random.Range(0, all.Length)];
                        nav.SetDestination(currentTarget.transform.position);
                    }
                }
            }
        }
        else
        {
            SetRandomDestination();
        }

        // Empujón: mover ligeramente hacia adelante para desbloquear
        NavMeshHit hit;
        Vector3 nudge = transform.position + transform.forward * 1f;
        if (NavMesh.SamplePosition(nudge, out hit, 3f, NavMesh.AllAreas))
            nav.Warp(hit.position);
    }

    // ═════════════════════════════════════════════
    //  DETECCIÓN
    // ═════════════════════════════════════════════

    /// <summary>
    /// Detecta la distancia al vehículo más cercano ADELANTE únicamente.
    /// Solo detecta vehículos que están en el camino directo frontal,
    /// no vehículos que circulan por carriles paralelos.
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

            // Solo reaccionar a otros vehículos (NavMeshAgents)
            NavMeshAgent otherNav = hit.collider.GetComponent<NavMeshAgent>();
            if (otherNav == null) continue;

            // Verificar que el otro vehículo REALMENTE está adelante
            // (no a un lado en un carril paralelo)
            Vector3 toOther = (hit.collider.transform.position - transform.position).normalized;
            float dot = Vector3.Dot(transform.forward, toOther);

            // dot > 0.5 significa que está en un arco de ~60° frente al vehículo
            if (dot > 0.5f)
                closest = Mathf.Min(closest, hit.distance);
        }

        return closest;
    }

    // ═════════════════════════════════════════════
    //  NAVEGACIÓN
    // ═════════════════════════════════════════════

    void SmoothRotation()
    {
        Vector3 vel = nav.velocity;
        if (vel.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation,
                                              Quaternion.LookRotation(vel.normalized),
                                              rotationSpeed * Time.deltaTime);
    }

    void AdvanceWaypoint()
    {
        if (currentTarget == null) return;
        RoadWaypoint next = currentTarget.GetNextRandom();
        if (next == null) { nav.SetDestination(currentTarget.transform.position); return; }
        currentTarget = next;
        nav.SetDestination(currentTarget.transform.position);
    }

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

    void SetRandomDestination()
    {
        Vector3 candidate = new Vector3(
            Random.Range(patrolAreaMin.x, patrolAreaMax.x),
            0f,
            Random.Range(patrolAreaMin.z, patrolAreaMax.z));

        NavMeshHit hit;
        if (NavMesh.SamplePosition(candidate, out hit, searchRadius, NavMesh.AllAreas))
            nav.SetDestination(hit.position);
        else
            Invoke(nameof(SetRandomDestination), 0.5f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.5f,
                        transform.position + Vector3.up * 0.5f + transform.forward * brakeDistance);
    }
}

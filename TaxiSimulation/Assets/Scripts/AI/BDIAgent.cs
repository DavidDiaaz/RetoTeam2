// ============================================================================
// BDIAgent.cs — Clase Base Abstracta del Modelo BDI
// ============================================================================
//
// DESCRIPCIÓN:
//   Implementa el ciclo de deliberación BDI (Belief–Desire–Intention) como
//   clase base abstracta. Todos los agentes inteligentes del sistema
//   (Dispatcher, Taxi) heredan de esta clase.
//
// CICLO BDI:
//   Cada frame (o cada N segundos según bdiTickInterval) se ejecuta:
//
//   ┌─────────────────────────────────────────────┐
//   │  1. PerceiveEnvironment()  — Sensores       │
//   │  2. UpdateBeliefs()        — Modelo mental   │
//   │  3. GenerateDesires()      — Objetivos       │
//   │  4. SelectIntentions()     — Deliberación    │
//   │  5. ExecuteIntentions()    — Acción          │
//   └─────────────────────────────────────────────┘
//
// USO EN UNITY:
//   Esta clase NO se agrega directamente a un GameObject.
//   Se usa como base: public class TaxiAgent : BDIAgent { ... }
//
// EXTENSIBILIDAD:
//   Para agregar nuevos agentes (semáforos, peatones, etc.) basta con:
//   1. Crear una clase que herede de BDIAgent.
//   2. Implementar los 5 métodos abstractos.
//
// ============================================================================

using UnityEngine;

/// <summary>
/// Clase base abstracta que implementa el ciclo de deliberación BDI.
/// Todos los agentes inteligentes del sistema multiagente heredan de esta clase.
/// </summary>
public abstract class BDIAgent : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // CONFIGURACIÓN
    // ─────────────────────────────────────────────

    [Header("BDI — Configuración del Ciclo")]
    [Tooltip("Intervalo en segundos entre cada ciclo BDI completo. " +
             "Valores bajos = más reactivo pero más costoso. " +
             "Valor recomendado: 0.2 – 0.5 segundos.")]
    [Range(0.05f, 2f)]
    public float bdiTickInterval = 0.3f;

    [Tooltip("Nombre del agente para logs de depuración.")]
    public string agentName = "BDIAgent";

    [Tooltip("Activar logs detallados del ciclo BDI en la consola.")]
    public bool debugBDI = false;

    // ─────────────────────────────────────────────
    // MÉTRICAS (solo lectura en Inspector)
    // ─────────────────────────────────────────────

    [Header("BDI — Métricas")]
    [Tooltip("Número total de ciclos BDI ejecutados.")]
    [SerializeField] private int bdiCycleCount = 0;

    /// <summary>Número total de ciclos BDI ejecutados desde el inicio.</summary>
    public int BDICycleCount => bdiCycleCount;

    // ─────────────────────────────────────────────
    // ESTADO INTERNO
    // ─────────────────────────────────────────────

    /// <summary>Temporizador interno para controlar la frecuencia del ciclo BDI.</summary>
    private float bdiTimer = 0f;

    // ─────────────────────────────────────────────
    // CICLO DE VIDA DE UNITY
    // ─────────────────────────────────────────────

    /// <summary>
    /// Update de Unity. Controla la frecuencia del ciclo BDI y lo ejecuta.
    /// Las subclases NO deben sobreescribir Update(); en su lugar, deben
    /// implementar los métodos abstractos y usar OnBDIStart() / OnBDIUpdate().
    /// </summary>
    protected virtual void Update()
    {
        // Acumular tiempo
        bdiTimer += Time.deltaTime;

        // Ejecutar ciclo BDI solo si ha pasado suficiente tiempo
        if (bdiTimer >= bdiTickInterval)
        {
            bdiTimer = 0f;
            RunBDICycle();
        }

        // Permitir a las subclases ejecutar lógica cada frame
        // (por ejemplo, movimiento continuo que no depende del ciclo BDI)
        OnBDIUpdate();
    }

    // ─────────────────────────────────────────────
    // CICLO BDI PRINCIPAL
    // ─────────────────────────────────────────────

    /// <summary>
    /// Ejecuta un ciclo completo de deliberación BDI.
    /// Este es el corazón del sistema de inteligencia artificial.
    /// </summary>
    private void RunBDICycle()
    {
        bdiCycleCount++;

        if (debugBDI)
            Debug.Log($"[BDI] {agentName} — Ciclo #{bdiCycleCount}");

        // ── Paso 1: Percepción del entorno ──────────────────
        // El agente "observa" el mundo: posiciones, estados, eventos.
        PerceiveEnvironment();

        // ── Paso 2: Actualización de creencias ──────────────
        // Con la información percibida, el agente actualiza su
        // modelo mental del mundo (beliefs).
        UpdateBeliefs();

        // ── Paso 3: Generación de deseos ────────────────────
        // El agente determina qué objetivos quiere alcanzar
        // basándose en sus creencias actuales.
        GenerateDesires();

        // ── Paso 4: Selección de intenciones (deliberación) ─
        // De todos los deseos, el agente elige cuál perseguir
        // ahora mismo, convirtiéndolo en una intención concreta.
        SelectIntentions();

        // ── Paso 5: Ejecución de la intención ───────────────
        // El agente ejecuta la acción correspondiente a su
        // intención seleccionada.
        ExecuteIntentions();
    }

    // ─────────────────────────────────────────────
    // MÉTODOS ABSTRACTOS — Cada agente concreto los implementa
    // ─────────────────────────────────────────────

    /// <summary>
    /// Paso 1: Percepción del entorno.
    /// El agente recopila información del mundo (posiciones de otros agentes,
    /// estado del tráfico, solicitudes pendientes, etc.).
    /// </summary>
    protected abstract void PerceiveEnvironment();

    /// <summary>
    /// Paso 2: Actualización de creencias (Beliefs).
    /// A partir de lo percibido, el agente actualiza su modelo interno
    /// del mundo. Las creencias son la representación interna de la realidad.
    /// </summary>
    protected abstract void UpdateBeliefs();

    /// <summary>
    /// Paso 3: Generación de deseos (Desires).
    /// El agente determina qué objetivos desea alcanzar, en función
    /// de sus creencias actuales. Los deseos son los estados del mundo
    /// que el agente quiere que se cumplan.
    /// </summary>
    protected abstract void GenerateDesires();

    /// <summary>
    /// Paso 4: Selección de intenciones (Intentions).
    /// La fase de deliberación: el agente evalúa sus deseos y selecciona
    /// la intención más apropiada para perseguir. La intención es el
    /// compromiso del agente con un curso de acción concreto.
    /// </summary>
    protected abstract void SelectIntentions();

    /// <summary>
    /// Paso 5: Ejecución de intenciones.
    /// El agente ejecuta las acciones necesarias para cumplir la intención
    /// seleccionada. Esto puede incluir moverse, asignar recursos,
    /// comunicarse con otros agentes, etc.
    /// </summary>
    protected abstract void ExecuteIntentions();

    // ─────────────────────────────────────────────
    // MÉTODOS VIRTUALES — Extensión opcional
    // ─────────────────────────────────────────────

    /// <summary>
    /// Se ejecuta CADA FRAME, independientemente del ciclo BDI.
    /// Úsalo para lógica continua como movimiento, animaciones, frenado, etc.
    /// No confundir con el ciclo BDI (que se ejecuta cada bdiTickInterval).
    /// </summary>
    protected virtual void OnBDIUpdate() { }
}

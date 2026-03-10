using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
public class FlashAutoPatrol : MonoBehaviour
{
    public List<Transform> points = new List<Transform>();
    public float speed = 8f;
    public float sprintSpeed = 16f;
    public float arriveDistance = 0.25f;
    public float gravity = -9.81f;
    public bool loop = true;

    int idx = 0;
    CharacterController cc;
    Vector3 vel;

    void Awake() => cc = GetComponent<CharacterController>();

    void Update()
    {
        if (points == null || points.Count == 0) return;

        Transform target = points[idx];

        // Dirección horizontal al waypoint
        Vector3 to = target.position - transform.position;
        to.y = 0f;

        // Llegó al punto
        if (to.magnitude <= arriveDistance)
        {
            idx++;
            if (idx >= points.Count)
            {
                if (loop) idx = 0;
                else { enabled = false; return; }
            }
            return;
        }

        // Movimiento
        Vector3 dir = to.normalized;
        float curSpeed = sprintSpeed; // siempre "flash", o cámbialo a speed si quieres
        cc.Move(dir * curSpeed * Time.deltaTime);

        // Rotar hacia donde va
        if (dir.sqrMagnitude > 0.001f) transform.forward = dir;

        // Gravedad
        if (cc.isGrounded && vel.y < 0) vel.y = -2f;
        vel.y += gravity * Time.deltaTime;
        cc.Move(vel * Time.deltaTime);
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;

/// Attach to your FLASH (the empty parent with CharacterController).
/// Create a TextAsset from flash_route_25x25.json and assign it in the Inspector.
[RequireComponent(typeof(CharacterController))]
public class FlashAutoRouteFromGrid : MonoBehaviour
{
    [Header("Route Data")]
    public TextAsset routeJson;

    [Header("Grid -> World Mapping")]
    public Transform origin;          // world position of grid cell (0,0)
    public float cellSize = 1f;       // world units per cell
    public bool rowIncreasesToWorldZ = true; // if your grid row goes "down", set true
    public float yOffset = 0f;        // lift Flash slightly above ground

    [Header("Movement")]
    public float speed = 12f;
    public float arriveDistance = 0.15f;
    public bool loop = true;

    [Header("Teleport Between Segments")]
    public bool teleportBetweenSegments = true;

    CharacterController cc;

    List<Vector2Int> route = new List<Vector2Int>();
    HashSet<int> teleportStarts = new HashSet<int>();
    int idx = 0;

    [Serializable] class RouteCell { public int r; public int c; }
    [Serializable] class RouteFile
    {
        public int rows;
        public int cols;
        public List<RouteCell> route;
        public List<int> teleport_indices;
    }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    void Start()
    {
        if (routeJson == null)
        {
            Debug.LogError("Route JSON is not assigned.");
            enabled = false;
            return;
        }
        if (origin == null) origin = this.transform; // fallback (not ideal)

        var file = JsonUtility.FromJson<RouteFile>(routeJson.text);
        if (file == null || file.route == null || file.route.Count == 0)
        {
            Debug.LogError("Route JSON could not be parsed or is empty.");
            enabled = false;
            return;
        }

        route.Clear();
        foreach (var cell in file.route)
            route.Add(new Vector2Int(cell.c, cell.r)); // store as (x=col, y=row)

        teleportStarts.Clear();
        if (file.teleport_indices != null)
            foreach (var t in file.teleport_indices) teleportStarts.Add(t);

        // Start at first point
        idx = 0;
        TeleportToIndex(idx);
    }

    void Update()
    {
        if (route.Count == 0) return;

        // If we're at a new segment start, teleport (optional)
        if (teleportBetweenSegments && teleportStarts.Contains(idx))
        {
            TeleportToIndex(idx);
        }

        Vector3 target = CellToWorld(route[idx]);
        Vector3 current = transform.position;
        Vector3 to = target - current;
        to.y = 0f;

        if (to.magnitude <= arriveDistance)
        {
            idx++;
            if (idx >= route.Count)
            {
                if (!loop) { enabled = false; return; }
                idx = 0;
            }
            return;
        }

        Vector3 dir = to.normalized;

        // Move with CharacterController (no rigidbody needed)
        cc.Move(dir * speed * Time.deltaTime);

        // Face direction
        if (dir.sqrMagnitude > 0.001f)
            transform.forward = dir;
    }

    void TeleportToIndex(int i)
    {
        Vector3 p = CellToWorld(route[i]);
        // Keep CC happy: disable/enable around teleport
        cc.enabled = false;
        transform.position = p;
        cc.enabled = true;
    }

    Vector3 CellToWorld(Vector2Int colRow)
    {
        // colRow.x = col, colRow.y = row
        float x = colRow.x * cellSize;
        float z = colRow.y * cellSize;
        if (!rowIncreasesToWorldZ) z = -z;

        Vector3 basePos = origin.position;
        return new Vector3(basePos.x + x, basePos.y + yOffset, basePos.z + z);
    }
}
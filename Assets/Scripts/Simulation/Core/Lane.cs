using System.Collections.Generic;

public class Lane
{
    public TrafficEdge        Edge;
    public int                LaneNumber;
    public List<VehicleAgent> Vehicles = new();

    // Vehicles mid-lane-change INTO this lane (lateral, same edge).
    public Dictionary<VehicleAgent, float> Entering = new();

    // Connector lanes that feed into this lane, registered at build time.
    // Used so main-road cars can see and yield to queued connector traffic.
    public List<Lane> IncomingConnectors = new();

    // ---------------------------------------------------------------
    // Neighbour queries
    // ---------------------------------------------------------------

    public VehicleAgent GetVehicleAhead(VehicleAgent v)
    {
        int i = v.LaneIndex;
        return i < Vehicles.Count - 1 ? Vehicles[i + 1] : null;
    }

    public VehicleAgent GetVehicleBehind(VehicleAgent v)
    {
        int i = v.LaneIndex;
        return i > 0 ? Vehicles[i - 1] : null;
    }

    public VehicleAgent GetVehicleAheadAt(float position)
    {
        for (int i = 0; i < Vehicles.Count; i++)
            if (Vehicles[i].Position >= position)
                return Vehicles[i];
        return null;
    }

    public VehicleAgent GetVehicleBehindAt(float position)
    {
        for (int i = Vehicles.Count - 1; i >= 0; i--)
            if (Vehicles[i].Position <= position)
                return Vehicles[i];
        return null;
    }

    // ---------------------------------------------------------------
    // Space checks
    // ---------------------------------------------------------------

    /// <summary>
    /// True if no committed or entering vehicle overlaps [position, position+length].
    /// Vehicles are stored by their *front* position; rear = Position - Length.
    /// </summary>
    public bool IsSegmentFree(float position, float length)
    {
        float qRear  = position;
        float qFront = position + length;

        foreach (var v in Vehicles)
        {
            float vRear  = v.Position - v.EffectiveLengthOn(this);
            float vFront = v.Position;
            if (vFront > qRear && vRear < qFront) return false;
        }

        foreach (var kvp in Entering)
        {
            var   v      = kvp.Key;
            float vFront = v.Position;
            float vRear  = v.Position - kvp.Value;
            if (vFront > qRear && vRear < qFront) return false;
        }

        return true;
    }

    /// <summary>
    /// True if any incoming connector has a vehicle that will merge near
    /// [mergePos, mergePos+length] on this lane — used by side-road cars
    /// to detect conflicts with sibling connectors targeting the same spot.
    /// </summary>
    public bool IsConnectorMergeConflict(float mergePos, float length)
    {
        foreach (var conn in IncomingConnectors)
        {
            if (conn.Vehicles.Count == 0) continue;

            // Check the exit link of this connector to find its merge position
            // We don't have nav here so we use the connector's own edge length
            // as a proxy: a car inside the connector will land near mergePos.
            // Consider it conflicting if any connector (other than self)
            // has an occupant — they're all targeting the same vicinity.
            return true;
        }
        return false;
    }

    /// <summary>
    /// True if entering at atPosition is safe for both the car behind
    /// (must have braking room) and the car ahead (minimum clearance).
    /// </summary>
    public bool IsSafeToEnter(float atPosition, float length = 4.5f, float deceleration = 4f)
    {
        var behind = GetVehicleBehindAt(atPosition);
        if (behind != null)
        {
            float gap    = atPosition - behind.Position;
            float needed = (behind.Speed * behind.Speed) / (2f * deceleration) + length;
            if (gap < needed) return false;
        }

        var ahead = GetVehicleAheadAt(atPosition + length);
        if (ahead != null)
        {
            float gap = ahead.Position - ahead.Length - atPosition;
            if (gap < 1f) return false;
        }

        return true;
    }

    // ---------------------------------------------------------------
    // Insertion / removal
    // ---------------------------------------------------------------

    public void InsertSorted(VehicleAgent v)
    {
        int i = 0;
        while (i < Vehicles.Count && Vehicles[i].Position < v.Position)
            i++;
        Vehicles.Insert(i, v);
        v.LaneIndex = i;
        for (int j = i + 1; j < Vehicles.Count; j++)
            Vehicles[j].LaneIndex = j;
    }

    public void Remove(VehicleAgent v)
    {
        int idx = v.LaneIndex;
        if (idx < 0 || idx >= Vehicles.Count) return;
        Vehicles.RemoveAt(idx);
        for (int i = idx; i < Vehicles.Count; i++)
            Vehicles[i].LaneIndex = i;
        v.LaneIndex = -1;
    }

    // ---------------------------------------------------------------
    // Lateral lane-change presence
    // ---------------------------------------------------------------

    public void BeginEntering(VehicleAgent v, float effectiveLength)
        => Entering[v] = effectiveLength;

    public void UpdateEntering(VehicleAgent v, float effectiveLength)
        => Entering[v] = effectiveLength;

    public void CompleteEntering(VehicleAgent v)
    {
        Entering.Remove(v);
        InsertSorted(v);
    }

    public void CancelEntering(VehicleAgent v)
        => Entering.Remove(v);

    // ---------------------------------------------------------------
    public bool IsFree(float position, float length) => IsSegmentFree(position, length);
    public void RemoveVehicle(VehicleAgent v) => Remove(v);
}
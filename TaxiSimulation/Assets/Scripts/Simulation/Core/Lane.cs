using System;
using System.Collections.Generic;

public class Lane
{
    public TrafficEdge        Edge;
    public int                LaneNumber; // position within edge (0=leftmost)
    public List<VehicleAgent> Vehicles = new();

    // ---------------------------------------------------------------
    // Queries
    // ---------------------------------------------------------------

    // First vehicle ahead of v (higher position)
    public VehicleAgent GetVehicleAhead(VehicleAgent v)
    {
        int i = v.LaneIndex + 1;
        return i < Vehicles.Count ? Vehicles[i] : null;
    }

    // First vehicle behind v (lower position)
    public VehicleAgent GetVehicleBehind(VehicleAgent v)
    {
        int i = v.LaneIndex - 1;
        return i >= 0 ? Vehicles[i] : null;
    }

    // Nearest vehicle ahead at a given position (for cross-lane perception)
    public VehicleAgent GetVehicleAheadAt(float position)
    {
        for (int i = 0; i < Vehicles.Count; i++)
            if (Vehicles[i].Position >= position)
                return Vehicles[i];
        return null;
    }

    // Nearest vehicle behind at a given position (for cross-lane perception)
    public VehicleAgent GetVehicleBehindAt(float position)
    {
        for (int i = Vehicles.Count - 1; i >= 0; i--)
            if (Vehicles[i].Position <= position)
                return Vehicles[i];
        return null;
    }

    // Physical segment overlap check — car occupies [pos, pos+length]
    // Two cars overlap if their segments intersect
    public bool IsSegmentFree(float position, float length)
    {
        foreach (var v in Vehicles)
        {
            // Overlap if: myStart < theirEnd && theirStart < myEnd
            if (position < v.Position + v.Length &&
                v.Position < position + length)
                return false;
        }
        return true;
    }

    // ---------------------------------------------------------------
    // Mutations — maintain sorted order by Position at all times
    // ---------------------------------------------------------------

    public void InsertSorted(VehicleAgent v)
    {
        int insertAt = 0;
        while (insertAt < Vehicles.Count &&
               Vehicles[insertAt].Position <= v.Position)
            insertAt++;

        Vehicles.Insert(insertAt, v);
        RebuildIndices(insertAt);
        v.LaneIndex = insertAt;
    }

    public void Remove(VehicleAgent v)
    {
        int index = v.LaneIndex;
        Vehicles.RemoveAt(index);
        RebuildIndices(index);
    }

    // ---------------------------------------------------------------
    // Internal
    // ---------------------------------------------------------------
    void RebuildIndices(int from)
    {
        for (int i = from; i < Vehicles.Count; i++)
            Vehicles[i].LaneIndex = i;
    }
}
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
        int i = LowerBound(position);
        return i < Vehicles.Count ? Vehicles[i] : null;
    }

    // Nearest vehicle behind at a given position (for cross-lane perception)
    public VehicleAgent GetVehicleBehindAt(float position)
    {
        int i = UpperBound(position) - 1;
        return i >= 0 ? Vehicles[i] : null;
    }

    // Physical segment overlap check — car occupies [pos, pos+length]
    // Two cars overlap if their segments intersect
    public bool IsSegmentFree(float position, float length)
    {
        // All vehicles at index >= LowerBound(position + length) have Position >= position + length
        // so they cannot overlap [position, position + length]. Scan backward from there.
        int end = LowerBound(position + length);
        for (int i = end - 1; i >= 0; i--)
        {
            var v = Vehicles[i];
            if (v.Position + v.Length <= position) break; // sorted — earlier vehicles also can't overlap
            return false;
        }
        return true;
    }

    // ---------------------------------------------------------------
    // Binary search helpers (Vehicles sorted ascending by Position)
    // ---------------------------------------------------------------

    // First index where Vehicles[i].Position >= position
    int LowerBound(float position)
    {
        int lo = 0, hi = Vehicles.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (Vehicles[mid].Position < position) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    // First index where Vehicles[i].Position > position
    int UpperBound(float position)
    {
        int lo = 0, hi = Vehicles.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (Vehicles[mid].Position <= position) lo = mid + 1;
            else hi = mid;
        }
        return lo;
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
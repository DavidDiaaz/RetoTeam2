using System;
using System.Collections.Generic;

/// <summary>
/// Ambient background traffic driver.
///
/// Car-following uses a simplified IDM (Intelligent Driver Model):
///   desired acceleration = a * [1 - (v/v0)^4 - (s*/s)^2]
/// where s* = s0 + v*T + v*Δv/(2√(a*b))
///
/// This naturally produces:
///  • free-flow at speed limit when road is clear
///  • smooth braking as gap closes
///  • cars enter gaps and then grow their following distance,
///    NOT refuse to enter because the gap isn't already at cruise size
///
/// Zipper merge works by making main-road cars see connector occupants
/// via IncomingConnectors and slow proportionally — creating the gap
/// rather than waiting for one to appear by chance.
/// </summary>
public class AmbientDriver : VehicleAgent
{
    Profile profile;
    float   desiredSpeed;
    float   acceleration       = 3f;   // m/s² — comfortable cruise accel
    float   laneChangeCooldown = 0f;
    const float LANE_CHANGE_COOLDOWN = 2f;

    public AmbientDriver() { profile = Profile.RandomProfile(); }

    // ---------------------------------------------------------------
    public override void Perceive(World world) => UpdatePerception(world);

    protected override LaneLink SelectNextLink(NavigationGraph graph)
    {
        var links = graph.GetLinksFrom(CurrentLane);

        if (links.Count == 0)
        {
            foreach (var lane in CurrentLane.Edge.Lanes)
            {
                var fallback = graph.GetLinksFrom(lane);
                if (fallback.Count > 0)
                    return fallback[rng.Next(fallback.Count)];
            }
            return null;
        }

        if (OnConnector) return links[0];

        var direct = new List<LaneLink>();
        foreach (var link in links)
            if (link.SourceLane == CurrentLane)
                direct.Add(link);

        var candidates = direct.Count > 0 ? direct : links;

        // Evitar calles sin salida (Terminal nodes)
        var nonDeadEnd = candidates.FindAll(l => !IsDeadEndLink(l, graph));
        if (nonDeadEnd.Count > 0) candidates = nonDeadEnd;

        // --- SISTEMA DE RULETA (Weighted Random Selection) ---
        float totalWeight = 0f;
        float[] weights = new float[candidates.Count];

        for (int i = 0; i < candidates.Count; i++)
        {
            LaneLink link = candidates[i];
            float weight = 10f; // Peso base (todas las opciones tienen chance)

            // 1. Preferencia por seguir recto (fluidez natural)
            if (link.IsStraight)
                weight += 5f;

            // 2. Repulsión suave al tráfico
            // Si el perímetro se llena, las calles hacia el centro se vuelven más atractivas
            int trafficCount = link.DestLane.Vehicles.Count;
            weight -= trafficCount * 1.5f;

            // Nunca permitimos pesos negativos o cero absoluto
            // Siempre hay al menos 1 boleto de lotería para cada giro posible
            weight = Math.Max(1f, weight);

            weights[i] = weight;
            totalWeight += weight;
        }

        // Hacemos girar la ruleta (Tirar el dado)
        float roll = (float)rng.NextDouble() * totalWeight;
        float cursor = 0f;

        for (int i = 0; i < candidates.Count; i++)
        {
            cursor += weights[i];
            if (roll <= cursor)
                return candidates[i]; // ¡Calle seleccionada!
        }

        return candidates[0]; // Fallback de seguridad
    }

    // ---------------------------------------------------------------
    public override void Deliberate(World world)
    {
        laneChangeCooldown -= world.DeltaTime;
        ConsiderLaneChange();
        ChooseSpeed(world);
    }

    void ConsiderLaneChange()
    {
        if (IsChangingLane)          return;
        if (laneChangeCooldown > 0f) return;
        if (OnConnector)             return;
        if (CurrentLane.Edge.Lanes.Count < 2) return;

        // Priority 1: must migrate toward DesiredLane for routing
        if (DesiredLane >= 0 && DesiredLane != LaneNumber)
        {
            float progress = 1f - Math.Min(1f, DistanceToEnd / CurrentLane.Edge.Length);
            if (progress > 0.2f)
            {
                int dir    = DesiredLane > LaneNumber ? 1 : -1;
                int target = LaneNumber + dir;
                if (BeginLaneChange(target))
                    laneChangeCooldown = LANE_CHANGE_COOLDOWN;
            }
            return;
        }

        // Priority 2: opportunistic — blocked ahead
        if (GapAhead > profile.MinFollowingDistance * 2f) return;
        if ((float)rng.NextDouble() > 0.15f) return;

        int[] candidates = { LaneNumber - 1, LaneNumber + 1 };
        foreach (int target in candidates)
        {
            if (BeginLaneChange(target))
            {
                laneChangeCooldown = LANE_CHANGE_COOLDOWN;
                break;
            }
        }
    }

    void ChooseSpeed(World world)
    {
        float v0 = TrafficLaw.SpeedLimitMs(this) * profile.DesiredSpeedFactor; // desired speed
        float a  = acceleration;
        float b  = 4f;    // comfortable braking deceleration m/s²
        float s0 = profile.MinFollowingDistance;
        float T  = profile.DesiredTimeGap;

        // IDM acceleration
        float idmAccel = a * (1f - IDMPow4(Speed / Math.Max(v0, 0.1f)));

        // Car-following term: only when there is a vehicle ahead
        if (AheadOnLane != null && GapAhead < float.MaxValue / 2f)
        {
            float deltaV = Speed - AheadOnLane.Speed;          // approach rate (positive = closing)
            float sStar  = s0 + Speed * T
                         + Speed * deltaV / (2f * (float)Math.Sqrt(a * b));
            sStar = Math.Max(s0, sStar);                       // never less than min gap

            float ratio = sStar / Math.Max(GapAhead, 0.01f);
            idmAccel   -= a * ratio * ratio;                    // IDM braking term
        }

        // Zipper merge — yield to cars inside connectors that feed our lane.
        // This creates the gap rather than waiting for one to appear.
        float zipperCap = ZipperCap(world.Navigation);

        // Slow while lane-changing
        if (IsChangingLane) idmAccel -= a * 0.5f;

        // Slow proactively when a required lane change is running out of road
        if (DesiredLane >= 0 && DesiredLane != LaneNumber)
        {
            float urgency = 1f - Math.Min(1f, DistanceToEnd / CurrentLane.Edge.Length);
            if (urgency > 0.5f)
                idmAccel -= a * (urgency - 0.5f) * 1.2f;
        }

        // Junction entry guard — skipped when stuck to break circular deadlocks
        float junctionCap = IsStuck ? float.MaxValue : JunctionEntryCap(world.Navigation);

        // Roundabout yield
        if (TrafficLaw.MustYieldToRoundabout(this) && TrafficLaw.IsRoundaboutOccupied(this))
            junctionCap = Math.Min(junctionCap, ComputeBrakingSpeed(DistanceToEnd));

        // Integrate speed
        float newSpeed = Speed + idmAccel * world.DeltaTime;
        newSpeed = Math.Max(0f, newSpeed);                     // never reverse
        newSpeed = Math.Min(newSpeed, v0);                     // never exceed desired
        newSpeed = Math.Min(newSpeed, junctionCap);
        newSpeed = Math.Min(newSpeed, zipperCap);
        newSpeed = ApplyBrakingConstraints(newSpeed);

        Speed = newSpeed;
    }

    // IDM uses v^4 normalisation for free-flow term
    static float IDMPow4(float x)
    {
        float x2 = x * x;
        return x2 * x2;
    }

    /// <summary>
    /// Zipper merge speed cap.
    ///
    /// For each connector lane that feeds into our current lane, if it has
    /// a vehicle in it we slow down to create a gap at the merge point —
    /// exactly like a zipper merge in real traffic.
    ///
    /// Returns a speed cap (float.MaxValue = no restriction).
    /// </summary>
    float ZipperCap(NavigationGraph nav)
    {
        if (OnConnector) return float.MaxValue;

        float cap = float.MaxValue;

        foreach (var conn in CurrentLane.IncomingConnectors)
        {
            if (conn.Vehicles.Count == 0) continue;

            // Find where this connector exits onto our lane
            var exitLinks = nav.GetLinksFrom(conn);
            if (exitLinks.Count == 0) continue;

            float mergePos = exitLinks[0].MergePosition * CurrentLane.Edge.Length;

            // How far are we from the merge point?
            float distToMerge = mergePos - Position;

            // Only yield if we're approaching the merge (it's ahead of us)
            // and within a reasonable lookahead distance
            float lookahead = Math.Max(Speed * 3f, 20f);
            if (distToMerge < 0f || distToMerge > lookahead) continue;

            // The connector vehicle's speed tells us what gap they'll need
            var merging = conn.Vehicles[conn.Vehicles.Count - 1]; // frontmost

            // Skip stopped connector cars — yielding to them would cause a
            // cascade freeze across the whole road network (targetSpeed → 0).
            if (merging.Speed < 0.5f) continue;

            // We want to arrive at the merge point just *after* the merging
            // car does — classic zipper. Cap our speed so the merging car
            // gets there first.
            float timeToMerge    = distToMerge / Math.Max(Speed, 0.1f);
            float connProgress   = merging.Position / Math.Max(conn.Edge.Length, 0.1f);
            float connRemaining  = (1f - connProgress) * conn.Edge.Length;
            float mergingArrival = connRemaining / Math.Max(merging.Speed, 0.5f);

            // If the merging car arrives before us — we don't need to slow
            if (mergingArrival < timeToMerge) continue;

            // We'd arrive first — slow so they can slot in ahead of us.
            // Target: arrive at mergePoint just after merging car + one car length gap.
            // Clamp to a creep minimum so we never fully stop for a zipper.
            float targetArrival = mergingArrival + (Length / Math.Max(merging.Speed, 1f));
            float targetSpeed   = distToMerge / Math.Max(targetArrival, 0.01f);
            cap = Math.Min(cap, Math.Max(1.5f, targetSpeed));
        }

        return cap;
    }

    public override void Act(World world) => Move(world);

    /// <summary>
    /// Returns true if following this link leads to a dead-end segment
    /// (one whose end node has no outgoing edges in the navigation graph).
    /// </summary>
    static bool IsDeadEndLink(LaneLink link, NavigationGraph graph)
    {
        Lane dest = link.DestLane;

        // If the link goes to a connector, peek at where that connector exits
        if (dest.Edge.IsConnector)
        {
            var onward = graph.GetLinksFrom(dest);
            dest = onward.Count > 0 ? onward[0].DestLane : null;
        }

        return dest == null
            || dest.Edge.to == null
            || dest.Edge.to.Outgoing.Count == 0;
    }
}
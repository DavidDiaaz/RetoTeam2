/// <summary>
/// Defines a valid lane-to-lane continuation between two adjacent road segments.
///
/// With connector lanes, every physical junction is represented as:
///   SourceLane →[LinkA]→ ConnectorLane →[LinkB]→ DestLane
///
/// LinkA: IsStraight=true,  MergePosition=0  (enter connector at its start)
/// LinkB: IsStraight=false for side entries, MergePosition = side-entry offset
///
/// Cars travelling the connector lane use the same following-distance and
/// space-check logic as any other lane — no special-casing needed.
/// </summary>
public class LaneLink
{
    public Lane  SourceLane;
    public Lane  DestLane;
    public float MergePosition; // normalised [0,1] on DestLane. 0 = segment start.

    /// <summary>
    /// True when DestLane is a connector lane (the first hop of a junction).
    /// Used by yield logic to look one link further ahead to the real road.
    /// </summary>
    public bool ToConnector { get; private set; }

    /// <summary>
    /// True when SourceLane is a connector lane (the second hop, exiting the junction).
    /// </summary>
    public bool FromConnector { get; private set; }

    public LaneLink(Lane source, Lane dest, float mergePosition = 0f)
    {
        SourceLane    = source;
        DestLane      = dest;
        MergePosition = mergePosition;

        ToConnector   = dest.Edge?.IsConnector  ?? false;
        FromConnector = source.Edge?.IsConnector ?? false;
    }

    /// <summary>True if this is a straight continuation (car enters at the start of the dest lane).</summary>
    public bool IsStraight => MergePosition <= 0.01f;
}
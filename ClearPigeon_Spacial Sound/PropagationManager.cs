using System.Collections.Generic;
using UnityEngine;
using ClearPigeon.Audio;

public class PropagationManager
{
    private const int MaxRooms = 1024;
    private readonly bool[] visited = new bool[MaxRooms];
    private readonly int[] cameFrom = new int[MaxRooms];
    private readonly RoomPortal[] exitPortals = new RoomPortal[MaxRooms];
    private readonly float[] roomOcclusion = new float[MaxRooms];
    private readonly int[] roomDepth = new int[MaxRooms];
    private readonly Dictionary<Room, int> roomToIndex = new Dictionary<Room, int>(MaxRooms);
    private readonly Room[] indexToRoom = new Room[MaxRooms];
    private readonly RoomData[] indexToData = new RoomData[MaxRooms];
    private int roomCount;
    private readonly float[] pathCost = new float[MaxRooms];

    public PropagationManager() { }

    private void SetupRooms(Dictionary<Room, RoomData> roomGraph)
    {
        roomToIndex.Clear();
        roomCount = 0;
        foreach (var kv in roomGraph)
        {
            if (roomCount >= MaxRooms) break;
            var room = kv.Key;
            roomToIndex[room] = roomCount;
            indexToRoom[roomCount] = room;
            indexToData[roomCount] = kv.Value;
            roomCount++;
        }
    }

    public List<Room> FindPath(
     Room startRoom,
     Room targetRoom,
     Dictionary<Room, RoomData> roomGraph,
     Audio_Asset asset,
     Audio_Source source,
     Vector3 listenerPosition,
     out float totalOcclusion,
     bool debug = false,
     int maxDepths = 10,
     int portalOffset = 0,
     float maxCost = 120f,
     bool cheapOcclusion = false) 
    { 
        totalOcclusion = 0f;
        if (startRoom == null || targetRoom == null || roomGraph == null)
        {
            Debug.LogError("Invalid input to FindPath");
            return null;
        }

        SetupRooms(roomGraph);

        if (!roomToIndex.TryGetValue(startRoom, out int startIdx) ||
            !roomToIndex.TryGetValue(targetRoom, out int targetIdx))
        {
            Debug.LogError("Start or Target room not in graph");
            return null;
        }

        for (int i = 0; i < roomCount; i++)
        {
            visited[i] = false;
            cameFrom[i] = -1;
            exitPortals[i] = null;
            roomOcclusion[i] = 0f;
            roomDepth[i] = 0;
            pathCost[i] = float.MaxValue;
        }

        pathCost[startIdx] = 0f;

        if (!cheapOcclusion)
        {
            PriorityQueue<Room> queue = new PriorityQueue<Room>();
            queue.Enqueue(startRoom, 0f);
            visited[startIdx] = true;
            roomDepth[startIdx] = 0;
            roomOcclusion[startIdx] = 0f;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                int curIdx = roomToIndex[current];
                int curDepth = roomDepth[curIdx];
                float curOcc = roomOcclusion[curIdx];

                if (current == targetRoom)
                    break;

                if (curDepth >= maxDepths)
                {
                    totalOcclusion = 255;
                    continue;
                }

                var data = indexToData[curIdx];
                ProcessPortals(current, data, asset, listenerPosition, ref queue, ref totalOcclusion, curIdx, curDepth, curOcc, portalOffset, maxCost);
            }
        }
        if (!visited[targetIdx])
        {
            if (debug) Debug.LogError("No path found.");
            return null;
        }

        return ReconstructPath(startIdx, targetIdx, out totalOcclusion, portalOffset, maxDepths, asset, listenerPosition, targetRoom, debug);
    }

    private void ProcessPortals(Room current, RoomData data, Audio_Asset asset, Vector3 listenerPosition, ref PriorityQueue<Room> queue, ref float totalOcclusion, int curIdx, int curDepth, float curOcc, int portalOffset, float maxCost)
    {
        foreach (var p in data.portals)
        {
            if (p.room1 == null || p.room2 == null) continue;
            bool isOpen = p._soundBlocker == null || p._soundBlocker.IsOpen;
            if (!isOpen) continue;

            var nbr = p.room1 == current ? p.room2 : p.room1;
            int nbrIdx = roomToIndex[nbr];
            if (visited[nbrIdx]) continue;
            EnqueueNeighbor(curIdx, nbr, nbrIdx, p, asset, curDepth, curOcc, ref queue, listenerPosition, maxCost);
        }

        foreach (var p in data.portals)
        {
            if (p.room1 == null || p.room2 == null) continue;
            bool isOpen = p._soundBlocker == null || p._soundBlocker.IsOpen;
            if (isOpen) continue;

            var nbr = p.room1 == current ? p.room2 : p.room1;
            int nbrIdx = roomToIndex[nbr];
            if (visited[nbrIdx]) continue;
            EnqueueNeighbor(curIdx, nbr, nbrIdx, p, asset, curDepth, curOcc, ref queue, listenerPosition, maxCost);
        }

        foreach (var nbr in data.neighbors)
        {
            if (nbr == null) continue;
            int nbrIdx = roomToIndex[nbr];
            if (visited[nbrIdx]) continue;
            EnqueueNeighbor(curIdx, nbr, nbrIdx, null, asset, curDepth, curOcc, ref queue, listenerPosition, maxCost);
        }
    }

    private void EnqueueNeighbor(int curIdx, Room nbr, int nbrIdx, RoomPortal portal, Audio_Asset asset, int curDepth, float curOcc, ref PriorityQueue<Room> queue, Vector3 listenerPosition, float maxCost)
    {
        float cost = CalculatePortalCost(portal, listenerPosition);
        float newCost = pathCost[curIdx] + cost;

        // Ensure we skip any paths that exceed the max allowed cost
        if (newCost > maxCost)
            return;

        if (newCost < pathCost[nbrIdx])
        {
            cameFrom[nbrIdx] = curIdx;
            exitPortals[nbrIdx] = portal;
            roomDepth[nbrIdx] = curDepth + 1;
            pathCost[nbrIdx] = newCost;
            roomOcclusion[nbrIdx] = 0f;

            if (!visited[nbrIdx])
            {
                visited[nbrIdx] = true;
                queue.Enqueue(nbr, newCost);
            }
        }
    }

    private float CalculatePortalCost(RoomPortal portal, Vector3 listenerPosition)
    {
        if (portal == null) return 1f;

        float baseWeight = portal._weight;
        float scaleFactor = portal.transform.lossyScale.magnitude;
        bool isOpen = portal._soundBlocker == null || portal._soundBlocker.IsOpen;
        float openPenalty = isOpen ? 1f : 2f;

        float dist = portal.transform != null
            ? Vector3.Distance(listenerPosition, portal.transform.position)
            : 1f;

        float distWeight = Mathf.Clamp(dist / portal._soundBlocker.attinuationAggression, 0.5f, 1.5f); // relaxed

        float frequencyFactor = isOpen ? 1f : 1.2f;

        // Prefer larger portals by reducing cost inversely with the scale
        float scaleBonus = Mathf.Clamp(1f / scaleFactor, 0.1f, 10f); // Inverse scaling for larger portals

        float rawCost = (baseWeight + openPenalty + (distWeight /5) + frequencyFactor);
        return rawCost * scaleBonus; // Multiply by scaleBonus to lower the cost for larger portals
    }


    private List<Room> ReconstructPath(int startIdx, int targetIdx, out float totalOcclusion, int portalOffset, int maxDepth, Audio_Asset asset, Vector3 listenerPosition, Room listenerRoom, bool debug)
    {
        var path = new List<Room>();
        int cursor = targetIdx;
        while (cursor != -1)
        {
            path.Add(indexToRoom[cursor]);
            cursor = cameFrom[cursor];
        }
        path.Reverse();

        totalOcclusion = 0f;
        const float maxOcclusion = 255f;

        int step = 1, portalCount = 0;
        float totalWeight = 0f;

        float[] weights = new float[path.Count];
        int listenerIdx = roomToIndex[listenerRoom];

        for (int i = 0; i < path.Count - 1 && i < maxDepth; i++)
        {
            int curIdx = roomToIndex[path[i]];
            int nextIdx = roomToIndex[path[i + 1]];

            if (indexToRoom[curIdx] == listenerRoom)
                break;

            var portal = exitPortals[nextIdx];
            if (portal == null) continue;

            var sb = portal._soundBlocker;
            if (sb != null && !sb.IsOpen && portalCount++ >= portalOffset)
            {
                float distanceWeight = 1f;
                if (portal.transform != null)
                {                                                                                                                  
                   float dist =  (listenerPosition - portal.transform.position).sqrMagnitude;

                                                                                                  // multiplier to help define the distance
                                                                                                                    // v
                    // the below equation controls how much distance weighs on the effect.  (dist    /   10f) *       2f +      1f; < kinda just extra help
                                                                                        //   ^            ^
                                                                                        // float     denominator  (aggresiveness)

                    distanceWeight = Mathf.Clamp01(dist / sb.attinuationAggression) * sb.attinuationInfluence + 1f; 
                                                                               
                }

                float depthWeight = 1f - (step - 1f) / maxDepth;
                depthWeight = Mathf.Clamp01(depthWeight);

                float weightedOcclusion = sb.occlusionAmount * distanceWeight * depthWeight;

                totalOcclusion += weightedOcclusion * asset.Blockability;
                totalWeight += depthWeight;
                if (debug)
                {
                    string portalName = portal?.name ?? "Unknown";
                    var blocker = portal._soundBlocker;
                    string state = blocker == null ? "no blocker" : (blocker.IsOpen ? "open" : "closed");

                    Debug.Log($"Asset: {asset} -- Portal '{portalName}' was {state} -- occlusion: {weightedOcclusion:F2} (depthWeight: {depthWeight:F2}, distWeight: {distanceWeight:F2}) at step {step}");
                }

                if (totalOcclusion >= maxOcclusion)
                {
                    totalOcclusion = maxOcclusion;
                    if (debug) Debug.Log("Occlusion limit reached. Stopping trace.");
                    break;
                }

            }

            step++;
        }

        totalOcclusion = Mathf.Clamp(totalOcclusion, 0f, maxOcclusion);

        return path;
    }

    public class PriorityQueue<T>
    {
        private readonly List<(T item, float priority)> _elements = new();

        public int Count => _elements.Count;

        public void Enqueue(T item, float priority)
        {
            _elements.Add((item, priority));
            int c = _elements.Count - 1;
            while (c > 0 && _elements[c].priority < _elements[(c - 1) / 2].priority)
            {
                (_elements[c], _elements[(c - 1) / 2]) = (_elements[(c - 1) / 2], _elements[c]);
                c = (c - 1) / 2;
            }
        }

        public T Dequeue()
        {
            int li = _elements.Count - 1;
            var frontItem = _elements[0].item;
            _elements[0] = _elements[li];
            _elements.RemoveAt(li);

            --li;
            int pi = 0;
            while (true)
            {
                int ci = pi * 2 + 1;
                if (ci > li) break;
                int rc = ci + 1;
                if (rc <= li && _elements[rc].priority < _elements[ci].priority) ci = rc;
                if (_elements[pi].priority <= _elements[ci].priority) break;
                (_elements[pi], _elements[ci]) = (_elements[ci], _elements[pi]);
                pi = ci;
            }
            return frontItem;
        }

        public bool Contains(T item)
        {
            return _elements.Exists(e => EqualityComparer<T>.Default.Equals(e.item, item));
        }
    }
}
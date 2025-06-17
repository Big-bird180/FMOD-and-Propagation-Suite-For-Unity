using System.Collections.Generic;
using System;
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
            visited[roomCount] = false;
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

        if (startRoom == targetRoom)
        {
            totalOcclusion = 0f;
            return new List<Room> { startRoom };
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
            roomDepth[startIdx] = 0;
            roomOcclusion[startIdx] = 0f;

            while (queue.Count > 0)
            {
                var (current, priority) = queue.DequeueWithPriority();
                int curIdx = roomToIndex[current];

                if (priority > pathCost[curIdx])
                    continue;

                int curDepth = roomDepth[curIdx];
                float curOcc = roomOcclusion[curIdx];

                if (visited[curIdx])
                    continue;

                visited[curIdx] = true;

                if (current == targetRoom)
                    break;

                if (curDepth >= maxDepths)
                {
                    totalOcclusion = 255;
                    continue;
                }

                ProcessPortals(current,
                               indexToData[curIdx],
                               asset, source,
                               listenerPosition,
                               ref queue,
                               ref totalOcclusion,
                               curIdx,
                               curDepth,
                               curOcc,
                               portalOffset,
                               maxCost);
            }
        }

        if (!visited[roomToIndex[targetRoom]])
        {
            if (debug) Debug.LogError("No path found.");
            return null;
        }

        return ReconstructPath(
            roomToIndex[startRoom],
            roomToIndex[targetRoom],
            out totalOcclusion,
            portalOffset,
            maxDepths,
            asset,
            listenerPosition,
            targetRoom,
            debug);
    }


    private void ProcessPortals(
        Room current,
        RoomData data,
        Audio_Asset asset,
        Audio_Source source,
        Vector3 listenerPosition,
        ref PriorityQueue<Room> queue,
        ref float totalOcclusion,
        int curIdx,
        int curDepth,
        float curOcc,
        int portalOffset,
        float maxCost)
    {
        // 1) Pick the best portal per neighbor, storing both portal and its precomputed cost
        var bestPortals = new Dictionary<Room, (RoomPortal portal, float cost)>();
        foreach (var p in data.portals)
        {
            if (p.room1 == null || p.room2 == null) continue;
            var nbr = p.room1 == current ? p.room2 : p.room1;
            if (nbr == null) continue;

            float cost = CalculatePortalCost(p, source, listenerPosition);
            if (!bestPortals.TryGetValue(nbr, out var best) || cost < best.cost)
                bestPortals[nbr] = (p, cost);
        }

        // 2) Enqueue using that exact portal and cost
        foreach (var kv in bestPortals)
        {
            int nbrIdx = roomToIndex[kv.Key];
            EnqueueNeighbor(curIdx,
                            kv.Key,
                            nbrIdx,
                            kv.Value.portal,
                            kv.Value.cost,
                            ref queue,
                            maxCost);
        }

        // 3) Fallback to non-portal neighbors if needed
        foreach (var nbr in data.neighbors)
        {
            if (nbr == null || bestPortals.ContainsKey(nbr)) continue;
            int nbrIdx = roomToIndex[nbr];
            EnqueueNeighbor(curIdx,
                            nbr,
                            nbrIdx,
                            null,
                            1f,
                            ref queue,
                            maxCost);
        }
    }

    // Updated signature: accepts precomputed portalCost
    private void EnqueueNeighbor(
        int curIdx,
        Room nbr,
        int nbrIdx,
        RoomPortal portal,
        float portalCost,
        ref PriorityQueue<Room> queue,
        float maxCost)
    {
        float depthPenalty = Mathf.Min(roomDepth[curIdx] * 0.1f, 1f);
        float newCost = pathCost[curIdx] + portalCost + depthPenalty;

        if (newCost > maxCost)
            return;

        if (newCost < pathCost[nbrIdx])
        {
            cameFrom[nbrIdx] = curIdx;
            exitPortals[nbrIdx] = portal;
            roomDepth[nbrIdx] = roomDepth[curIdx] + 1;
            pathCost[nbrIdx] = newCost;
            roomOcclusion[nbrIdx] = 0f;

            queue.Enqueue(nbr, newCost);
        }
    }

    private float CalculatePortalCost(RoomPortal portal, Audio_Source source, Vector3 listenerPosition)
    {
        if (portal == null) return 100f;

        // === Tunables ===
        const float openPenalty = 1f;
        const float closedPenalty = 4f;
        const float distanceWeight = 1f;
        const float frequencyWeight = 0.7f;
        const float minCost = 0.5f;
        const float maxCost = 100f;

        // === Portal properties ===
        Vector3 portalPos = portal.transform.position;
        float scaleMagnitude = portal.transform.lossyScale.magnitude;
        float scaleFactor = Mathf.Clamp01(1f / (scaleMagnitude * scaleMagnitude)); // Normalized: [0..1], large portal = cheap

        bool isOpen = portal._soundBlocker == null || portal._soundBlocker.IsOpen;
        float attenuation = Mathf.Max(portal._soundBlocker?.attinuationAggression ?? 1f, 0.01f); // Avoid divide-by-zero

        float opennessPenalty = isOpen ? openPenalty : closedPenalty;
        float frequencyFactor = isOpen ? 1f : 1.25f;

        float sourceDist = Vector3.Distance(source.transform.position, portalPos);
        float listenerDist = Vector3.Distance(listenerPosition, portalPos);
        float weightedDist = (sourceDist * 0.4f) + (listenerDist * 0.6f);

        // === Normalize distance impact ===
        float distancePenalty = Mathf.Clamp01(weightedDist / 20f); // Assume 20m is "long range"

        // === Final cost formula ===
        float cost = (opennessPenalty)
                     + (distancePenalty * distanceWeight)
                     + (frequencyFactor * frequencyWeight);

        cost = Mathf.Clamp(cost, minCost, maxCost) + portal._soundBlocker.occlusionAmount;

        Debug.Log($"Portal: {portal.name}, Cost: {cost:F2}, Dist: {weightedDist:F2}, Open: {isOpen}, Scale: {scaleMagnitude:F2}");

        return cost;
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
                    float dist = (listenerPosition - portal.transform.position).sqrMagnitude;

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

                   // Debug.Log($"Asset: {asset} -- Portal '{portalName}' was {state} -- occlusion: {weightedOcclusion:F2} (depthWeight: {depthWeight:F2}, distWeight: {distanceWeight:F2}) at step {step}");
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
        private List<Node> _heap = new();
        private Dictionary<T, int> _itemToIndex = new();

        public int Count => _heap.Count;

        public void Enqueue(T item, float priority)
        {
            if (_itemToIndex.TryGetValue(item, out int index))
            {
                // Item already exists, check if priority should be updated
                if (priority >= _heap[index].Priority)
                    return;

                _heap[index] = new Node(item, priority);
                SiftUp(index);
                SiftDown(index);
            }
            else
            {
                _heap.Add(new Node(item, priority));
                _itemToIndex[item] = _heap.Count - 1;
                SiftUp(_heap.Count - 1);
            }
        }

        public (T item, float priority) DequeueWithPriority()
        {
            if (_heap.Count == 0)
                throw new InvalidOperationException("PriorityQueue is empty.");

            var node = _heap[0];
            _itemToIndex.Remove(node.Item);

            int last = _heap.Count - 1;
            if (last > 0)
            {
                _heap[0] = _heap[last];
                _itemToIndex[_heap[0].Item] = 0;
                _heap.RemoveAt(last);
                SiftDown(0);
            }
            else
            {
                _heap.RemoveAt(last);
            }

            return (node.Item, node.Priority);
        }


        public bool Contains(T item) => _itemToIndex.ContainsKey(item);

        public void Clear()
        {
            _heap.Clear();
            _itemToIndex.Clear();
        }

        public float GetPriority(T item)
        {
            if (_itemToIndex.TryGetValue(item, out int index))
                return _heap[index].Priority;

            return float.MaxValue;
        }

        private void SiftUp(int index)
        {
            var node = _heap[index];
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (_heap[parent].Priority <= node.Priority)
                    break;

                _heap[index] = _heap[parent];
                _itemToIndex[_heap[parent].Item] = index;
                index = parent;
            }
            _heap[index] = node;
            _itemToIndex[node.Item] = index;
        }

        private void SiftDown(int index)
        {
            var node = _heap[index];
            int half = _heap.Count >> 1;

            while (index < half)
            {
                int left = (index << 1) + 1;
                int right = left + 1;
                int smallest = left;

                if (right < _heap.Count && _heap[right].Priority < _heap[left].Priority)
                    smallest = right;

                if (_heap[smallest].Priority >= node.Priority)
                    break;

                _heap[index] = _heap[smallest];
                _itemToIndex[_heap[smallest].Item] = index;
                index = smallest;
            }

            _heap[index] = node;
            _itemToIndex[node.Item] = index;
        }

        private struct Node
        {
            public T Item { get; }
            public float Priority { get; }

            public Node(T item, float priority)
            {
                Item = item;
                Priority = priority;
            }
        }
    }


}

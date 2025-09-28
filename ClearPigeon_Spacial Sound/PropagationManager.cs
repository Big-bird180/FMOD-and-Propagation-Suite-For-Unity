using System.Collections.Generic;
using System;
using UnityEngine;
using ClearPigeon.Audio;
using Unity.Mathematics;
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
    private bool built = false;
    private readonly Dictionary<Room, (RoomPortal portal, float cost)> _bestPortalsCache = new();
    // Class-level reusable list, initialized once
    private List<Room> reusablePath = new List<Room>();
    private PriorityQueue<int> reusableQueue = new PriorityQueue<int>();
    private readonly Room[] reversePathBuffer = new Room[MaxRooms];
    private float3[] cachedPortalPositions = new float3[MaxRooms];  // or MaxPortals if needed

    private Dictionary<SoundBlocker, float> aggressionLogCache = new Dictionary<SoundBlocker, float>();
    public PropagationManager() { }


    private void SetupRooms(Dictionary<Room, RoomData> roomGraph)
    {
        if (built) return;

        roomToIndex.Clear();
        roomCount = 0;

        var enumerator = roomGraph.GetEnumerator();
        while (enumerator.MoveNext() && roomCount < MaxRooms)
        {
            var kv = enumerator.Current;
            var room = kv.Key;
            visited[roomCount] = false;
            roomToIndex[room] = roomCount;
            indexToRoom[roomCount] = room;
            indexToData[roomCount] = kv.Value;
            roomCount++;
        }

        built = true;
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
            reusablePath.Clear();
            return reusablePath;

        }

        if (startRoom == targetRoom)
        {
            totalOcclusion = 0f;
            reusablePath.Clear();
            reusablePath.Add(startRoom);
            return reusablePath;
        }

        SetupRooms(roomGraph);

        if (!roomToIndex.TryGetValue(startRoom, out int startIdx) ||
            !roomToIndex.TryGetValue(targetRoom, out int targetIdx))
        {
            Debug.LogError("Start or Target room not in graph");
            reusablePath.Clear();
            return reusablePath;
        }

        int rc = roomCount;
        for (int i = 0; i < rc; i++)
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
            reusableQueue.Clear();
            reusableQueue.Enqueue(startIdx, 0f);



            while (reusableQueue.Count > 0)
            {
                reusableQueue.DequeueWithPriority(out var curIdx, out var priority);
                Room current = indexToRoom[curIdx];
                if (priority > pathCost[curIdx] || visited[curIdx])
                    continue;

                int curDepth = roomDepth[curIdx];
                float curOcc = roomOcclusion[curIdx];

                if (priority > pathCost[curIdx] || visited[curIdx])
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
                               ref reusableQueue,
                               ref totalOcclusion,
                               curIdx,
                               curDepth,
                               curOcc,
                               portalOffset,
                               maxCost,
                               debug);
            }
        }

        if (!visited[targetIdx])
        {
            if (debug) Debug.LogError("No path found.");
            return null;
        }
        for (int i = 0; i < roomCount; i++)
        {
            if (exitPortals[i] != null)
                cachedPortalPositions[i] = exitPortals[i].transform.position;
        }

        return ReconstructPath(
            startIdx,
            targetIdx,
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
    ref PriorityQueue<int> queue,
    ref float totalOcclusion,
    int curIdx,
    int curDepth,
    float curOcc,
    int portalOffset,
    float maxCost,
    bool debug)
    {
        _bestPortalsCache.Clear();
        var roomToIndexLocal = roomToIndex;

        int portalCount = data.portals.Count;
        for (int i = 0; i < portalCount; i++)
        {
            var p = data.portals[i];
            Room nbr = (p.room1 == current) ? p.room2 : p.room1;
            if (nbr == null) continue;

            float cost = CalculatePortalCost(p, source, listenerPosition, debug);
            if (!_bestPortalsCache.TryGetValue(nbr, out var best) || cost < best.cost)
            {
                _bestPortalsCache[nbr] = (p, cost);
            }
        }

        var enumerator = _bestPortalsCache.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var kv = enumerator.Current;
            if (roomToIndexLocal.TryGetValue(kv.Key, out int nbrIdx))
            {
                EnqueueNeighbor(curIdx, nbrIdx, kv.Value.portal, kv.Value.cost, ref queue, maxCost);

            }
        }

        int neighborCount = data.neighbors.Count;
        for (int i = 0; i < neighborCount; i++)
        {
            var nbr = data.neighbors[i];
            if (nbr == null || _bestPortalsCache.ContainsKey(nbr)) continue;

            if (roomToIndexLocal.TryGetValue(nbr, out int nbrIdx))
            {
                const float fallbackCost = 1.0f;
                EnqueueNeighbor(curIdx, nbrIdx, null, fallbackCost, ref queue, maxCost);

            }
        }
    }

    private void EnqueueNeighbor(
        int curIdx,
        int nbrIdx,
        RoomPortal portal,
        float portalCost,
        ref PriorityQueue<int> queue,
        float maxCost)

    {
        float depthPenalty = Mathf.Min(roomDepth[curIdx] * 0.1f, 1f);
        float newCost = pathCost[curIdx] + portalCost + depthPenalty;

        if (newCost > maxCost || newCost >= pathCost[nbrIdx])
            return;

        cameFrom[nbrIdx] = curIdx;
        exitPortals[nbrIdx] = portal;
        roomDepth[nbrIdx] = roomDepth[curIdx] + 1;
        pathCost[nbrIdx] = newCost;
        roomOcclusion[nbrIdx] = 0f;

        queue.Enqueue(nbrIdx, newCost);

    }


    private float CalculatePortalCost(RoomPortal portal, Audio_Source source, Vector3 listenerPosition, bool debug)
    {
        if (portal == null) return 100f;

        const float openPenalty = 1f;
        const float closedPenalty = 2f;
        const float distanceWeight = 2f;
        const float freqWeight = 1f;
        const float minCost = 0.5f;
        const float maxCost = 100f;
        const float maxDistance = 20f;

        var pos = portal.transform.position;
        bool isOpen = portal._soundBlocker == null || portal._soundBlocker.IsOpen;
        float penalty = isOpen ? openPenalty : closedPenalty;
        float freqFactor = isOpen ? 1f : 1.25f;

        Vector3 sPos = source.transform.position;
        float distSqrSource = (sPos - pos).sqrMagnitude;
        float distSqrListener = (listenerPosition - pos).sqrMagnitude;
        float weightedDistSqr = (distSqrSource * 0.4f) + (distSqrListener * 0.6f);
        float dist = Mathf.Sqrt(weightedDistSqr);
        float distancePenalty = dist > maxDistance ? 1f : (dist / maxDistance);

        float cost = penalty + (distancePenalty * distanceWeight) + (freqFactor * freqWeight);

        // --- NEW: reduce cost if same layer ---
        Room currentRoom = (portal.room1 != null && portal.room1.roomLayer == portal.room2.roomLayer)
                           ? portal.room1
                           : portal.room1 ?? portal.room2;

        Room neighborRoom = (portal.room1 == currentRoom) ? portal.room2 : portal.room1;

        if (neighborRoom != null && neighborRoom.roomLayer == currentRoom.roomLayer)
        {
            cost *= 0.5f; // prefer same-layer rooms
            if (debug) Debug.Log($"Preferring same-layer portal: {portal.name}, cost reduced to {cost:F2}");
        }

        cost = Mathf.Clamp(cost, minCost, maxCost);
        if (debug) Debug.Log($"Portal: {portal.name}, Cost: {cost:F2}, Dist: {dist:F2}, Open: {isOpen}");

        return cost;
    }



    private List<Room> ReconstructPath(
        int startIdx,
        int targetIdx,
        out float totalOcclusion,
        int portalOffset,
        int maxDepth,
        Audio_Asset asset,
        Vector3 listenerPosition,
        Room listenerRoom,
        bool debug)
    {
        reusablePath.Clear();

        int cursor = targetIdx;
        int length = 0;
        while (cursor != -1 && length < MaxRooms)
        {
            reversePathBuffer[length++] = indexToRoom[cursor];
            cursor = cameFrom[cursor];
        }

        reusablePath.Clear();
        for (int i = length - 1; i >= 0; i--)
            reusablePath.Add(reversePathBuffer[i]);

        totalOcclusion = 0f;
        const float maxOcclusion = 255f;

        int listenerIdx = roomToIndex[listenerRoom];
        int portalCount = 0;
        int step = 1;

        float blockability = asset.Blockability;
        float maxDepthScale = 1f / math.max(1f, maxDepth);

        float3 listenerPos = listenerPosition;
        var roomToIndexLocal = roomToIndex;
        var exitPortalsLocal = exitPortals;

        for (int i = 0; i < reusablePath.Count - 1 && i < maxDepth; i++)
        {
            int curIdx = roomToIndexLocal[reusablePath[i]];
            int nextIdx = roomToIndexLocal[reusablePath[i + 1]];

            if (curIdx == listenerIdx)
                break;

            RoomPortal portal = exitPortalsLocal[nextIdx];
            if (portal == null) continue;

            SoundBlocker sb = portal._soundBlocker;
            if (sb == null) continue;

            float distanceWeight = 1f;

            // Use cached portal positions instead of Transform calls
            float3 portalPos = cachedPortalPositions[nextIdx];
            float dist = math.distance(listenerPos, portalPos);

            float baseAgg = math.max(sb.attinuationAggression, 1f);
            float aggression = baseAgg;

            if (!aggressionLogCache.TryGetValue(sb, out float logAgg))
            {
                logAgg = math.log10(aggression);
                aggressionLogCache[sb] = logAgg;
            }

            float influence = sb.attinuationInfluence;
            float normalized = math.clamp(math.log10(dist + 1f) / logAgg, 0f, 1f);


            distanceWeight = 0.85f + (normalized * influence);

            float depthWeight = math.clamp((step - 1f) / maxDepth, 0f, 1f);
            float baseOcclusion = sb.IsOpen ? 0 : sb.occlusionAmount;
            float weightedOcclusion = baseOcclusion + depthWeight + distanceWeight;

            float scaledOcclusion = weightedOcclusion * maxDepthScale;

            totalOcclusion += (scaledOcclusion * distanceWeight) * blockability;

            if (!sb.IsOpen && portalCount++ >= portalOffset)
            {
                if (debug)
                {
                    UnityEngine.Debug.LogFormat(
                        "Asset: {0} -- Portal '{1}' CLOSED -- occlusion: {2:F2}, depthWeight: {3:F2}, distWeight: {4:F2}, step: {5}",
                        asset, portal.name, weightedOcclusion, depthWeight, distanceWeight, step);
                }

                if (totalOcclusion >= maxOcclusion)
                {
                    totalOcclusion = maxOcclusion;
                    if (debug) UnityEngine.Debug.Log("Occlusion limit reached. Stopping trace.");
                    break;
                }
            }
            else if (debug)
            {
                UnityEngine.Debug.LogFormat(
                    "Asset: {0} -- Portal '{1}' OPEN -- distanceWeight: {2:F2}, depthWeight: {3:F2}, occlusion: {4:F2}",
                    asset, portal.name, distanceWeight, depthWeight, weightedOcclusion);
            }

            step++;
        }

        totalOcclusion = math.clamp(totalOcclusion, 0f, maxOcclusion);
        return reusablePath;
    }




    public class PriorityQueue<T>
    {
        private struct Node
        {
            public T Item;
            public float Priority;
        }

        private const int Capacity = 1024;
        private readonly Node[] _heap = new Node[Capacity];
        private int _count;

        private readonly Dictionary<T, int> _itemToIndex = new Dictionary<T, int>(Capacity);

        public int Count => _count;

        public void Enqueue(T item, float priority)
        {
            if (_itemToIndex.TryGetValue(item, out int index))
            {
                if (priority >= _heap[index].Priority)
                    return;

                // Only update if strictly lower
                _heap[index].Priority = priority;
                SiftUp(index);
                return;
            }

            // Fast-path check before writing
            if (_count >= Capacity)
                throw new InvalidOperationException("PriorityQueue is full.");

            _heap[_count] = new Node { Item = item, Priority = priority };
            _itemToIndex[item] = _count;
            SiftUp(_count++);
        }


        public void DequeueWithPriority(out T item, out float priority)
        {
            if (_count == 0)
                throw new InvalidOperationException("PriorityQueue is empty.");

            Node rootNode = _heap[0];
            item = rootNode.Item;
            priority = rootNode.Priority;

            _itemToIndex.Remove(item);
            _count--;

            if (_count > 0)
            {
                Node lastNode = _heap[_count];
                _heap[0] = lastNode;
                _itemToIndex[lastNode.Item] = 0;
                SiftDown(0);
            }
        }

        public bool Contains(T item) => _itemToIndex.ContainsKey(item);

        public void Clear()
        {
            for (int i = 0; i < _count; i++)
            {
                _heap[i] = default;
            }
            _count = 0;
            _itemToIndex.Clear();
        }

        public float GetPriority(T item)
        {
            return _itemToIndex.TryGetValue(item, out int index)
                ? _heap[index].Priority
                : float.MaxValue;
        }

        private void SiftUp(int index)
        {
            Node node = _heap[index];

            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                Node parentNode = _heap[parent];
                if (parentNode.Priority <= node.Priority)
                    break;

                _heap[index] = parentNode;
                _itemToIndex[parentNode.Item] = index;
                index = parent;
            }

            _heap[index] = node;
            _itemToIndex[node.Item] = index;
        }

        private void SiftDown(int index)
        {
            Node node = _heap[index];
            int half = _count >> 1;

            while (index < half)
            {
                int left = (index << 1) + 1;
                int right = left + 1;
                int smallest = left;
                Node smallestNode = _heap[left];

                if (right < _count && _heap[right].Priority < smallestNode.Priority)
                {
                    smallest = right;
                    smallestNode = _heap[right];
                }

                if (smallestNode.Priority >= node.Priority)
                    break;

                _heap[index] = smallestNode;
                _itemToIndex[smallestNode.Item] = index;
                index = smallest;
            }

            _heap[index] = node;
            _itemToIndex[node.Item] = index;
        }
    }


}

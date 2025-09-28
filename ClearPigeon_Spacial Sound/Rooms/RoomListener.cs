using System.Collections.Generic;
using UnityEngine;
using ClearPigeon.Audio;
using System;

[RequireComponent(typeof(Collider))]
public class RoomListener : MonoBehaviour
{
    public OnRoomEvent OnRoomEnter;
    public OnRoomEvent OnRoomActive;
    public OnRoomEvent OnRoomExit;

    public Action<Audio_Source, Room> OnSourceEnter;
    public Action<Audio_Source> OnSourceActive;
    public Action<Audio_Source> OnSourceExit;

    private Dictionary<int, Audio_SoundListener> listenersInRoom = new();
    private Dictionary<int, Audio_Source> sourcesInRoom = new();

    private Collider roomCollider;
    private Room cachedRoom;

    // Reusable collections for GC optimization
    private readonly HashSet<int> tempListenerInsideIds = new();
    private readonly HashSet<int> tempSourceInsideIds = new();
    private readonly List<int> tempListenerExited = new();
    private readonly List<int> tempSourceExited = new();
    private readonly List<Audio_Source> tempNewSources = new();

    private void OnEnable()
    {
        roomCollider = GetComponent<Collider>();
        cachedRoom = GetComponent<Room>();
    }

    public void OnPropagateUpdate(bool instant)
    {
        var activeListeners = Audio_SoundManager.Instance.activeListeners;
        var allSources = Audio_SoundManager.Instance.sourceList;

        if (activeListeners.Count > 0) RefreshListeners(activeListeners);
        if (allSources.Count > 0) RefreshSources(allSources);
    }

    public void RefreshListeners(List<Audio_SoundListener> allListeners)
    {
        if (roomCollider == null) return;

        tempListenerInsideIds.Clear();
        tempListenerExited.Clear();

        var bounds = roomCollider.bounds;

        // Detect inside listeners
        foreach (var listener in allListeners)
        {
            int id = listener.GetInstanceID(); 
            Vector3 pos = listener.transform.position;
            bool isInside = bounds.min.x <= pos.x && pos.x <= bounds.max.x &&
                            bounds.min.y <= pos.y && pos.y <= bounds.max.y &&
                            bounds.min.z <= pos.z && pos.z <= bounds.max.z;

            if (!isInside) continue;

            tempListenerInsideIds.Add(id);

            if (listenersInRoom.TryAdd(id, listener))
            {
                OnRoomEnter?.Invoke(listener);
            }

            OnRoomActive?.Invoke(listener);
        }

        // Detect exited listeners
        foreach (var kvp in listenersInRoom)
        {
            if (!tempListenerInsideIds.Contains(kvp.Key))
                tempListenerExited.Add(kvp.Key);
        }

        foreach (var id in tempListenerExited)
        {
            if (listenersInRoom.TryGetValue(id, out var listener))
                OnRoomExit?.Invoke(listener);

            listenersInRoom.Remove(id);
        }
    }

    public void RefreshSources(List<Audio_Source> allSources)
    {
        if (roomCollider == null) return;

        tempSourceInsideIds.Clear();
        tempSourceExited.Clear();
        tempNewSources.Clear();

        var bounds = roomCollider.bounds;

        // Detect inside sources
        foreach (var source in allSources)
        {
          int id = source.GetInstanceID(); 
            Vector3 pos = source.transform.position;
            bool isInside = bounds.min.x <= pos.x && pos.x <= bounds.max.x &&
                            bounds.min.y <= pos.y && pos.y <= bounds.max.y &&
                            bounds.min.z <= pos.z && pos.z <= bounds.max.z;

            if (!isInside) continue;

            tempSourceInsideIds.Add(id);

            if (sourcesInRoom.TryAdd(id, source))
                tempNewSources.Add(source);
        }

        // Detect exited sources
        foreach (var kvp in sourcesInRoom)
        {
            if (!tempSourceInsideIds.Contains(kvp.Key))
                tempSourceExited.Add(kvp.Key);
        }

        // Fire exit events
        foreach (var id in tempSourceExited)
        {
            if (sourcesInRoom.TryGetValue(id, out var source))
                OnSourceExit?.Invoke(source);

            sourcesInRoom.Remove(id);
        }

        // Fire enter events
        foreach (var source in tempNewSources)
            OnSourceEnter?.Invoke(source, cachedRoom);

        // Fire active events
        foreach (var id in tempSourceInsideIds)
        {
            if (sourcesInRoom.TryGetValue(id, out var source))
                OnSourceActive?.Invoke(source);
        }
    }
}

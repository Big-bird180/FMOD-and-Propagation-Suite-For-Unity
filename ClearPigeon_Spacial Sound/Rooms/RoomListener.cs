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

    private Dictionary<int, Audio_SoundListener> listenersInRoom = new Dictionary<int, Audio_SoundListener>();
    private Dictionary<int, Audio_Source> sourcesInRoom = new Dictionary<int, Audio_Source>();

    private Collider roomCollider;
    private Room cachedRoom;

    // Reusable collections for GC optimization
    private HashSet<int> tempListenerInsideIds = new HashSet<int>();
    private HashSet<int> tempSourceInsideIds = new HashSet<int>();
    private List<int> tempExitedIds = new List<int>();
    private List<Audio_Source> tempNewEntries = new List<Audio_Source>();

    private void OnEnable()
    {
        roomCollider = GetComponent<Collider>();
        cachedRoom = GetComponent<Room>();
    }

    public void OnPropagateUpdate(bool instant)
    {
        RefreshListeners(Audio_SoundManager.Instance.activeListeners);
        RefreshSources(Audio_SoundManager.Instance.sourceList);
    }

    public void RefreshListeners(List<Audio_SoundListener> allListeners)
    {
        if (roomCollider == null)
            return;

        tempListenerInsideIds.Clear();
        tempExitedIds.Clear();

        var bounds = roomCollider.bounds;

        foreach (var listener in allListeners)
        {
            int id = listener.GetInstanceID();
            bool isInside = bounds.Contains(listener.transform.position);

            if (isInside)
            {
                tempListenerInsideIds.Add(id);

                if (!listenersInRoom.ContainsKey(id))
                {
                    listenersInRoom.Add(id, listener);
                    OnRoomEnter?.Invoke(listener);
                }

                OnRoomActive?.Invoke(listener);
            }
        }

        foreach (var kvp in listenersInRoom)
        {
            if (!tempListenerInsideIds.Contains(kvp.Key))
            {
                OnRoomExit?.Invoke(kvp.Value);
                tempExitedIds.Add(kvp.Key);
            }
        }

        foreach (var id in tempExitedIds)
        {
            listenersInRoom.Remove(id);
        }
    }

    public void RefreshSources(List<Audio_Source> allSources)
    {
        if (roomCollider == null)
            return;

        tempSourceInsideIds.Clear();
        tempExitedIds.Clear();
        tempNewEntries.Clear();

        var bounds = roomCollider.bounds;

        // Detect sources inside
        foreach (var source in allSources)
        {
            int id = source.GetInstanceID();
            bool isInside = bounds.Contains(source.transform.position);

            if (isInside)
            {
                tempSourceInsideIds.Add(id);

                if (!sourcesInRoom.ContainsKey(id))
                {
                    sourcesInRoom.Add(id, source);
                    tempNewEntries.Add(source);
                }
            }
        }

        // Find exited sources
        foreach (var kvp in sourcesInRoom)
        {
            if (!tempSourceInsideIds.Contains(kvp.Key))
            {
                tempExitedIds.Add(kvp.Key);
            }
        }

        // Fire exit events first
        foreach (var id in tempExitedIds)
        {
            if (sourcesInRoom.TryGetValue(id, out var source))
            {
                OnSourceExit?.Invoke(source);
                sourcesInRoom.Remove(id);
            }
        }

        // Fire enter events for new sources
        foreach (var source in tempNewEntries)
        {
            OnSourceEnter?.Invoke(source, cachedRoom);
        }

        // Fire active events for all inside sources
        foreach (var id in tempSourceInsideIds)
        {
            if (sourcesInRoom.TryGetValue(id, out var source))
            {
                OnSourceActive?.Invoke(source);
            }
        }
    }
}

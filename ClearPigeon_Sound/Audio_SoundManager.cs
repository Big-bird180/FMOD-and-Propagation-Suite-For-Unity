using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ClearPigeon.Audio;
using FMOD.Studio;
using FMODUnity;
public class Audio_SoundManager : MonoBehaviour
{
    public static Audio_SoundManager Instance { get; private set; }

    public static event Action<bool> OnPropagationUpdate;
    public static event Action<float> OnUpdate;

    public readonly List<Audio_Source> sourceList = new();
    public readonly List<Audio_SoundListener> activeListeners = new();
    private readonly HashSet<Audio_SoundListener> _sharedListenerCache = new(); // reused to avoid GC

    public Transform Player => _player ??= GameObject.Find("Player")?.transform;
    public Transform _player;
    public Room playerRoom;
    private Room oldPlayerRoom;

    //Tick system
    public float tickRate = 0.1f; // 10Hz
    public static float TickDeltaTime { get; private set; }

    //Reverb
    private float _nextTickTime;
    public EventReference reverbReference;
    public EventInstance reverbInstance;
    


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        TickDeltaTime = tickRate;
        _nextTickTime = Time.time + 0.01f;
        reverbInstance = FMODUnity.RuntimeManager.CreateInstance(reverbReference);
        reverbInstance.start();

        StartCoroutine(WaitForRoomManager());
    }

    private IEnumerator WaitForRoomManager()
    {
        yield return new WaitUntil(() => RoomManager.Instance != null && RoomManager.Instance._roomListener != null);

        foreach (var room in RoomManager.Instance._roomListener)
            OnPropagationUpdate += room.OnPropagateUpdate;
    }

    private void Update()
    {
        float time = Time.time;
        if (time < _nextTickTime) return;

        float tickStart = Time.realtimeSinceStartup;
        PerformTick();
        float tickDuration = Time.realtimeSinceStartup - tickStart;

        _nextTickTime += tickRate;
        if (_nextTickTime < time) // catch-up prevention
            _nextTickTime = time + tickRate;

        if (tickDuration > tickRate)
            Debug.LogWarning($"[Audio_SoundManager] Tick took too long! Duration: {tickDuration:F4}s");
    }

    private void PerformTick()
    {
        if (sourceList.Count == 0) return;

        // Prune invalid sources before triggering update
        for (int i = sourceList.Count - 1; i >= 0; i--)
        {
            var source = sourceList[i];
            if (source == null)
            {
                sourceList.RemoveAt(i);
                OnUpdate -= source.OnUpdate; // Prevent dangling delegate reference
                continue;
            }
        }
        playerRoom = RoomManager.Instance.GetCurrentRoom(Player.transform.position);
        UpdateReverb();

        OnPropagationUpdate?.Invoke(true);
        OnUpdate?.Invoke(TickDeltaTime);
    }

    private void UpdateReverb()
    {
        if (oldPlayerRoom != playerRoom)
        {
            reverbInstance.setParameterByName("Reverb", (int)playerRoom.config.reverbType);
            oldPlayerRoom = playerRoom;
            Debug.Log($"UPDATED REVERB ZONE!! + {playerRoom.config.reverbType}");
        }
    }
    public void AddSoundSource(Audio_Source source)
    {
        if (!sourceList.Contains(source))
        {
            sourceList.Add(source);

            if (source.GetSoundAsset().spacialSound && !source.propagation.global)
                OnPropagationUpdate += source.OnPropagateUpdate;

            OnUpdate += source.OnUpdate;
        }
    }

    public void RemoveSoundSource(Audio_Source source)
    {
        if (sourceList.Remove(source))
        {
            if (source.GetSoundAsset().spacialSound && !source.propagation.global)
                OnPropagationUpdate -= source.OnPropagateUpdate;

            OnUpdate -= source.OnUpdate;
        }
    }

    public void AddSoundListener(Audio_SoundListener listener)
    {
        if (listener != null && !activeListeners.Contains(listener))
            activeListeners.Add(listener);
    }

    public void RemoveSoundListener(Audio_SoundListener listener)
    {
        activeListeners.Remove(listener);
    }

    /// <summary>
    /// Tries to collect all listeners in the given room. Shared cache is reused to reduce allocations.
    /// </summary>
    public int TryGetListeners(Room room, HashSet<Audio_SoundListener> outListeners)
    {
        outListeners.Clear();

        int count = 0;
        foreach (var listener in activeListeners)
        {
            if (listener != null && listener.room == room && outListeners.Add(listener))
                count++;
        }

        return count;
    }
}

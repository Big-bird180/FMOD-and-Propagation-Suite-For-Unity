using System;
using System.Collections.Generic;
using UnityEngine;
using ClearPigeon.Audio;

public class Audio_SoundManager : MonoBehaviour
{
    public static Audio_SoundManager Instance { get; private set; }

    public static event Action<bool> OnPropagationUpdate;
    public static event Action<float> OnUpdate;

    public List<Audio_Source> sourceList = new();
    public List<Audio_SoundListener> activeListeners = new();
    public Transform player => GameObject.Find("Player").transform;


    private int SoundSourceCount => sourceList.Count;
    private int SoundListenerCount => activeListeners.Count;


    private float nextTickTime;
    public float tickRate = 1f / 10f;
    public static float TickDeltaTime { get; private set; }

    private void OnEnable()
    {
        TickDeltaTime = tickRate;
        nextTickTime = Time.time + 0.01f;
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);

        }
        else Instance = this;
        foreach (var room in RoomManager.Instance._roomListener)
        {
            OnPropagationUpdate += room.OnPropagateUpdate;
        }

      

        //InitializeReverbDSPs();

    }

    private void Update()
    {
       

        if (Time.time < nextTickTime) return;

        OnTick();
        nextTickTime += tickRate;

        if (nextTickTime < Time.time)
        {
            nextTickTime = Time.time + tickRate;
        }
    }

    private void OnTick()
    {
        float tickStart = Time.realtimeSinceStartup;
        UpdatePropagation();

        float tickDuration = Time.realtimeSinceStartup - tickStart;
        if (tickDuration > tickRate)
        {
            UnityEngine.Debug.LogWarning($"Tick took too long! Duration: {tickDuration:F4}s");
        }
    }


    public static void UpdatePropagation()
    {
        var manager = Audio_SoundManager.Instance;
        if (manager.SoundSourceCount <= 0) return;

        OnPropagationUpdate?.Invoke(true);
        OnUpdate?.Invoke(TickDeltaTime);
    }

    public void AddSoundSource(Audio_Source source)
    {
        if (source.propagation.enabled)
        {
            OnPropagationUpdate += source.OnPropagateUpdate;
            OnUpdate += source.OnUpdate;
        }

        sourceList.Add(source);
    }

    public void RemoveSoundSource(Audio_Source source)
    {
        if (source.propagation.enabled)
        {
            OnPropagationUpdate -= source.OnPropagateUpdate;
            OnUpdate -= source.OnUpdate;
        }

        sourceList.Remove(source);
    }

    public void AddSoundListener(Audio_SoundListener listener)
    {
        if (!activeListeners.Contains(listener))
            activeListeners.Add(listener);
    }

    public void RemoveSoundListener(Audio_SoundListener listener)
    {
        activeListeners.Remove(listener);
    }

    public int TryGetListeners(Room room, HashSet<Audio_SoundListener> listeners)
    {
        listeners.Clear();
        if (SoundListenerCount <= 0) return 0;

        int count = 0;
        foreach (var listener in activeListeners)
        {
            if (listener.room == room && listeners.Add(listener))
                count++;
        }

        return count;
    }




}

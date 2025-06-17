using ClearPigeon.Audio;

using FMOD.Studio;
using FMOD;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Collections;
using UnityEditor;

public class Audio_Source : MonoBehaviour
{


    #region Nested Types
    [Serializable]
    public class PropagationSettings
    {
        public bool enabled = true;
        public bool repropagate;
        public bool global;
        public int portalLimit = -1;
        public int portalOffset = 0;
        public Room overrideRoom; 
    }

    // Key = tuple of (sourceRoom, listenerRoom)
    private Dictionary<(Room, Room), CachedPath> sharedPathCache = new Dictionary<(Room, Room), CachedPath>();

    private class CachedPath
    {
        public List<Room> path;           // The cached path (room list)
        public float occlusion;           // Cached occlusion value for that path
        public Vector3 lastListenerPosition;  // Last listener position used for path calc
        public Room lastListenerRoom;          // Last listener room
        public Room lastSourceRoom;            // Last source room
    }


    #endregion

    #region Fields
    [SerializeField] private Audio_ReverbConfig currentReverbConfig;
   private Audio_ReverbConfig nextReverbConfig;
    private readonly Dictionary<EventInstance, DSPHandle> instanceDSPHandles = new();

    [Header("Configuration")]
    public bool debug;
    public PropagationSettings propagation;

    [Header("Audio Properties")]
    [SerializeField] private Audio_PlayPresets _playPreset;
    [SerializeField] private Audio_Asset _asset;
    [SerializeField] private GameObject _owner;
    [Range(0, 1)] public float volume = 1f;
   
    [SerializeField] private float transitionDuration = 45f;

    // Internal FMOD instance
    private EventInstance _soundInstance;

    // Cached data
    private Room soundRoom;
    private PropagationManager propManager;
    private readonly Dictionary<Audio_SoundListener, Room> listenerRoomCache = new();
    private readonly HashSet<Room> visitedRooms = new();
    private readonly List<Audio_SoundListener> foundListeners = new();
    private float previousVolume = 0f;
    // State
    private Vector3 lastPosition;
    private float transitionTimer;
    private float _targetOcclusion;
    private float appliedOcclusion;

    private bool isPlaying;
    private bool HasPropagated = false;
    private bool isPlayAwake = false;

    #endregion


    #region Unity Callbacks

    private void Start()
    {
        if (_asset == null)
        {
            UnityEngine.Debug.LogError("Audio_Source: _asset is null! Assign an Audio_Asset.", this);
            return;
        }

        OnInitialize();
    }

    private void FixedUpdate()
    {
        if (!_asset.spacialSound || !_soundInstance.isValid() || !isPlaying) return;
        if (debug)
        {
            ChangeReverbPreset(_soundInstance, currentReverbConfig);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (_asset == null) return;

        Vector3 position = transform.position;
      
        // Draw RangeMin
        Gizmos.color = new Color(1f, 0f, 0f, 0.5f); // Semi-transparent red
        Gizmos.DrawWireSphere(position, _asset.RangeMin);


        // Draw RangeMax
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(position, _asset.RangeMax);

    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.DrawIcon(this.transform.position, "/C.P.-Room-Propagation-FMOD-/Gizmos/Audio_Icon.png", true);
    }


#endif

    #endregion

    #region Initialization and Destruction

    protected void OnInitialize()
    {
        propManager = new PropagationManager();
        SetOwner(_owner ?? gameObject);

        if (propagation.overrideRoom != null)
        {
            soundRoom = propagation.overrideRoom;
        }

        SetSoundAsset(_asset);
        SetSoundPreset(_playPreset);

        Audio_SoundManager.Instance.AddSoundSource(this);

        if(_asset.spacialSound)  RegisterWithRooms(); 
    }

    private void OnDestroy()
    {
        Stop();
        Reset();

        if (Audio_SoundManager.Instance != null)
        {
            Audio_SoundManager.Instance.RemoveSoundSource(this);
        }

        if (_asset.spacialSound) UnregisterFromRooms();
    }

    public void Reset()
    {
        CleanupDSP();
        UnityEngine.Debug.Log("PropagationManager reset!");
    }

    public void SetSoundAsset(Audio_Asset asset)
    {
        _asset = asset;
        _soundInstance = FMODUnity.RuntimeManager.CreateInstance(_asset.eventReference);

        _soundInstance.setProperty(EVENT_PROPERTY.MINIMUM_DISTANCE, _asset.RangeMin);
        _soundInstance.setProperty(EVENT_PROPERTY.MAXIMUM_DISTANCE, _asset.RangeMax);
    }

    private void SetSoundPreset(Audio_PlayPresets preset)
    {
        _playPreset = preset;

        if (_playPreset == Audio_PlayPresets.Awake)
        {
            isPlayAwake = true;
            Play();
        }
    }

    #endregion

    #region Playback

    public void StartPlay() => Play();
    public void Play()
    {
        if (_asset == null || !_soundInstance.isValid()) return;
        isPlaying = true;

        _soundInstance.start();
        UnityEngine.Debug.Log("Sound Started!");
        SetVolume(volume);

        if (currentReverbConfig != null)
            ChangeReverbPreset(_soundInstance, currentReverbConfig);

        HasPropagated = false;
    }


    public void SetOwner(GameObject owner)
    {
        _owner = owner;
    }

    public void Stop()
    {
        if (!isPlaying || !_soundInstance.isValid())
            return;

        CleanupDSP();

        _soundInstance.stop(STOP_MODE.IMMEDIATE);
        _soundInstance.release();

        isPlaying = false;
        UnityEngine.Debug.Log("Sound Stopped and Released!");

        Reset();
    }

    public void SetVolume(float newVolume)
    {
        volume = Mathf.Clamp01(newVolume);
        if (_soundInstance.isValid())
        {
            _soundInstance.setVolume(volume);
        }
    }
    #endregion

    #region Reverb DSP

    public void ChangeReverbPreset(EventInstance instance, Audio_ReverbConfig newConfig, float fadeDuration = 0.45f)
    {
        if (newConfig == null) return;

        if (!instanceDSPHandles.TryGetValue(instance, out var handle) || !handle.Dsp.hasHandle())
        {
            var dsp = CreateDSPForInstance(instance);
            if (!dsp.hasHandle())
            {
                UnityEngine.Debug.LogWarning("Failed to create or attach DSP.");
                return;
            }

            handle = new DSPHandle
            {
                Dsp = dsp,
                CurrentConfig = newConfig,
                FadeCoroutine = null
            };

            instanceDSPHandles[instance] = handle;
            ApplyReverbPresetToDSP(handle.Dsp, newConfig);
            handle.Dsp.setBypass(false); // Ensure DSP is active
            return;
        }

        if (handle.CurrentConfig == newConfig)
            return;

        // Ensure DSP is active in case it was bypassed earlier
        handle.Dsp.setBypass(false);

        // Stop any existing fade coroutine
        if (handle.FadeCoroutine != null)
            StopCoroutine(handle.FadeCoroutine);

        handle.FadeCoroutine = StartCoroutine(FadeReverbPreset(handle, newConfig, fadeDuration));
    }

    private DSP CreateDSPForInstance(EventInstance instance)
    {
        if (FMODUnity.RuntimeManager.CoreSystem.createDSPByType(DSP_TYPE.SFXREVERB, out var dsp) != FMOD.RESULT.OK)
            return default;

        if (instance.getChannelGroup(out var channelGroup) != FMOD.RESULT.OK || !channelGroup.hasHandle())
        {
            dsp.release(); // Don't leak
            return default;
        }

        if (channelGroup.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD, dsp) != FMOD.RESULT.OK)
        {
            dsp.release();
            return default;
        }

        return dsp;
    }

    private IEnumerator FadeReverbPreset(DSPHandle handle, Audio_ReverbConfig newConfig, float duration)
    {
        var dsp = handle.Dsp;
        var fromConfig = handle.CurrentConfig;
        float elapsed = 0f;

        if (fromConfig == null || newConfig == null || fromConfig.Values.Length != 13 || newConfig.Values.Length != 13)
            yield break;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            float eased = EaseInOut(t);

            for (int i = 0; i < fromConfig.Values.Length; i++)
            {
                float lerpedValue = Mathf.Lerp(fromConfig.Values[i], newConfig.Values[i], eased);
                dsp.setParameterFloat(i, lerpedValue);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        ApplyReverbPresetToDSP(dsp, newConfig);
        handle.CurrentConfig = newConfig;
        handle.FadeCoroutine = null;
    }

    private void ApplyReverbPresetToDSP(DSP dsp, Audio_ReverbConfig config)
    {
        if (config == null || config.Values == null || config.Values.Length != 13)
        {
            UnityEngine.Debug.LogWarning("Invalid reverb config.");
            return;
        }

        for (int i = 0; i < config.Values.Length; i++)
        {
            dsp.setParameterFloat(i, config.Values[i]);
        }
    }

   
    /// Bypasses the DSP instead of destroying it. This allows quick reactivation later.

    public void CleanupDSP()
    {
        if (!instanceDSPHandles.TryGetValue(_soundInstance, out var handle)) return;

        // Simply bypass the DSP instead of removing/releasing it
        if (handle.Dsp.hasHandle())
        {
            handle.Dsp.setBypass(true);
        }

        instanceDSPHandles.Remove(_soundInstance);
    }
    #endregion

    #region Propagation

    public void OnUpdate(float deltaTime)
    {
        if (this == null || Audio_SoundManager.Instance == null) return;

        if (!propagation.enabled || !_asset.spacialSound)
            return;

        if (isPlaying && !HasPropagated)
        {
            Propagate();
            HasPropagated = true;
            return;
        }

        if (propagation.repropagate || _asset.loop)
        {
            HasPropagated = false;
            Repropagate();
        }

        if (_asset.spacialSound)
        {
            Vector3 currentPosition = Vector3.zero;
            if (transform != null) currentPosition = transform.position;

            if (currentPosition != lastPosition)
            {
                _soundInstance.set3DAttributes(new FMOD.ATTRIBUTES_3D
                {
                    position = FMODUnity.RuntimeUtils.ToFMODVector(currentPosition),
                    velocity = FMODUnity.RuntimeUtils.ToFMODVector(Vector3.zero),
                    forward = FMODUnity.RuntimeUtils.ToFMODVector(transform.forward),
                    up = FMODUnity.RuntimeUtils.ToFMODVector(Vector3.up)
                });

                lastPosition = currentPosition;
            }
        }

        if(previousVolume != volume)
        {
            SetVolume(volume);
            previousVolume = volume;
        }
        ApplyOcclusion(_targetOcclusion);
    }


    public void OnPropagateUpdate(bool instant)
    {
        if (!propagation.enabled || !isPlaying || HasPropagated) return;
       
        Propagate();
        HasPropagated = true;
       
        
    }

    public void Propagate()
    {
        if (propagation.enabled)
        {
            OnPropagate();
        }
    }

    public void Repropagate()
    {
        if (propagation.enabled && propagation.repropagate && isPlaying && _asset != null)
        {
            OnPropagate();
        }
    }

    private void OnPropagate()
    {
        if (this == null || !gameObject.activeInHierarchy || HasPropagated)
            return;

        HasPropagated = true; // Set early to prevent reentrancy.

        visitedRooms.Clear();

        var soundManager = Audio_SoundManager.Instance;
        var player = soundManager.player;
        var roomManager = RoomManager.Instance;
        var roomDict = roomManager.dictionary;
        var sourceRoom = GetSoundRoom();
        var portalLimit = propagation.portalLimit;
        var portalOffset = propagation.portalOffset;

        Vector3 playerPos = player.position;
        Room playerRoom = roomManager.GetCurrentRoom(playerPos);

        if (playerRoom != null)
        {
            float occlusionToPlayer = 0f;

            propManager.FindPath(
                sourceRoom,
                playerRoom,
                roomDict,
                _asset,
                this,
                playerPos,
                out occlusionToPlayer,
                debug,
                portalLimit,
                portalOffset
            );

            visitedRooms.Add(playerRoom);
            ApplyOcclusion(occlusionToPlayer * 0.5f);
        }

        SeekListeners();

        int listenerCount = foundListeners.Count;
        for (int i = 0; i < listenerCount; i++)
        {
            var listener = foundListeners[i];

            if (listener == null || listener.room == null)
                continue;

            Room listenerRoom = listener.room;
            Vector3 listenerPos = listener.transform.position;

            var key = (sourceRoom, listenerRoom);
            CachedPath cached;
            bool pathNeedsUpdate = true;

            if (sharedPathCache.TryGetValue(key, out cached))
            {
                if (listenerRoom == cached.lastListenerRoom && sourceRoom == cached.lastSourceRoom)
                {
                    float distSqr = (listenerPos - cached.lastListenerPosition).sqrMagnitude;
                    pathNeedsUpdate = distSqr > 0.25f; // 0.5 * 0.5
                }
            }

            if (pathNeedsUpdate)
            {
                var path = propManager.FindPath(
                    sourceRoom,
                    listenerRoom,
                    roomDict,
                    _asset,
                    this,
                    listenerPos,
                    out float occlusion,
                    debug,
                    portalLimit,
                    portalOffset
                );

                cached = new CachedPath
                {
                    path = path,
                    occlusion = occlusion,
                    lastListenerPosition = listenerPos,
                    lastListenerRoom = listenerRoom,
                    lastSourceRoom = sourceRoom
                };

                sharedPathCache[key] = cached;
            }

            ApplySound(listener, cached.occlusion);
        }
    }


    private void ApplyOcclusion(float targetValue)
    {
        if (_asset == null || !_soundInstance.isValid()) return;

        _targetOcclusion = targetValue;

        if (isPlayAwake)
        {
            transitionTimer = 0f;
            appliedOcclusion = _targetOcclusion;
            _soundInstance.setParameterByName("SFX_Occlusion", appliedOcclusion);
            isPlayAwake = false;
        }
        else
        {
            transitionTimer += Time.deltaTime;
            float t = Mathf.Clamp01(transitionTimer / transitionDuration);
            float easedT = EaseInOut(t);
            appliedOcclusion = Mathf.Lerp(appliedOcclusion, _targetOcclusion, easedT);
            _soundInstance.setParameterByName("SFX_Occlusion", appliedOcclusion);
        }
    }
    #endregion

    #region AI & Helpers
    public static void ApplySound(Audio_SoundListener listener, float occlusionValue)
    {
        listener.OnHear(occlusionValue);
    }

    private void SeekListeners()
    {
        foundListeners.Clear();
        Vector3 sourcePos = transform.position;
        float rangeSqr = _asset.RangeMax * _asset.RangeMax;

        foreach (var listener in Audio_SoundManager.Instance.activeListeners)
        {
            if ((listener.transform.position - sourcePos).sqrMagnitude <= rangeSqr)
            {
                foundListeners.Add(listener);
            }
        }
    }

    private float EaseInOut(float t) => t * t * (3f - 2f * t); // Smoothstep

    public Room GetSoundRoom()
    {
        if (this == null)
            return null;

        if (propagation.overrideRoom != null)
            return propagation.overrideRoom;

        Room currentRoom = RoomManager.Instance.GetCurrentRoom(transform.position);
        if (currentRoom != null)
            soundRoom = currentRoom;

        return soundRoom;
    }

    public void SetParameter(string parameterName, float value)
    {
        if (!_soundInstance.isValid())
        {
            UnityEngine.Debug.LogWarning($"Audio_Source: Sound instance is invalid.", this);
            return;
        }

        var result = _soundInstance.setParameterByName(parameterName, value);

        if (result != FMOD.RESULT.OK)
        {
            UnityEngine.Debug.LogError($"Audio_Source: Failed to set parameter '{parameterName}' to {value}. FMOD Result: {result}", this);
        }
    }

    public void SetParameterLabel(string parameterName, string label)
    {
        if (!_soundInstance.isValid())
        {
            UnityEngine.Debug.LogWarning($"Audio_Source: Sound instance is invalid.", this);
            return;
        }

        var result = _soundInstance.setParameterByNameWithLabel(parameterName, label);

        if (result != FMOD.RESULT.OK)
        {
            UnityEngine.Debug.LogError($"Audio_Source: Failed to set labeled parameter '{parameterName}' to label '{label}'. FMOD Result: {result}", this);
        }
    }

    private void RegisterWithRooms()
    {
        var manager = RoomManager.Instance;
       

        var listeners = manager._roomListener;
       

        foreach (var roomListener in listeners)
        {
            if (roomListener == null)
            {
                UnityEngine.Debug.LogWarning("Found null RoomListener in RoomManager._roomListener.");
                continue;
            }
<<<<<<< Updated upstream

=======
        
>>>>>>> Stashed changes
            roomListener.OnSourceEnter += OnRoomEnter;
            roomListener.OnSourceExit += OnRoomExit;
        }
    }

    private void UnregisterFromRooms()
    {
        foreach (var roomListener in RoomManager.Instance?._roomListener)
        {
            roomListener.OnSourceEnter -= OnRoomEnter;
            roomListener.OnSourceExit -= OnRoomExit;
        }
    }

    private void OnRoomEnter(Audio_Source source, Room room)
    {
        if (source == this)
            soundRoom = room;

        var newReverbConfig = soundRoom?.config.roomReverbPreset;

        // Only change if the new config is different
        if (newReverbConfig != null && newReverbConfig != currentReverbConfig)
        {
            ChangeReverbPreset(_soundInstance, newReverbConfig);
            currentReverbConfig = newReverbConfig; // Update tracker
        }

        if (debug)
            UnityEngine.Debug.Log($"{name} entered room: {soundRoom?.name}");
    }


    private void OnRoomExit(Audio_Source source)
    {
        if (source == this)
        {
            if (RoomManager.Instance.GetCurrentRoom(transform.position) != soundRoom)
            {
                if (debug) UnityEngine.Debug.Log($"{name} exited room: {soundRoom?.name}");
                soundRoom = null;
            }
        }
    }

    #endregion

    #region Saving and Loading

    // Add save/load methods here if needed in future

    #endregion
}


public class DSPHandle
{
    public DSP Dsp;
    public Audio_ReverbConfig CurrentConfig;
    public Coroutine FadeCoroutine;
}

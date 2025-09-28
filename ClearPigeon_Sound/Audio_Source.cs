using ClearPigeon.Audio;

using FMOD.Studio;
using FMOD;
using UnityEngine;
using System;
using System.Collections.Generic;
using static Unity.VisualScripting.Member;

public class Audio_Source : MonoBehaviour
{
    #region Nested Types
    [Serializable]
    public class PropagationSettings
    {
        public bool repropagate;
        [Tooltip("whether the sound source responds to/enters the propagation loop")] public bool global;
        [Tooltip("The maximum number of portals to traverse until maximum occlusion")]
        public int portalLimit = -1;
        [Tooltip("how many portals traversed until effects get applied")]
        public int portalOffset = 0;
        public Room overrideRoom;
    }

    // Key = tuple of (sourceRoom, listenerRoom)
    private Dictionary<RoomPair, CachedPath> sharedPathCache;

    private class CachedPath
    {
        public List<Room> path;           // The cached path (room list)
        public float occlusion;           // Cached occlusion value for that path
        public Vector3 lastListenerPosition;  // Last listener position used for path calc
        public Room lastListenerRoom;          // Last listener room
        public Room lastSourceRoom;            // Last source room
    }

    private class RoomPairComparer : IEqualityComparer<RoomPair>
    {
        public bool Equals(RoomPair a, RoomPair b) =>
            ReferenceEquals(a.source, b.source) && ReferenceEquals(a.listener, b.listener);

        public int GetHashCode(RoomPair pair) =>
            (pair.source?.GetHashCode() ?? 0) * 397 ^ (pair.listener?.GetHashCode() ?? 0);
    }
    #endregion

    #region Fields
    private Audio_Asset _lastPlayedAsset;
    [Header("Audio Properties")]
    [SerializeField] private Audio_PlayPresets _playPreset;
    [SerializeField] private Audio_Asset _asset;
    [SerializeField] private GameObject _owner;
    [Range(0, 1)] public float volume = 1f;

    [SerializeField, Range(0, 300)] private float transitionDuration = 135;
    private float _inverseTransitionDuration;

    [Header("Configuration")]
    public bool debug;
    public PropagationSettings propagation;

    // Internal FMOD instance
    private EventInstance _soundInstance;

    // Cached data
    private Room soundRoom;
    private PropagationManager propManager;
    private readonly HashSet<Room> visitedRooms = new();
    private readonly List<Audio_SoundListener> foundListeners = new();
    private float previousVolume = 0f;

    // Cached references for performance
    private Transform cachedPlayerTransform;
    private RoomManager cachedRoomManager;

    // State
    private Vector3 lastPosition;
    private float _targetOcclusion;
    private float appliedOcclusion;

    private bool isPlaying;
    private bool HasPropagated = false;
    private bool isPlayAwake = false;
    private DSP _delayDSP;
    private DSP _lowpassDSP;
    private ChannelGroup _channelGroup;

    private float _targetDelay;
    private float _targetLowpass;
    private float _transitionDuration;
    private float _transitionTimer;
    private bool _transitionActive;

    private float _currentDelay;
    private float _currentLowpass;


    #endregion

    #region Unity Callbacks

    private void Start()
    {
        if (_asset == null)
        {
            UnityEngine.Debug.LogError("Audio_Source: _asset is null! Assign an Audio_Asset.", this);
            return;
        }
        _inverseTransitionDuration = 1f / Mathf.Max(transitionDuration, 0.0001f);

        // Initialize sharedPathCache with comparer
        sharedPathCache = new Dictionary<RoomPair, CachedPath>(new RoomPairComparer());

        OnInitialize();
    }


    private void FixedUpdate()
    {
        if (!_asset.spacialSound || !_soundInstance.isValid() || !isPlaying) return;

        // Possibly update sound propagation here if needed, or handled externally.
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

        cachedPlayerTransform = FindObjectOfType<ClearPigeon.Entities.Ent_PlayerController>()?.transform;
        cachedRoomManager = RoomManager.Instance;

        if (propagation.overrideRoom != null)
        {
            soundRoom = propagation.overrideRoom;
        }

        SetSoundAsset(_asset);
        SetSoundPreset(_playPreset);

        Audio_SoundManager.Instance.AddSoundSource(this);

        if (!propagation.global)
            RegisterWithRooms();
    }

    private void OnDestroy()
    {
        Stop();
        Reset();

        if (Audio_SoundManager.Instance != null)
            Audio_SoundManager.Instance.RemoveSoundSource(this);

        if (!propagation.global)
            UnregisterFromRooms();
    }

    public void Reset()
    {
        if (debug) UnityEngine.Debug.Log("PropagationManager reset!");
    }

    public void SetSoundAsset(Audio_Asset asset)
    {
        _asset = asset;

        _soundInstance = FMODUnity.RuntimeManager.CreateInstance(_asset.eventReference);

        _soundInstance.setProperty(EVENT_PROPERTY.MINIMUM_DISTANCE, _asset.RangeMin);
        _soundInstance.setProperty(EVENT_PROPERTY.MAXIMUM_DISTANCE, _asset.RangeMax);
    }

    public Audio_Asset GetSoundAsset()
    {
        return _asset;
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

    public void Play()
    {
        if (_asset == null || !_soundInstance.isValid()) return;
        isPlaying = true;

        if (_asset == null && _soundInstance.isValid()) _soundInstance.release();
//Stop();
//Reset();

_soundInstance.start();
        if (debug) UnityEngine.Debug.Log("Sound Started!");
        SetVolume(volume);

        if (_asset.spacialSound) HasPropagated = false;
    }

    public void PlaySoundOnce(Audio_Asset newAsset)
    {
        if (this == null || newAsset == null)
            return;

        if (_lastPlayedAsset == newAsset && isPlaying)
            return;

        Stop();
        SetSoundAsset(newAsset);
        Play();

        _lastPlayedAsset = newAsset;
    }

    public void PlayFromPosition(Audio_Asset asset, int startTimeMs)
    {
        if (asset == null) return;

        Stop();

        SetSoundAsset(asset); // <- Must happen first to create a fresh instance

        // Now apply timeline offset BEFORE starting
        if (_soundInstance.isValid())
            _soundInstance.setTimelinePosition(startTimeMs);

        Play();
        isPlaying = true;

        if (debug) UnityEngine.Debug.Log($"Started {asset.name} from {startTimeMs}ms");
        _lastPlayedAsset = asset;

    }




    public void SetOwner(GameObject owner)
    {
        _owner = owner;
    }

    public void Stop()
    {
        if (!isPlaying || !_soundInstance.isValid())
            return;

        _soundInstance.stop(STOP_MODE.IMMEDIATE);
        _soundInstance.release();

        isPlaying = false;
        if (debug) UnityEngine.Debug.Log("Sound Stopped and Released!");

        Reset();
    }

    public void SetVolume(float newVolume)
    {
        volume = Mathf.Clamp01(newVolume);
        if (_soundInstance.isValid())
            _soundInstance.setVolume(volume);
    }
    #endregion

    #region DSPs

    #endregion

    #region Propagation

    public void OnUpdate(float deltaTime)
    {
        if (Audio_SoundManager.Instance == null) return;
        if (this == null || gameObject == null) return;

        if (isPlaying && !HasPropagated)
        {
            Propagate();
            HasPropagated = true;
            return;
        }

        if ((propagation.repropagate || _asset.loop) && _asset.spacialSound)
        {
            HasPropagated = false;
            Repropagate();
        }

        Vector3 currentPosition = Vector3.zero;
        if (this != null) currentPosition = transform.position;


        if (currentPosition != lastPosition && _soundInstance.isValid())
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

        if (previousVolume != volume)
        {
            SetVolume(volume);
            previousVolume = volume;
        }
        ApplyOcclusion(_targetOcclusion);
    }


    public void OnPropagateUpdate(bool instant)
    {
        if (!_asset.spacialSound || !isPlaying || HasPropagated) return;

        Propagate();
        HasPropagated = true;
    }

    public void Propagate()
    {
        if (_asset.spacialSound)
        {
            OnPropagate();
        }
    }

    public void Repropagate()
    {
        if (_asset.spacialSound && propagation.repropagate && isPlaying && _asset != null)
        {
            OnPropagate();
        }
    }

    private void OnPropagate()
    {
        if (!gameObject.activeInHierarchy || HasPropagated)
            return;

        HasPropagated = true; // Set early to prevent reentrancy.
        visitedRooms.Clear();

        if (cachedPlayerTransform == null || cachedRoomManager == null)
            return;

        var roomDict = cachedRoomManager.dictionary;
        var sourceRoom = GetSoundRoom();
        var portalLimit = propagation.portalLimit;
        var portalOffset = propagation.portalOffset;

        Vector3 playerPos = cachedPlayerTransform.position;
        Room playerRoom = Audio_SoundManager.Instance.playerRoom;

        SeekListeners();

        bool playerInRange = playerRoom != null &&
                             (playerPos - transform.position).sqrMagnitude <= _asset.RangeMax * _asset.RangeMax;

        if (!playerInRange && foundListeners.Count == 0)
        {
            ApplyOcclusion(1f); // Full occlusion or default value
            return;
        }

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

        int listenerCount = foundListeners.Count;
        for (int i = 0; i < listenerCount; i++)
        {
            var listener = foundListeners[i];

            if (listener == null || listener.room == null)
                continue;

            Room listenerRoom = listener.room;
            Vector3 listenerPos = listener.transform.position;

            var key = new RoomPair(sourceRoom, listenerRoom);
            if (!sharedPathCache.TryGetValue(key, out CachedPath cached))
            {
                cached = null;
            }

            bool pathNeedsUpdate = true;

            if (cached != null)
            {
                if (listenerRoom == cached.lastListenerRoom && sourceRoom == cached.lastSourceRoom)
                {
                    if (!listener.transform.hasChanged && !transform.hasChanged)
                    {
                        pathNeedsUpdate = false;
                        listener.transform.hasChanged = false;
                        transform.hasChanged = false;
                    }
                    else
                    {
                        float distSqr = (listenerPos - cached.lastListenerPosition).sqrMagnitude;
                        pathNeedsUpdate = distSqr > 0.25f;
                    }
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
            // Immediate set on first play
            appliedOcclusion = _targetOcclusion;
            _soundInstance.setParameterByName("SFX_Occlusion", appliedOcclusion);
            isPlayAwake = false;
            return;
        }

        // Smoothly approach target occlusion using MoveTowards for stable convergence
        float step = Audio_SoundManager.Instance.tickRate / Mathf.Max(transitionDuration, 0.0001f);
        if (!Mathf.Approximately(appliedOcclusion, _targetOcclusion))
        {
            appliedOcclusion = Mathf.MoveTowards(appliedOcclusion, _targetOcclusion, step);
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

    public Room GetSoundRoom()
    {
        if (propagation.overrideRoom != null)
            return propagation.overrideRoom;

        Room currentRoom = cachedRoomManager?.GetCurrentRoom(transform.position);
        if (currentRoom != null)
            soundRoom = currentRoom;

        return soundRoom;
    }

    public void SetParameter(string parameterName, float value)
    {
        if (!_soundInstance.isValid())
        {
            if (debug) UnityEngine.Debug.LogWarning($"Audio_Source: Sound instance is invalid.", this);
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
            if (debug) UnityEngine.Debug.LogWarning($"Audio_Source: Sound instance is invalid.", this);
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
        var listeners = cachedRoomManager?._roomListener;
        if (listeners == null) return;

        foreach (var roomListener in listeners)
        {
            if (roomListener == null)
            {
                if (debug) UnityEngine.Debug.LogWarning("Found null RoomListener in RoomManager._roomListener.");
                continue;
            }

            roomListener.OnSourceEnter += OnRoomEnter;
            roomListener.OnSourceExit += OnRoomExit;
        }
    }

    private void UnregisterFromRooms()
    {
        var listeners = cachedRoomManager?._roomListener;
        if (listeners == null) return;

        foreach (var roomListener in listeners)
        {
            roomListener.OnSourceEnter -= OnRoomEnter;
            roomListener.OnSourceExit -= OnRoomExit;
        }
    }

    private void OnRoomEnter(Audio_Source source, Room room)
    {
        if (source == this)
            soundRoom = room;

        if (debug)
            UnityEngine.Debug.Log($"{name} entered room: {soundRoom?.name}");
    }

    private void OnRoomExit(Audio_Source source)
    {
        if (source == this)
        {
            if (cachedRoomManager?.GetCurrentRoom(transform.position) != soundRoom)
            {
                if (debug) UnityEngine.Debug.Log($"{name} exited room: {soundRoom?.name}");
                soundRoom = null;
            }
        }
    }
    #endregion

    #region Saving and Loading

    public struct RoomPair : IEquatable<RoomPair>
    {
        public Room source;
        public Room listener;

        public RoomPair(Room source, Room listener)
        {
            this.source = source;
            this.listener = listener;
        }

        public bool Equals(RoomPair other) =>
            ReferenceEquals(source, other.source) && ReferenceEquals(listener, other.listener);

        public override int GetHashCode() =>
            (source?.GetHashCode() ?? 0) * 397 ^ (listener?.GetHashCode() ?? 0);
    }

    #endregion
}

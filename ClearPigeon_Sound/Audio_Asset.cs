
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.Serialization;
[CreateAssetMenu(fileName = "SoundAsset", menuName = "FMOD Sound Propagation/Sound/Sound Asset", order = 40)]
public class Audio_Asset : ScriptableObject
{
    public bool loop = false;
    public bool spacialSound;
    [Header("AI")]
    [SerializeField]
    [FormerlySerializedAs("type")]
    [FormerlySerializedAs("AILevel")]
    private AI_SoundType AILevelMax;
    [SerializeField] private AI_SoundType AILevelMin;
    [SerializeField] private float aiRangeMax = 1f;
    [Header("Settings")]
    public EventReference eventReference;
    public EventInstance eventInstance;

    [SerializeField][Range(0f, 1f)] private float volume = 1f;
      [Range(0f, 2f)] public float volumeMod = 1f;

    [SerializeField] private float playerVolumeMin;

    [SerializeField] private float pitchMin = 1f;

    [SerializeField] private float pitchMax = 1f;

    [SerializeField] private float rangeMin = -1f;

    [SerializeField] private float rangeMax = -1f;

    [SerializeField][Min(0f)] private float attenuation = 1f;
    [SerializeField][Range(0f, 1f)] private float blockability = 1f;


    public float PitchMin => pitchMin;

    public float PitchMax => pitchMax;

    public float RangeMin => rangeMin;

    public float RangeMax => rangeMax;

    public float AIRangeMax => aiRangeMax;

    public float Volume => volume;

    public float PlayerVolumeMin => playerVolumeMin;

    public float Attenuation => attenuation;

    public float Blockability => blockability;

  //  public Audio_SoundInfo audio_SoundInfo;
}

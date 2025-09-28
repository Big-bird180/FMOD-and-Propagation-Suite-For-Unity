using FMOD;
using UnityEditor;
using UnityEngine;


[CreateAssetMenu(fileName = "Reverb Config", menuName = "FMOD Sound Propagation/Sound/Reverb Config")]

public class Audio_ReverbConfig : ScriptableObject
{
    public float delayMS = 150f;
    public float lowpassHz = 3000f;
    public float reverbMix = 1f;
    public float transitionTime = 0.5f; // fade duration
}


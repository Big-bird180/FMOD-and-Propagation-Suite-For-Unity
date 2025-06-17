using FMOD;
using UnityEditor;
using UnityEngine;


[CreateAssetMenu(fileName = "Reverb Config", menuName = "FMOD Sound Propagation/Sound/Reverb Config")]

public class Audio_ReverbConfig : ScriptableObject
{
    [Tooltip("Reverb parameter values matching DSP_SFXREVERB order")]
    public float[] Values = new float[13];

    // ✅ Indexer for convenient access
    public float this[int index]
    {
        get => Values[index];
        set => Values[index] = value;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(Audio_ReverbConfig))]
public class ReverbPresetConfigEditor : Editor
{
    private static readonly string[] ParamNames = new string[]
    {
        "Decay Time",
        "Early Delay",
        "Late Delay",
        "HF Reference",
        "HF Decay Ratio",
        "Diffusion",
        "Density",
        "Low Shelf Frequency",
        "Low Shelf Gain",
        "High Cut",
        "Early-Late Mix",
        "Wet Level",
        "Dry Level"
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var targetPreset = (Audio_ReverbConfig)target;

        EditorGUILayout.LabelField("Reverb Parameters", EditorStyles.boldLabel);

        for (int i = 0; i < ParamNames.Length; i++)
        {
            targetPreset.Values[i] = EditorGUILayout.FloatField(ParamNames[i], targetPreset.Values[i]);
        }

        EditorUtility.SetDirty(target);
        serializedObject.ApplyModifiedProperties();
    }
}

#endif
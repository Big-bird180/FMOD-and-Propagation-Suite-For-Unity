using UnityEngine;
using FMODUnity;

[CreateAssetMenu(fileName = "Room Config", menuName = "FMOD Sound Propagation/Sound/Room Config")]
public class RoomConfig : ScriptableObject
{
    public enum ReverbType { inside = 1, outside = 2, basement = 3 };
    public ReverbType reverbType;
}



using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "Room Config", menuName = "FMOD Sound Propagation/Sound/Room Config")]
public class RoomConfig : ScriptableObject
{
    public Audio_ReverbConfig roomReverbPreset;
}

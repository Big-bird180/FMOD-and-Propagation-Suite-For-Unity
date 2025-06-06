using System.Collections;
using System.Collections.Generic;
using ClearPigeon.Audio;
using UnityEngine;

public class Audio_SoundListener : MonoBehaviour
{
    public Room room => RoomManager.Instance.GetCurrentRoom(this.transform.position);

    public void OnHear(float value)
    {
        Debug.Log($"{transform.name} inside of room: {room.name} got sound value: {value}");
    }

    void Awake()
    {
        Audio_SoundManager.Instance.AddSoundListener(this);
    }

     void OnDestroy()
     {
        Audio_SoundManager.Instance.RemoveSoundListener(this);
     }
}
using System.Collections;
using System.Collections.Generic;
using ClearPigeon.Audio;
using UnityEngine;
using ClearPigeon.Managers;
public class Audio_SoundListener : MonoBehaviour
{
    public Room room => RoomManager.Instance.GetCurrentRoom(this.transform.position);

    public void OnHear(float value)
    {
        Debug.Log($"{transform.name} inside of room: {room.name} got sound value: {value}");
    }

    void Awake()
    {
        Global_GameManager.Instance.SoundManager.AddSoundListener(this);
    }

     void OnDestroy()
     {
         Global_GameManager.Instance.SoundManager.RemoveSoundListener(this);
     }
}
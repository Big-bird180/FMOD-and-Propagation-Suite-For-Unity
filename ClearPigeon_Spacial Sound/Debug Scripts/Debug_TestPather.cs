using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ClearPigeon.Audio;
using FMODUnity;
using System.Threading.Tasks;

public class Debug_TestPather : MonoBehaviour
{
    public RoomManager roomManager;
   
    public Room startRoom;
    public Room targetRoom;
    public EventReference sound;
    async void Start()
    {
        await WaitForLoadComplete(); // Ensure world is loaded before proceeding

    

       // float value; 

        //PropagationManager.FindPath(startRoom, targetRoom, roomManager.dictionary, out value);
        //_Source.StartSound(sound, transform.position, true, 0);
        //Debug.Log("Pathfinding complete!" + value);
    }

    // Wait until the RoomManager is ready
    private async Task WaitForLoadComplete()
    {
        while (roomManager == null || roomManager.dictionary == null)
        {

            try
            {
                Debug.Log("Waiting for RoomManager to be initialized...");
                await Task.Delay(100); // Wait a bit before checking again
            }
            catch
            {
                return;
            }

        }

        Debug.Log("RoomManager is ready!");
    }

}

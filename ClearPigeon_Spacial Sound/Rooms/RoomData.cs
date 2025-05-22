using System.Collections;
using System.Collections.Generic;
using UnityEngine;



namespace ClearPigeon.Audio
{
    [System.Serializable]
    public struct RoomData
    {
        public Room room;
        public List<RoomPortal> portals;
        public List<Room> neighbors;
        public List<Path_VentExit> ventExits;
        public List<RoomCell> cells;
        public RoomData(Room room)
        {
            this.room = room;
            cells = new List<RoomCell>();
            portals = new List<RoomPortal>();
            neighbors = new List<Room>();
            ventExits = new List<Path_VentExit>();
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
namespace ClearPigeon.Audio
{
    public class Room : MonoBehaviour
    {
        [Header("Room Properties")]
        public RoomConfig config;
        public string roomName;
        public string roomID;

        [Header("Room Settings")]
        public bool isInitialized = false;
        public bool isGlobal;
        public bool isActive;
        [SerializeField]
        private int priority;

        public List<RoomCell> cellList;

        private List<RoomPortal> portalList;
        public RoomData roomData;
        public RoomListener roomListener;

        public void SetConfig(RoomConfig config)
        {
            this.config = config;
        }

        public string GenerateID()
        {
            roomID = Guid.NewGuid().ToString();
            return roomID;
        }

        public void Build(RoomData data)
        {

            if (!roomListener)
            {
                roomListener = gameObject.AddComponent<RoomListener>();
            }

            else
            {
                roomListener = GetComponent<RoomListener>();
            }

            cellList = data.cells;
            portalList = data.portals;
            int count = portalList.Count;

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)  // Start from i + 1 to avoid redundant checks
                {
                    // Assuming you want to connect portals if they belong to the same room
                    if (portalList[i].room1 == portalList[j].room1 || portalList[i].room2 == portalList[j].room2)
                    {
                        portalList[i].AddConnectedPortal(portalList[j]);
                        portalList[j].AddConnectedPortal(portalList[i]);  // Make sure to connect both ways
                    }
                }
            }


        }

        public int CompareTo(Room other)
        {

            if (priority < other.priority)
            {
                return -1;
            }
            if (priority > other.priority)
            {
                return 1;
            }
            return 0;
        }

        public bool ContainsPoint(Vector3 point)
        {
            int count = cellList.Count;
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    if (cellList[i].IsPointWithin(point, false))
                    {
                        return true;
                    }
                }
            }
            return false;
        }


    }
}

using System;
using System.Collections.Generic;
using FMOD;
using UnityEngine;


namespace ClearPigeon.Audio
{
    [System.Serializable]
    public class RoomPortalData
    {
        public string portalID;
        public Vector3 position;
        public float weight;
        public bool IsOpen;
    }


    public class RoomPortal : MonoBehaviour, ISaveable
    {
        public string _portalID;
        public Room room1;
        public Room room2;

        public Dictionary<RoomPortal, float> portalDistanceDict = new Dictionary<RoomPortal, float>();

        [Header("Portal Properties")]
        public float _weight = 0;
        public SoundBlocker _soundBlocker;
        public bool built = false;



        public RoomPortalData SaveData()
        {
            RoomPortalData dataToSave = new RoomPortalData();
            SoundBlocker sounndBlocker = GetComponent<SoundBlocker>();

            dataToSave.position = transform.position;
            dataToSave.weight = _weight;

            dataToSave.IsOpen = sounndBlocker != null ? sounndBlocker._isOpen : true;
            dataToSave.portalID = _portalID;

            return dataToSave;

        }


        public void LoadData(RoomPortalData data)
        {
            transform.position = data.position;
            _weight = data.weight;

            SoundBlocker soundBlocker = GetComponent<SoundBlocker>();
            if (soundBlocker != null)
            {
                soundBlocker._isOpen = data.IsOpen;
            }
            UnityEngine.Debug.Log($"Loaded portal {data.portalID} at {data.position}");
        }


        public void SetRoom(int direction, Room room)
        {
            if (direction > 0)
            {
                room1 = room;
            }
            if (direction < 0)
            {
                room2 = room;
            }
        }

        public void AddConnectedPortal(RoomPortal targetPortal)
        {
            targetPortal.portalDistanceDict[targetPortal] = Vector3.Distance(this.transform.position, targetPortal.transform.position);
        }
        [ExecuteInEditMode]
        public string GenerateUniqueID()
        {
            string uniqueID = "portal" + "_" + System.Guid.NewGuid().ToString(); // Use GUID for uniqueness
            UnityEngine.Debug.Log(uniqueID);
            _portalID = uniqueID; // Assign the generated ID
            return uniqueID;
        }

        [ExecuteInEditMode]
        void OnValidate()
        {
            if (_portalID == String.Empty) GenerateUniqueID();
        }

        // This method will draw the debug icon in the Scene view
        private void OnDrawGizmos()
        {
            if (_soundBlocker != null)
            {
                // If you prefer an icon, you can use Gizmos.DrawIcon instead
                if (_soundBlocker.IsOpen)
                {
                    Gizmos.DrawIcon(transform.position, "open_icon.png", true, Color.cyan);

                }
                else
                {
                    Gizmos.DrawIcon(transform.position, "closed_icon.png", true, Color.cyan);
                }
            }
            else if (_soundBlocker == null)
            {
                Gizmos.DrawIcon(transform.position, "portal_icon.png", true, Color.cyan);
            }

        }


    }
}

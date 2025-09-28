using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using ClearPigeon.Helpers;
using System;
using System.Linq;
using Unity.VisualScripting;


//Thank you chatGPT for helping me understand the Dictionary Based graphing system
namespace ClearPigeon.Audio
{
    [Serializable]
    public class RoomManager : MonoBehaviour
    {
        [SerializeField] private Material _roomCellMaterial;
        [SerializeField] private Material _portalMaterial;


        [SerializeField] private int _roomLayerIndex = 11;
        [SerializeField] private int _defaultLayerIndex = 0; // Default layer to reset to

        public Dictionary<Room, RoomData> dictionary = new Dictionary<Room, RoomData>();
        private Dictionary<Room, Bounds> roomBoundsCache = new Dictionary<Room, Bounds>();
        public Room _globalRoom;

        [Header("Debug")]
        [SerializeField] bool _debug;
        [SerializeField] private Material _fallBackMaterial;
        [SerializeField] private RoomConfig _defaultConfig;

        [SerializeField] public List<Room> _rooms = new List<Room>();
        public List<RoomListener> _roomListener = new List<RoomListener>();
        [SerializeField] public Dictionary<int, Room> _roomInstanceID = new Dictionary<int, Room>();
        [SerializeField] List<RoomPortal> PortalList = new List<RoomPortal>();

        [SerializeField] public Dictionary<string, Room> roomIdDict;


        public bool initialized = false;

        private Scene activeScene;

        // Game manager instance
        public static RoomManager Instance;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;

            }
            else if (Instance != this)
            {
                Destroy(gameObject); // Destroy duplicate instances
            }
            StartGraphBuild();

        }

        public void StartGraphBuild()
        {
            if (!initialized) InitializeRooms();
        }

        public void InitializeRooms()
        {
            activeScene = SceneManager.GetActiveScene();

            roomIdDict = new Dictionary<string, Room>();
            _rooms = SceneHelper.GetAllComponentsInScene<Room>(activeScene);
            PortalList = SceneHelper.GetAllComponentsInScene<RoomPortal>(activeScene);


            List<RoomData> roomDataList = new List<RoomData>(); // Store room data
            List<Room> initializedRooms = new List<Room>(); // Store initialized rooms

            // Initialize all other rooms
            for (int i = 0; i < _rooms.Count; i++)
            {
                // Generate ID if it's empty
                if (string.IsNullOrEmpty(_rooms[i].roomID))
                    _rooms[i].roomID = _rooms[i].GenerateID();

                // Initialize room properties
                _rooms[i].gameObject.layer = _roomLayerIndex;
                if (!_rooms[i].GetComponent<RoomListener>())
                {
                    _rooms[i].AddComponent<RoomListener>();
                }

                // Cache the bounds of the room
                Collider roomCollider = _rooms[i].GetComponent<Collider>();
                if (roomCollider != null)
                {
                    roomBoundsCache[_rooms[i]] = roomCollider.bounds;
                }

                // Set room config if not already set
                if (_rooms[i].config == null)
                {
                    _rooms[i].SetConfig(_defaultConfig);
                }


                // Create room data for this room
                RoomData roomData = new RoomData(_rooms[i]);

                // Store roomData in the dictionary
                dictionary[_rooms[i]] = roomData;

                // Log if a specific room is being initialized
                if (_debug)
                {
                    Debug.Log($"Room {_rooms[i].roomName} initialized and added to dictionary");
                }
                // If room name is empty, print a warning
                if (string.IsNullOrEmpty(_rooms[i].roomName))
                {
                    if (_debug) Debug.LogError("___ENTER ROOM NAME, config = " + _rooms[i].config + ", YOU'LL THANK ME LATER___");
                }
                else
                {
                    _rooms[i].gameObject.name = _rooms[i].roomName;
                }

                // Add room data to the list and mark as initialized
                if (roomIdDict.ContainsKey(_rooms[i].roomID))
                {
                    roomIdDict[_rooms[i].roomID] = _rooms[i];
                    Debug.Log("Room has already been registered to active room graph with id: '" + _rooms[i].roomID + "'.");
                }
                else
                {
                    roomIdDict.Add(_rooms[i].roomID, _rooms[i]);
                }


                roomDataList.Add(roomData);
                _roomInstanceID.Add(_rooms[i].gameObject.GetInstanceID(), _rooms[i]);
                initializedRooms.Add(_rooms[i]);
                _rooms[i].isInitialized = true;
            }

            // Store the initialized rooms list and other data
            _rooms = initializedRooms;
            // Initialize global room if not present
            Room globalRoom = InitializeGlobalRoom();
            _globalRoom = globalRoom;
            _roomListener = SceneHelper.GetAllComponentsInScene<RoomListener>(activeScene);
            // Generate connections and update the graph with portals
            GenerateConnections(globalRoom, dictionary, PortalList, _rooms, roomDataList);
        }

        private Room InitializeGlobalRoom()
        {
            // Look for an existing global room
            Room globalRoom = _rooms.Find(room => room.isGlobal);

            // If no global room is found, create a new one
            if (globalRoom == null)
            {
                GameObject globalRoomObject = new GameObject("Global");
                globalRoom = globalRoomObject.AddComponent<Room>();
                globalRoom.isGlobal = true;
                globalRoom.isInitialized = true;

                // Create room data for the global room
                RoomData globalRoomData = new RoomData(globalRoom);
                dictionary[globalRoom] = globalRoomData; // Store global room in dictionary

                // Log the creation of the global room
                if (_debug) Debug.Log($"Global room created: {globalRoom.name}");
            }
            else
            {
                // If a global room exists, ensure it's initialized
                globalRoom.isInitialized = true;
                if (_debug) Debug.Log($"Existing global room: {globalRoom.name}");
            }

            return globalRoom;
        }

        void GenerateConnections(Room globalRoom, Dictionary<Room, RoomData> roomGraph, List<RoomPortal> portalList, List<Room> roomList, List<RoomData> roomData)
        {
            // This dictionary is used to temporarily store room data, but we should add room data to the global dictionary (dictionary)
            foreach (Room room in roomList)
            {
                if (!room.gameObject.activeSelf)
                {
                    continue;
                }

                // Ensure each room has its configuration and add to dictionary
                if (!room.config)
                {
                    room.SetConfig(_defaultConfig);
                }

                // Initialize the RoomData for the current room if not already present in the list
                RoomData roomInfo = roomData.Find(r => r.room == room);
                if (roomInfo.room == null)
                {
                    roomInfo = new RoomData(room); // Create new RoomData if not found
                    roomData.Add(roomInfo); // Add to list
                }


                // Add cells for each collider in the room
                Collider[] componentsInChildren = room.GetComponentsInChildren<Collider>();
                foreach (Collider collider in componentsInChildren)
                {
                    RoomCell roomCell = collider.gameObject.AddComponent<RoomCell>();
                    roomCell.SetOwner(room);
                    roomInfo.cells.Add(roomCell); // Add the RoomCell to the room's RoomData instance
                }

                if (room.isGlobal)
                {
                    _globalRoom = room; // Update global room reference
                }
            }

            // Ensure global room is present in the dictionary
            if (globalRoom != null && !dictionary.ContainsKey(globalRoom))
            {
                globalRoom.SetConfig(_defaultConfig);
                dictionary[globalRoom] = new RoomData(globalRoom); // Store global room in the dictionary
            }

            // Process portals and establish connections between rooms
            if (portalList.Count > 0)
            {
                if (_debug) Debug.Log($"Portal list contains {portalList.Count} portals.");

                foreach (RoomPortal portal in portalList)
                {
                    if (!portal.enabled)
                    {
                        Debug.Log($"Portal {portal.name} is inactive and skipped.");
                        continue;
                    }

                    // Check if room1 and room2 are assigned
                    bool room1Check = portal.room1 != null && dictionary.ContainsKey(portal.room1);
                    bool room2Check = portal.room2 != null && dictionary.ContainsKey(portal.room2);

                    // Only attempt to assign global room if both room1 and room2 are invalid

                    if (!room1Check && !room2Check)
                    {
                        if (_debug) Debug.LogError($"Both rooms are invalid for portal {portal.name}. Skipping portal.");
                        continue;
                    }

                    // If room1 is invalid but room2 is valid, assign global room to room1
                    if (!room1Check && room2Check)
                    {
                        if (_debug) Debug.LogError($"Room1 for portal {portal.name} is invalid. Setting to global room.");
                        portal.SetRoom(1, globalRoom); // Assign global room to room1
                    }

                    // If room2 is invalid but room1 is valid, assign global room to room2
                    if (!room2Check && room1Check)
                    {
                        if (_debug) Debug.LogError($"Room2 for portal {portal.name} is invalid. Setting to global room.");
                        portal.SetRoom(-1, globalRoom); // Assign global room to room2
                    }


                    room1Check = portal.room1 != null && dictionary.ContainsKey(portal.room1);
                    room2Check = portal.room2 != null && dictionary.ContainsKey(portal.room2);

                    if (!room1Check && !room2Check)
                    {
                        if (_debug) Debug.LogError($"Both rooms are invalid for portal {portal.name}. Assigning both to global room.");
                        portal.SetRoom(1, globalRoom);
                        portal.SetRoom(-1, globalRoom);
                    }
                    else if (!room1Check)
                    {
                        if (_debug) Debug.LogWarning($"Room1 for portal {portal.name} is missing. Assigning to global room.");
                        portal.SetRoom(1, globalRoom);
                    }
                    else if (!room2Check)
                    {
                        if (_debug) Debug.LogWarning($"Room2 for portal {portal.name} is missing. Assigning to global room.");
                        portal.SetRoom(-1, globalRoom);
                    }


                    RoomData roomData1 = dictionary[portal.room1];
                    RoomData roomData2 = dictionary[portal.room2];


                    RoomCell portalCell1 = FindPortalCell(portal, roomData1, roomData2);
                    RoomCell portalCell2 = FindPortalCell(portal, roomData2, roomData1);

                    if (portalCell1 != null)
                    {
                        portalCell1.SetPortal(portal);
                        if (_debug) Debug.Log($"Portal {portal.name} assigned to RoomCell in {portal.room1.name}");
                    }

                    if (portalCell2 != null)
                    {
                        portalCell2.SetPortal(portal);
                        if (_debug) Debug.Log($"Portal {portal.name} assigned to RoomCell in {portal.room2.name}");
                    }
                    // Assign the portal to both rooms' RoomData
                    if (!roomData1.portals.Contains(portal))
                    {
                        roomData1.portals.Add(portal);
                        if (_debug) Debug.Log($"Portal {portal.name} added to RoomData of {roomData1.room.name}");
                    }

                    if (!roomData2.portals.Contains(portal))
                    {
                        roomData2.portals.Add(portal);
                        if (_debug)
                            Debug.Log($"Portal {portal.name} added to RoomData of {roomData2.room.name}");
                    }


                    if (!roomData1.neighbors.Contains(portal.room2))
                    {
                        roomData1.neighbors.Add(portal.room2);
                        if (_debug)
                            Debug.Log($"Added {portal.room2.name} as neighbor of {portal.room1.name}");
                    }

                    if (!roomData2.neighbors.Contains(portal.room1))
                    {
                        roomData2.neighbors.Add(portal.room1);
                        if (_debug)
                            Debug.Log($"Added {portal.room1.name} as neighbor of {portal.room2.name}");
                    }


                    // Explicitly assign rooms to the portal
                    portal.room1 = roomData1.room; // Assign room1 to portal
                    portal.room2 = roomData2.room; // Assign room2 to portal
                                                   // Log the connection between rooms
                    if (_debug) Debug.Log($"Portal {portal.name} connects {portal.room1?.name} <-> {portal.room2?.name}");

                    if (portal.room1 == null || portal.room2 == null)
                    {
                        Debug.LogError($"Portal {portal.name} has an invalid connection!");
                    }

                    if (!dictionary.TryGetValue(portal.room1, out roomData1) || !dictionary.TryGetValue(portal.room2, out roomData2))
                    {
                        if (_debug) Debug.LogError($"Portal {portal.name} connects missing rooms.");
                        continue;
                    }
                    // Log the connection between rooms
                    Debug.Log(roomData1.room.gameObject.name + " and " + roomData2.room.gameObject.name);

                    portal.built = true;
                }
            }
            else
            {
                Debug.Log("No portals to process.");
            }

            for (int i = 0; i < roomList.Count; i++)
            {
                Room room1 = roomList[i];
                RoomData data1 = dictionary[room1];

                for (int j = i + 1; j < roomList.Count; j++)
                {
                    Room room2 = roomList[j];
                    RoomData data2 = dictionary[room2];

                    // Skip if rooms are not in the same layer
                    bool crossLayerAllowed = false;

                    // Allow cross-layer only if there is a portal connecting them
                    if (portalList.Any(p => (p.room1 == room1 && p.room2 == room2) || (p.room1 == room2 && p.room2 == room1)))
                    {
                        crossLayerAllowed = true;
                    }

                    if (room1.roomLayer != room2.roomLayer && !crossLayerAllowed)
                        continue;

                    // Check bounds intersection
                    Collider c1 = room1.GetComponent<Collider>();
                    Collider c2 = room2.GetComponent<Collider>();
                    if (c1 != null && c2 != null && c1.bounds.Intersects(c2.bounds))
                    {
                        if (!data1.neighbors.Contains(room2)) data1.neighbors.Add(room2);
                        if (!data2.neighbors.Contains(room1)) data2.neighbors.Add(room1);
                    }
                }
            }

            Debug.Log($"Total rooms initialized: {dictionary.Count}");

            foreach (var entry in dictionary)
            {
                if (_debug)
                    Debug.Log($"Room: {entry.Key.name}, Neighbors: {entry.Value.neighbors.Count}, Portals: {entry.Value.portals.Count}");
            }

            initialized = true;
        }


        // **New method to undo initialization**
        public void UndoInitialization()
        {
            activeScene = SceneManager.GetActiveScene();
            List<Room> rooms = SceneHelper.GetAllComponentsInScene<Room>(activeScene);
            List<RoomPortal> portals = SceneHelper.GetAllComponentsInScene<RoomPortal>(activeScene);
            foreach (Room room in rooms)
            {
                // **Only reset uninitialized rooms or those marked as initialized**
                if (!room.isInitialized) continue;

                room.gameObject.layer = _defaultLayerIndex;

                Renderer renderer = room.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = _roomCellMaterial; // Reset material to default (or set to another default material)
                }
                // **Only reset room config if it was set to the default**
                if (room.config == _defaultConfig)
                {
                    room.config = null;
                }

                RoomCell[] roomCell = room.GetComponents<RoomCell>();
                foreach (RoomCell roomcells in roomCell)
                {
                    if (roomCell != null)
                    {
                        DestroyImmediate(roomcells);
                    }
                }

                // **Mark room as uninitialized**
                room.isInitialized = false;
            }

            foreach (RoomPortal portal in portals)
            {
                if (portal.room1 == _globalRoom) portal.room1 = null;
                if (portal.room2 == _globalRoom) portal.room2 = null;
                portal.GetComponent<Renderer>().material = _portalMaterial;
                portal.built = false;
            }
            dictionary.Clear();

            _globalRoom = null;
            _rooms.Clear();
            PortalList.Clear();
            initialized = false; // Reset manager state
        }

        private static RoomCell FindPortalCell(RoomPortal portal, RoomData r1, RoomData r2)
        {
            RoomCell result = null;
            Room room = r1.room;
            Room room2 = r2.room;
            RoomCell cell = null;
            RoomCell cell2 = null;
            Vector3 position = portal.transform.position;
            bool flag = false;
            bool flag2 = false;
            if ((bool)room)
            {
                flag = r1.cells.TryGetCellFromPoint(position, debug: false, out cell);
            }
            if ((bool)room2)
            {
                flag2 = r2.cells.TryGetCellFromPoint(position, debug: false, out cell2);
            }
            if (flag && flag2)
            {
                int num = string.Compare(room.name, room2.name, StringComparison.Ordinal);

                if (num <= 0)
                {
                    result = cell;
                }
                else if (num > 0)
                {
                    result = cell2;
                }
            }
            else if (flag && !flag2)
            {
                result = cell;
            }
            else if (!flag && flag2)
            {
                result = cell2;
            }
            return result;
        }


        public Room GetCurrentRoom(Vector3 position)
        {
            Room bestRoom = null;

            foreach (var roomEntry in roomBoundsCache)
            {
                if (roomEntry.Value.Contains(position))
                {
                    Room candidate = roomEntry.Key;

                    // If no best room yet, or this one has higher priority → take it
                    if (bestRoom == null || candidate.roomLayer > bestRoom.roomLayer)
                    {
                        bestRoom = candidate;
                    }
                }
            }


            // Fallback to global room if nothing found
            return bestRoom ?? _globalRoom;
        }


        public bool TryGetRoomByInstanceId(int instanceId, out Room room)
        {
            if (initialized)
            {
                return _roomInstanceID.TryGetValue(instanceId, out room);
            }
            room = null;
            return false;
        }


        public bool TryGetRoomDataByID(Room roomID, out RoomData roomData)
        {
            // If not found, try to get from the roomManager's dictionary
            if (dictionary.TryGetValue(roomID, out roomData))
            {
                return true;
            }
            else return false;
        }
        public bool TryGetRoomById(string instanceId, out Room room)
        {
            return roomIdDict.TryGetValue(instanceId, out room);

        }


    }


}
 using UnityEngine;
 using UnityEditor;
 using ClearPigeon.Audio;
 
 [CustomEditor(typeof(RoomManager))]
    public class RoomManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector(); // Draw the default inspector UI

            RoomManager roomManager = (RoomManager)target;

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,              // Set font size to make it larger
                fontStyle = FontStyle.Bold, // Make the font bold
                alignment = TextAnchor.MiddleCenter, // Center the text
                normal = { textColor = Color.white } // Set text color
            };

            // Use GUILayout to display the title with custom style
            GUILayout.Label("TOOLS", titleStyle);

            if (GUILayout.Button("Verify Room Graph"))
            {
                roomManager.StartGraphBuild();
                EditorUtility.SetDirty(roomManager); // Mark object as changed
            }

            if (GUILayout.Button("Initialize Rooms"))
            {
                roomManager.InitializeRooms();
                EditorUtility.SetDirty(roomManager); // Mark object as changed
            }

            if (GUILayout.Button("Undo Initialization"))
            {
                roomManager.UndoInitialization();
                EditorUtility.SetDirty(roomManager); // Mark object as changed
            }
        }
    }
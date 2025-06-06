using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace ClearPigeon.Helpers
{
    public static class SceneHelper
    {
        public static List<T> GetAllComponentsInScene<T>(Scene scene) where T : Component
        {
            List<T> components = new List<T>();

            // Get all root GameObjects in the scene
            GameObject[] rootObjects = scene.GetRootGameObjects();
            foreach (GameObject rootObject in rootObjects)
            {
                // Get components of type T in each root GameObject and its children
                components.AddRange(rootObject.GetComponentsInChildren<T>(true));
            }

            return components;
        }
    }
}

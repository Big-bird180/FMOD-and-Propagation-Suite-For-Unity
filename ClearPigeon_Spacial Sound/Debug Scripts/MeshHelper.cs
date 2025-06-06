using System.Collections.Generic;
using UnityEngine;


public static class MeshHelper
{
    public static HashSet<Plane> GetPlanes(this Mesh mesh)
    {
        HashSet<Plane> hashSet = new HashSet<Plane>();
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        int num = triangles.Length / 3;
        for (int i = 0; i < num; i++)
        {
            Vector3 a = vertices[triangles[i * 3]];
            Vector3 b = vertices[triangles[i * 3 + 1]];
            Vector3 c = vertices[triangles[i * 3 + 2]];
            Plane item = new Plane(a, b, c);
            item.distance = 0f - item.distance;
            hashSet.Add(item);
        }
        return hashSet;
    }
}
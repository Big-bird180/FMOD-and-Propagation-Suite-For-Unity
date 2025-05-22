using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ClearPigeon.Audio;
public class RoomCell : MonoBehaviour
{
    [SerializeField]
    private Room owner;

    [SerializeField]
    public RoomPortal portal;

    private Transform tr;

    private Mesh mesh;

    private MeshCollider meshCol;

    private HashSet<Plane> meshPlanes;

    public Vector3 Center
    {
        get
        {
            if (!HasInitialized)
            {
                CacheCollider();
            }
            return mesh.bounds.center;
        }
    }

    public Collider Collider
    {
        get
        {
            if (!HasInitialized)
            {
                CacheCollider();
            }
            return meshCol;
        }
    }

    public Room Owner => owner;

    public bool HasOwner { get; private set; }

    public int OwnerInstanceId
    {
        get
        {
            if (!HasOwner)
            {
                return -1;
            }
            return owner.GetInstanceID();
        }
    }

    private bool HasInitialized { get; set; }

    private void Awake()
    {
        tr = GetComponent<Transform>();
        if (!HasInitialized)
        {
            CacheCollider();
        }
    }

    public void CacheCollider()
    {
        meshCol = GetComponent<MeshCollider>();
        if ((bool)meshCol)
        {
            meshCol.convex = true;
            mesh = meshCol.sharedMesh;
            meshPlanes = mesh.GetPlanes();
        }
        HasInitialized = true;
    }

    public void SetOwner(Room owner)
    {
        this.owner = owner;
        HasOwner = owner != null;
    }

    public void SetPortal(RoomPortal portal)
    {
        this.portal = portal;
    }

    public bool IsPointWithin(Vector3 point, bool debug)
    {
        if (!HasInitialized)
        {
            CacheCollider();
        }
        Vector3 vector = meshCol.ClosestPoint(point);
        float num = Vector3.SqrMagnitude(vector - point);
        if (debug)
        {
            Debug.DrawLine(point, vector, Color.red, 5f);
        }
        return num <= Mathf.Epsilon * Mathf.Epsilon;
    }

    
}

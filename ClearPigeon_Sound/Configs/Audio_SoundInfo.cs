
using System;
using UnityEngine;
using ClearPigeon.Audio;

[Serializable]
public class Audio_SoundInfo
{
	public Audio_Source source;

	public Audio_Asset asset;

	public GameObject owner;

	public GameObject instigator;

	//public Audio_SoundListener listener;

	public GameObject ignore;

	public float distance;

	public float height;

	public float attenuation;

	public float gain;

	public float volume;

	public float pitch;

	public float blocking;

	public float occlusion;

	public float eavesdropping;

	public float rangeMin;

	public float rangeMax;

	public int AILevelMin;

	public int AILevelMax;

	public AI_SoundType AIFlags;

	public int portalCount;

	public int pointCount;

	public RoomPortal[] portals;

	public Vector3[] points;

	public Vector3 anchorPoint;

	public float DistanceSq => distance * distance;

	public float RangeMinSq => rangeMin * rangeMin;

	public float RangeMaxSq => rangeMax * rangeMax;

	public Vector3 AnchorPoint => anchorPoint;

	public Vector3 OriginPosition
	{
		get
		{
			if (pointCount <= 0)
			{
				return Vector3.zero;
			}
			return points[0];
		}
	}

	public Vector3 EndPosition
	{
		get
		{
			if (pointCount <= 0)
			{
				return Vector3.zero;
			}
			return points[pointCount - 1];
		}
	}

	public Audio_SoundInfo(int maxPortalDepth)
	{
		portals = new RoomPortal[maxPortalDepth + 1];
		points = new Vector3[maxPortalDepth + 2];
	}

	public void AddPoint(Vector3 point)
	{
		points[pointCount] = point;
		pointCount++;
	}

	public void SetPoints(int pointCount, Vector3[] points)
	{
		this.pointCount = pointCount;
		if (pointCount > 0)
		{
			for (int i = 0; i < pointCount; i++)
			{
				this.points[i] = points[i];
			}
		}
	}

	public void SetPortals(int portalCount, RoomPortal[] portals)
	{
		this.portalCount = portalCount;
		if (portalCount > 0)
		{
			for (int i = 0; i < portalCount; i++)
			{
				this.portals[i] = portals[i];
			}
		}
	}

	public void DrawPointPath()
	{
		if (pointCount > 1)
		{
			for (int i = 1; i < pointCount; i++)
			{
				Vector3 start = points[i - 1];
				Vector3 end = points[i];
				Debug.DrawLine(start, end, Color.red, 3f);
			}
		}
	}

	public override string ToString()
	{
		return $"Source: '{source.name}', volume: '{volume}', blocking '{blocking}', point count: '{pointCount}', portal count: '{portalCount}'.";
	}

	public void Clear()
	{
		distance = 0f;
		height = 0f;
		attenuation = 1f;
		volume = 0f;
		gain = 0f;
		blocking = 0f;
		eavesdropping = 0f;
		occlusion = 0f;
		rangeMax = 0f;
		rangeMax = 0f;
		AILevelMin = 0;
		AILevelMax = 0;
		AIFlags = AI_SoundType.None;
		for (int i = 0; i < portalCount; i++)
		{
			portals[i] = null;
		}
		portalCount = 0;
		for (int j = 0; j < pointCount; j++)
		{
			points[j] = Vector3.zero;
		}
		pointCount = 0;
	}
}
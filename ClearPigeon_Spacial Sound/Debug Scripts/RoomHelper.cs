using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class RoomHelper
{
    public static bool TryGetCellFromPoint(this List<RoomCell> cellList, Vector3 point, bool debug, out RoomCell cell)
    {
        if (cellList.Count > 0)
        {
            for (int i = 0; i < cellList.Count; i++)
            {
                RoomCell roomCell = cellList[i];
                if (roomCell.IsPointWithin(point, debug))
                {
                    cell = roomCell;
                    return true;
                }
            }
        }
        cell = null;
        return false;
    }
}

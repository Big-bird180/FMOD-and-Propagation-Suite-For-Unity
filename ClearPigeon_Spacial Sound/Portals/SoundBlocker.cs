using System;
using System.Collections.Generic;
using UnityEngine;

public class SoundBlocker : MonoBehaviour
{
    public bool _isOpen;

    public event Action<bool> OnIsOpenChanged;

    public float occlusionAmount = 0.1f;
    public float attinuationAggression = 25;
    public float attinuationInfluence = 2;
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen != value)  // Only trigger the event if the value changes
            {
                _isOpen = value;
                OnIsOpenChanged?.Invoke(_isOpen);  // Trigger the event
            }
        }
    }
}

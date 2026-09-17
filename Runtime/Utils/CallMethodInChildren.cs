using System;
using UnityEngine;

/// <summary>
/// Utility component to broadcast a method call down all child GameObjects in the hierarchy.
/// Commonly used on vehicle root objects with callbackName 'UpdateVehicle' to synchronize
/// sensor topics, streamers, and TF frame IDs across all child components.
/// </summary>
[AddComponentMenu("Marus/Utils/Call Method In Children")]
public class CallMethodInChildren : MonoBehaviour
{
    [Tooltip("The name of the method to invoke on this GameObject and all child components (e.g., 'UpdateVehicle').")]
    public string callbackName = "UpdateVehicle";
}

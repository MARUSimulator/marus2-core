using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CallMethodInChildren))]
public class CallMethodInChildrenInspector : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "Broadcasts a method call to all child components in the hierarchy.\n\n" +
            "Common usage: Set Callback Name to 'UpdateVehicle' and click the button to update ROS topics, sensor frame IDs, and TF prefixes across all child sensors after adding sensors or renaming the vehicle.",
            MessageType.Info
        );

        DrawDefaultInspector();

        EditorGUILayout.Space(5);

        if (GUILayout.Button("Call method in children", GUILayout.Height(30)))
        {
            UpdateChildren();
        }
    }

    void UpdateChildren()
    {
        var conn = (CallMethodInChildren)target;
        if (!string.IsNullOrEmpty(conn.callbackName))
        {
            Undo.RecordObjects(
                conn.gameObject.GetComponentsInChildren<MonoBehaviour>(true),
                $"Broadcast {conn.callbackName}"
            );
            conn.gameObject.BroadcastMessage(conn.callbackName, null, SendMessageOptions.DontRequireReceiver);
            Debug.Log($"[CallMethodInChildren] Broadcasted '{conn.callbackName}' to all children of '{conn.gameObject.name}'.", conn.gameObject);
        }
    }
}

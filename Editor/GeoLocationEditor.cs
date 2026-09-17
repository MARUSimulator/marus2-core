// Copyright 2026 Laboratory for Underwater Systems and Technologies (LABUST)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using UnityEditor;
using UnityEngine;

namespace Marus.Core.Editor
{
    [CustomEditor(typeof(GeoLocation))]
    [CanEditMultipleObjects]
    public class GeoLocationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            GeoLocation location = (GeoLocation)target;

            serializedObject.Update();

            var origin = location.CurrentOrigin;

            if (location.TrackPosition && !Application.isPlaying && origin != null)
            {
                location.SyncFromCurrentPosition();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Object Geographic Location", EditorStyles.boldLabel);

            if (origin == null)
            {
                EditorGUILayout.HelpBox("No GeoOrigin found in the scene! Add a GeoOrigin component to anchor the world coordinates.", MessageType.Warning);
                if (GUILayout.Button("Create GeoOrigin in Scene"))
                {
                    var go = new GameObject("GeoOrigin");
                    var newOrigin = go.AddComponent<GeoOrigin>();
                    location.CurrentOrigin = newOrigin;
                    EditorUtility.SetDirty(location);
                    Selection.activeGameObject = go;
                }
                EditorGUILayout.Space();
            }

            DrawDefaultInspector();

            EditorGUILayout.Space();

            // Real-time Coordinate Readout
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Current GPS Position:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(location.Coordinates.ToDmsString(), EditorStyles.wordWrappedLabel);

            if (origin != null)
            {
                double dist = origin.Origin.DistanceTo(location.Coordinates);
                double bearing = origin.Origin.BearingTo(location.Coordinates);
                EditorGUILayout.LabelField($"Distance from Origin:", $"{dist:F1} m (Bearing: {bearing:F0}°)");
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Bidirectional Action Buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Sync From Scene Position"))
            {
                location.SyncFromCurrentPosition();
                EditorUtility.SetDirty(location);
            }

            if (GUILayout.Button("Move Object To Coordinates"))
            {
                Undo.RecordObject(location.transform, "Move Object to GPS Coordinates");
                location.MoveToCoordinates(location.Coordinates);
                EditorUtility.SetDirty(location.transform);
            }
            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
        }
    }
}


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
    [CustomEditor(typeof(GeoOrigin))]
    public class GeoOriginEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            GeoOrigin geoOrigin = (GeoOrigin)target;

            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("MARUS Geographic Origin (WGS84)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Defines the real-world GPS anchor at Unity (0, 0, 0). All sensors, Metocean providers, and GPS coordinates synchronize relative to this origin.", MessageType.Info);

            EditorGUILayout.Space();

            // Draw default properties
            DrawDefaultInspector();

            EditorGUILayout.Space();

            // DMS Display Box
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Human-Readable DMS Format:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(geoOrigin.Origin.ToDmsString(), EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }
    }
}


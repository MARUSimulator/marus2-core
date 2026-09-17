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

using UnityEngine;

namespace Marus.Core
{
    /// <summary>
    /// Component attached to any GameObject (vessel, buoy, sensor, dock, POI) to provide real-time
    /// georeferencing (WGS84 Latitude, Longitude, Altitude) relative to the scene's <see cref="GeoOrigin"/>.
    /// Supports bidirectional syncing: reading live coordinates as the object moves, or teleporting/placing
    /// the object by entering target GPS coordinates.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("MARUS/Core/Geo Location")]
    public class GeoLocation : MonoBehaviour
    {
        [Header("Anchor Reference")]
        [Tooltip("Optional direct reference to the scene's GeoOrigin anchor. If not set, automatically resolves to the scene's GeoOrigin.")]
        [SerializeField] private GeoOrigin _origin;

        [Header("Geographic Coordinates (WGS84)")]
        [Tooltip("Real-world geographic coordinates computed relative to GeoOrigin.")]
        [SerializeField] private GeoPoint _coordinates;

        [Header("Tracking Settings")]
        [Tooltip("If true, automatically updates Latitude, Longitude, and Altitude from transform.position.")]
        [SerializeField] private bool _trackPosition = true;

        public GeoOrigin CurrentOrigin
        {
            get
            {
                if (_origin != null) return _origin;
                if (GeoOrigin.HasInstance)
                {
                    _origin = GeoOrigin.Instance;
                    return _origin;
                }
#if UNITY_6000_0_OR_NEWER
                _origin = Object.FindFirstObjectByType<GeoOrigin>(FindObjectsInactive.Include);
#else
                _origin = Object.FindObjectOfType<GeoOrigin>();
#endif
                return _origin;
            }
            set => _origin = value;
        }

        public bool HasOrigin => CurrentOrigin != null;

        public GeoPoint Coordinates => _coordinates;
        public double Latitude => _coordinates.latitude;
        public double Longitude => _coordinates.longitude;
        public double Altitude => _coordinates.altitude;

        public bool TrackPosition
        {
            get => _trackPosition;
            set => _trackPosition = value;
        }

        private void Reset()
        {
            var origin = CurrentOrigin;
            SyncFromCurrentPosition();
        }

        private void OnEnable()
        {
            SyncFromCurrentPosition();
        }

        private void Update()
        {
            if (_trackPosition)
            {
                var origin = CurrentOrigin;
                if (origin != null)
                {
                    _coordinates = origin.Unity2Geo(transform.position);
                }
            }
        }

        /// <summary>
        /// Reads the current 3D Unity transform position and recalculates geodetic coordinates.
        /// </summary>
        public void SyncFromCurrentPosition()
        {
            var origin = CurrentOrigin;
            if (origin != null)
            {
                _coordinates = origin.Unity2Geo(transform.position);
            }
        }

        /// <summary>
        /// Teleports/moves this GameObject in the Unity world to match the specified real-world geodetic coordinates.
        /// </summary>
        public void MoveToCoordinates(GeoPoint targetCoordinates)
        {
            var origin = CurrentOrigin;
            if (origin != null)
            {
                _coordinates = targetCoordinates;
                transform.position = origin.Geo2Unity(targetCoordinates);
            }
            else
            {
                Debug.LogWarning("[GeoLocation] Cannot move object to GPS coordinates: No GeoOrigin found in scene.", this);
            }
        }

        /// <summary>
        /// Teleports/moves this GameObject in the Unity world to match the specified real-world latitude and longitude.
        /// </summary>
        public void MoveToCoordinates(double latitude, double longitude, double altitude = 0.0)
        {
            MoveToCoordinates(new GeoPoint(latitude, longitude, altitude));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.4f);
        }
    }
}


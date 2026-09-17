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

using System;
using Marus.Utils;
using UnityEngine;

namespace Marus.Core
{
    /// <summary>
    /// Central singleton managing the scene's real-world geographic origin (WGS84 anchor).
    /// Anchors the Unity Cartesian origin (0, 0, 0) to a specific geodetic Latitude, Longitude, and Altitude.
    /// Provides bidirectional conversions between Unity world space and WGS84 GPS coordinates.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("MARUS/Core/Geo Origin")]
    public class GeoOrigin : Singleton<GeoOrigin>
    {
        [Header("WGS84 Geodetic Origin")]
        [Tooltip("Latitude in decimal degrees (WGS84).")]
        [SerializeField] private double _latitude = 43.733333; // Default: Šibenik coastal waters

        [Tooltip("Longitude in decimal degrees (WGS84).")]
        [SerializeField] private double _longitude = 15.883333;

        [Tooltip("Altitude in meters above WGS84 ellipsoid (approx. Mean Sea Level).")]
        [SerializeField] private double _altitude = 0.0;

        [Header("Orientation")]
        [Tooltip("Clockwise rotation angle in degrees from Unity +Z axis to True North. 0 means Unity +Z is due True North.")]
        [Range(0f, 360f)]
        [SerializeField] private float _trueNorthOffset = 0.0f;

        public event Action<GeoPoint> OnOriginChanged;

        private GeographicFrame _frame;

        public double Latitude
        {
            get => _latitude;
            set { _latitude = value; RebuildFrame(); }
        }

        public double Longitude
        {
            get => _longitude;
            set { _longitude = value; RebuildFrame(); }
        }

        public double Altitude
        {
            get => _altitude;
            set { _altitude = value; RebuildFrame(); }
        }

        public float TrueNorthOffset
        {
            get => _trueNorthOffset;
            set { _trueNorthOffset = value; RebuildFrame(); }
        }

        public GeoPoint Origin => new GeoPoint(_latitude, _longitude, _altitude);

        public GeographicFrame Frame
        {
            get
            {
                if (_frame == null) RebuildFrame();
                return _frame;
            }
        }

        /// <summary>
        /// Direction vector pointing toward True North in Unity world coordinates.
        /// </summary>
        public Vector3 TrueNorthVector => Quaternion.Euler(0f, _trueNorthOffset, 0f) * Vector3.forward;

        /// <summary>
        /// Direction vector pointing toward East in Unity world coordinates.
        /// </summary>
        public Vector3 EastVector => Quaternion.Euler(0f, _trueNorthOffset, 0f) * Vector3.right;

        protected override void Awake()
        {
            base.Awake();
            RebuildFrame();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (instance == null)
            {
                instance = this;
            }
            RebuildFrame();
        }

        private void OnValidate()
        {
            if (instance == null)
            {
                instance = this;
            }
            RebuildFrame();
        }

        /// <summary>
        /// Updates the geographic origin coordinates.
        /// </summary>
        public void SetOrigin(double latitude, double longitude, double altitude = 0.0, float trueNorthOffset = 0f)
        {
            _latitude = latitude;
            _longitude = longitude;
            _altitude = altitude;
            _trueNorthOffset = trueNorthOffset;
            RebuildFrame();
        }

        private void RebuildFrame()
        {
            _frame = new GeographicFrame(transform, _latitude, _longitude, _altitude, _trueNorthOffset);
            OnOriginChanged?.Invoke(Origin);
        }

        /// <summary>
        /// Converts a 3D Unity world position to real-world WGS84 coordinates.
        /// </summary>
        public GeoPoint Unity2Geo(Vector3 unityPosition)
        {
            return Frame.Unity2Geo(unityPosition);
        }

        /// <summary>
        /// Converts real-world WGS84 coordinates to a 3D Unity world position.
        /// </summary>
        public Vector3 Geo2Unity(GeoPoint geoPoint)
        {
            return Frame.Geo2Unity(geoPoint);
        }

        /// <summary>
        /// Converts real-world latitude and longitude to a 3D Unity world position.
        /// </summary>
        public Vector3 Geo2Unity(double latitude, double longitude, double altitude = 0.0)
        {
            return Frame.Geo2Unity(new GeoPoint(latitude, longitude, altitude));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(Vector3.zero, 1.0f);

            // Draw North and East axis indicators
            Vector3 north = TrueNorthVector * 5.0f;
            Vector3 east = EastVector * 5.0f;

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(Vector3.zero, north);

            Gizmos.color = Color.red;
            Gizmos.DrawRay(Vector3.zero, east);
        }
    }
}

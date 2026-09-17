// Copyright 2022 Laboratory for Underwater Systems and Technologies (LABUST)
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
using UnityEngine;

namespace Marus.Core
{
    //TODO
    [System.Serializable]
    public struct GeoPoint
    {
        public double latitude;
        public double longitude;
        public double altitude;

        ///Semi-major axis of the local geodetic datum ellipsoid
        const double a = 6378137;
        ///Flattening of the local geodetic datum ellipsoid
        const double f = 1 / 298.257223563;
        ///Semi-minor axis of the local geodetic datum ellipsoid
        const double b = a * (1 - f);
        ///First eccentricity squared
        const double esq = 2 * f - f * f;
        ///Secondary eccentricity squared
        const double ecsq = (a * a - b * b) / (b * b);

        public GeoPoint(double latitude, double longitude, double altitude)
        {
            this.latitude = latitude;
            this.longitude = longitude;
            this.altitude = altitude;
        }

        public GeoPoint(Geometry.Vector3 ecef)
        {
            var geo = Ecef2Geodetic(ecef);

            this.latitude = geo.latitude;
            this.longitude = geo.longitude;
            this.altitude = geo.altitude;
        }

        public static GeoPoint Ecef2Geodetic(Geometry.Vector3 ecef)
        {
            double x = ecef.X;
            double y = ecef.Y;
            double z = ecef.Z;

            double r = Math.Sqrt(x * x + y * y);
            double F = 54 * (b*b) * (z*z);
            double G = r * r + (1 - esq)* z * z - esq * (a * a - b * b);
            double c = esq * esq * F * r * r / (G * G *G);
            double s = cbrt(1 + c + Math.Sqrt(c * c + 2 * c));
            double P = F / (3 * (s + 1 / s + 1) * ( s + 1 / s + 1) * G * G);
            double Q = Math.Sqrt(1 + 2 * esq * esq * P);
            double r0 = -P * esq * r / (1 + Q) + Math.Sqrt(0.5 * a * a * ( 1 + 1 / Q)
                                              - P * (1 - esq) * z * z / (Q * (1 + Q)) - 0.5 * P * r * r);
            double re = r - esq * r0;
            double U = Math.Sqrt(re * re + z * z);
            double V = Math.Sqrt(re * re + (1 - esq) * z *z);
            double z0 = b * b *z / (a * V);

            return new GeoPoint(
                (Math.Atan((z + ecsq * z0) / r) * 180 / Math.PI),
                (Math.Atan2(y, x) * 180 / Math.PI),
                (U * (1 - b * b / (a * V))));
        }

        private static double cbrt(double root)
        {
            if (root < 0)
                return -Math.Pow(-root, 1d / 3d);

            return Math.Pow(root, 1d / 3d);
        }

        public static Geometry.Vector3 Geodetic2ecef(GeoPoint geo)
        {
            double rlat = geo.latitude * Math.PI / 180;
            double rlon = geo.longitude * Math.PI / 180;
            double R = a / Math.Sqrt(1 - esq * Math.Sin(rlat) * Math.Sin(rlat));

            var retVal = new Geometry.Vector3();
            retVal.X = (R + geo.altitude) * Math.Cos(rlat) * Math.Cos(rlon);
            retVal.Y = (R + geo.altitude) * Math.Cos(rlat) * Math.Sin(rlon);
            retVal.Z = (R + geo.altitude - esq * R) * Math.Sin(rlat);

            return retVal;
        }

        public static Geometry.Vector3 Enu2Ecef(GeographicFrame frame, Vector3 enu)
        {
            var origin = frame.origin;
            var originEcef = frame.originEcef;
            var slon = Math.Sin(Math.PI * origin.longitude / 180.0);
            var slat = Math.Sin(Math.PI * origin.latitude / 180.0);
            var clon = Math.Cos(Math.PI * origin.longitude / 180.0);
            var clat = Math.Cos(Math.PI * origin.latitude / 180.0);

            var retVal = new Geometry.Vector3();
            retVal.X = originEcef.X - slon * enu.x - slat * clon * enu.y + clat * clon * enu.z;
            retVal.Y = originEcef.Y + clon * enu.x - slat * slon * enu.y + clat * slon * enu.z;
            retVal.Z = originEcef.Z + 0            +          clat*enu.y +          slat*enu.z;

            return retVal;
        }

        /// <summary>
        /// Converts an ECEF coordinate back to local East-North-Up (ENU) coordinates relative to a GeographicFrame origin.
        /// </summary>
        public static Vector3 Ecef2Enu(GeographicFrame frame, Geometry.Vector3 ecef)
        {
            var origin = frame.origin;
            var originEcef = frame.originEcef;
            var slon = Math.Sin(Math.PI * origin.longitude / 180.0);
            var slat = Math.Sin(Math.PI * origin.latitude / 180.0);
            var clon = Math.Cos(Math.PI * origin.longitude / 180.0);
            var clat = Math.Cos(Math.PI * origin.latitude / 180.0);

            double dx = ecef.X - originEcef.X;
            double dy = ecef.Y - originEcef.Y;
            double dz = ecef.Z - originEcef.Z;

            float east = (float)(-slon * dx + clon * dy);
            float north = (float)(-slat * clon * dx - slat * slon * dy + clat * dz);
            float up = (float)(clat * clon * dx + clat * slon * dy + slat * dz);

            return new Vector3(east, north, up);
        }

        /// <summary>
        /// Calculates geodesic surface distance in meters between two coordinates using the Haversine formula.
        /// </summary>
        public double DistanceTo(GeoPoint other)
        {
            double rlat1 = latitude * Math.PI / 180.0;
            double rlat2 = other.latitude * Math.PI / 180.0;
            double dlat = (other.latitude - latitude) * Math.PI / 180.0;
            double dlon = (other.longitude - longitude) * Math.PI / 180.0;

            double h = Math.Sin(dlat * 0.5) * Math.Sin(dlat * 0.5) +
                       Math.Cos(rlat1) * Math.Cos(rlat2) *
                       Math.Sin(dlon * 0.5) * Math.Sin(dlon * 0.5);
            double c = 2.0 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(Math.Max(0.0, 1.0 - h)));
            return a * c;
        }

        /// <summary>
        /// Calculates the initial forward azimuth bearing in degrees (0 - 360°) from this point towards another.
        /// </summary>
        public double BearingTo(GeoPoint other)
        {
            double rlat1 = latitude * Math.PI / 180.0;
            double rlat2 = other.latitude * Math.PI / 180.0;
            double dlon = (other.longitude - longitude) * Math.PI / 180.0;

            double y = Math.Sin(dlon) * Math.Cos(rlat2);
            double x = Math.Cos(rlat1) * Math.Sin(rlat2) - Math.Sin(rlat1) * Math.Cos(rlat2) * Math.Cos(dlon);
            double bRad = Math.Atan2(y, x);
            double bDeg = bRad * (180.0 / Math.PI);
            return (bDeg % 360.0 + 360.0) % 360.0;
        }

        /// <summary>
        /// Formats coordinates as Degrees, Minutes, Seconds (DMS) string with N/S and E/W indicators.
        /// </summary>
        public string ToDmsString()
        {
            return $"{FormatDms(latitude, true)} {FormatDms(longitude, false)} (Alt: {altitude:F1}m)";
        }

        private static string FormatDms(double val, bool isLatitude)
        {
            char dir = isLatitude ? (val >= 0 ? 'N' : 'S') : (val >= 0 ? 'E' : 'W');
            val = Math.Abs(val);
            int d = (int)val;
            double mTotal = (val - d) * 60.0;
            int m = (int)mTotal;
            double s = (mTotal - m) * 60.0;
            return $"{d}°{m:00}'{s:00.00}\"{dir}";
        }

        public override string ToString()
        {
            return $"{latitude:F6}°, {longitude:F6}° (Alt: {altitude:F1}m)";
        }
    }
}
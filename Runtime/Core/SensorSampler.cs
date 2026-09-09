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
using System.Collections.Generic;
using Marus.Utils;
using UnityEngine;

#if UNITY_6000_5_OR_NEWER
using SensorId = UnityEngine.EntityId;
#else
using SensorId = System.Int32;
#endif

namespace Marus.Core
{
    [DefaultExecutionOrder(100)]
    public class SensorSampler : Singleton<SensorSampler>
    {

        Dictionary<SensorId, SensorCallback> _sensorCallbacks = new Dictionary<SensorId, SensorCallback>();
        Dictionary<SensorId, double> _timeOfLastCallback = new Dictionary<SensorId, double>();

        void FixedUpdate()
        {
            foreach (var kvp in _sensorCallbacks)
            {
                var callback = kvp.Value;

                if (callback.active
                    && callback.sensor.isActiveAndEnabled)
                {
                    if (!_timeOfLastCallback.TryGetValue(kvp.Key, out var time)
                        || EnoughTimePassed(time, callback.sensor))
                    {
                        callback.callback();
                        callback.sensor.hasData = true;
                        _timeOfLastCallback[kvp.Key] = Time.fixedTimeAsDouble;
                    }
                }
            }
        }

        private bool EnoughTimePassed(double lastTime, SensorBase sensor)
        {
            if (sensor.SampleFrequency <= 0)
                return true;
            return Time.fixedTimeAsDouble >= lastTime + 1 / sensor.SampleFrequency;
        }

        private static SensorId GetSensorId(SensorBase sensor)
        {
#if UNITY_6000_5_OR_NEWER
            return sensor.GetEntityId();
#else
            return sensor.GetInstanceID();
#endif
        }

        public void AddSensorCallback(SensorBase sensor, Action callback)
        {
            var id = GetSensorId(sensor);
            if (_sensorCallbacks.ContainsKey(id))
                return;

            var sensorCallback = new SensorCallback
            {
                callback = callback,
                sensor = sensor,
                active = true
            };
            _sensorCallbacks.Add(id, sensorCallback);
        }

        private void RemoveSensorCallback(SensorBase sensor)
        {
            _sensorCallbacks.Remove(GetSensorId(sensor));
        }

        internal void DisableCallback(SensorBase sensor)
        {
            if (_sensorCallbacks.TryGetValue(GetSensorId(sensor), out var callback))
            {
                callback.active = false;
            }
        }

        internal void EnableCallback(SensorBase sensor)
        {
            if (_sensorCallbacks.TryGetValue(GetSensorId(sensor), out var callback))
            {
                callback.active = true;
            }
        }

    }

    /// <summary>
    /// Data class for sensor callback definition
    /// </summary>
    public class SensorCallback
    {
        public SensorBase sensor;
        // callback for sensor update
        public Action callback;
        public bool active = true;
    };
}
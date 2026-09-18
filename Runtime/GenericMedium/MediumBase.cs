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

namespace Marus.Core
{
    /// <summary>
    /// Base class for communication media in MARUS, inheriting Singleton so each medium
    /// can be accessed globally via TSelf.Instance.
    /// </summary>
    public abstract class MediumBase<TSelf> : Singleton<TSelf> where TSelf : Component
    {
        public string Name;
    }

    /// <summary>
    /// Generic communication medium managing typed message routing and device lifecycle.
    /// Provides null-safe registration, unregistration, safe broadcast iteration,
    /// and distance calculations.
    /// </summary>
    /// <typeparam name="TSelf">The concrete medium implementation (CRTP).</typeparam>
    /// <typeparam name="TMessage">The base message type for this medium.</typeparam>
    /// <typeparam name="TDevice">The device/transceiver component type.</typeparam>
    public abstract class MediumBase<TSelf, TMessage, TDevice> : MediumBase<TSelf>
        where TSelf : MediumBase<TSelf, TMessage, TDevice>
        where TDevice : Component
        where TMessage : class
    {
        public List<TDevice> RegisteredDevices = new List<TDevice>();

        protected override void Initialize()
        {
            base.Initialize();
            if (RegisteredDevices == null)
            {
                RegisteredDevices = new List<TDevice>();
            }
        }

        /// <summary>
        /// Registers a device so messages can be broadcast or transmitted to it.
        /// </summary>
        public virtual void Register(TDevice device)
        {
            if (device != null && !RegisteredDevices.Contains(device))
            {
                RegisteredDevices.Add(device);
            }
        }

        /// <summary>
        /// Unregisters a device so messages are no longer routed to it.
        /// </summary>
        public virtual void Unregister(TDevice device)
        {
            if (device != null)
            {
                RegisteredDevices.Remove(device);
            }
        }

        /// <summary>
        /// Broadcasts a message to all registered devices in range.
        /// Automatically prunes any destroyed devices safely during backwards iteration.
        /// </summary>
        public virtual void Broadcast(TMessage msg)
        {
            if (msg == null)
            {
                return;
            }

            for (int i = RegisteredDevices.Count - 1; i >= 0; i--)
            {
                var device = RegisteredDevices[i];
                if (device == null)
                {
                    RegisteredDevices.RemoveAt(i);
                    continue;
                }

                if (device.gameObject.activeInHierarchy && !IsSender(msg, device))
                {
                    Transmit(msg, device);
                }
            }
        }

        /// <summary>
        /// Transmits a message to a specific receiver device.
        /// Derived classes implement physical propagation constraints (range, frequency, delay).
        /// </summary>
        public abstract bool Transmit(TMessage msg, TDevice receiver);

        /// <summary>
        /// Identifies whether a given device was the sender of the message.
        /// </summary>
        protected abstract bool IsSender(TMessage msg, TDevice device);

        /// <summary>
        /// Calculates Euclidean distance between two devices in Unity world space.
        /// </summary>
        public virtual float DistanceFromTo(TDevice deviceA, TDevice deviceB)
        {
            if (deviceA == null || deviceB == null)
            {
                return float.MaxValue;
            }
            return Vector3.Distance(deviceA.transform.position, deviceB.transform.position);
        }

        /// <summary>
        /// Calculates the transmission range/distance between two devices.
        /// Defaults to Euclidean distance. Override to add protocol or medium-specific constraints.
        /// </summary>
        public virtual float Range(TDevice source, TDevice target)
        {
            return DistanceFromTo(source, target);
        }

        /// <summary>
        /// Retrieves a registered device by its integer ID.
        /// Override in derived mediums whose devices define an integer identifier.
        /// </summary>
        public virtual TDevice GetDeviceById(int id)
        {
            return null;
        }

        /// <summary>
        /// Retrieves a registered device of type TSub by its integer ID.
        /// </summary>
        public virtual TSub GetDeviceById<TSub>(int id) where TSub : class
        {
            return GetDeviceById(id) as TSub;
        }
    }
}

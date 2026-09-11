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

using UnityEngine;
using Sensorstreaming;
using Google.Protobuf;
using Grpc.Core;
using System;
using Marus.Networking;
using Marus.Logger;
using Marus.Utils;
using Marus.ROS;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

namespace Marus.Core
{

    /// <summary>
    /// Base class that every sensor has to implement
    /// Sensor streams readings to the server defined in RosConnection singleton instance
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [DefaultExecutionOrder(200)]
    public abstract class SensorStreamer<TClient, TMsg> : MonoBehaviour
        where TClient : ClientBase
        where TMsg : IMessage
    {

        [Space]
        [Header("Streaming Parameters")]
        public float UpdateFrequency = 1;
        public string address;

        public int MessageQueueSize = 10000;

        protected Transform _vehicle;

        protected SensorBase _sensor;

        Task _awaitable;
        double _prevMsgTime;

        /// <summary>
        /// A client instance used for streaming sensor readings
        /// </summary>
        /// <value></value>
        protected TClient streamingClient
        {
            get
            {
                return RosConnection.Instance.GetClient<TClient>();
            }
        }

        AsyncClientStreamingCall<TMsg, Std.Empty> streamHandle;
        Func<Grpc.Core.Metadata, System.DateTime?, System.Threading.CancellationToken, AsyncClientStreamingCall<TMsg, Std.Empty>> _streamingFn;

        // Cached MethodInfo of the sensor's streaming method (e.g., StreamImuSensor, StreamGnssSensor).
        // Used by StartClientStream to dynamically re-bind the delegate to new client instances after reconnect.
        System.Reflection.MethodInfo _streamingMethod;
        bool _isSubscribedToRos;

        volatile bool _killSendMsgsThread;
        Thread _sendMsgThread;
        ConcurrentQueue<TMsg> _msgQueue;

        /// <summary>
        /// Starts or restarts the gRPC client-to-server streaming call.
        /// When ROS reconnects, RosConnection creates a new GrpcChannel and instantiates a new TClient.
        /// Since C# delegates created in derived Start() methods (e.g. streamingClient.StreamImuSensor) are
        /// permanently bound to the original client instance on the now-disposed channel, we check whether
        /// the delegate's target matches the current client instance. If not, Delegate.CreateDelegate re-binds
        /// the method to the new client instance on the active channel.
        /// </summary>
        private AsyncClientStreamingCall<TMsg, Std.Empty> StartClientStream()
        {
            if (_streamingMethod == null || !RosConnection.HasInstance || !RosConnection.Instance.IsConnected)
                return null;

            try
            {
                var client = streamingClient;
                if (client == null) return null;

                // If the client instance changed after reconnection, re-bind delegate to the new client
                if (_streamingFn == null || !object.ReferenceEquals(_streamingFn.Target, client))
                {
                    var method = _streamingMethod;
                    if (method.DeclaringType != null && !method.DeclaringType.IsAssignableFrom(client.GetType()))
                    {
                        var m = client.GetType().GetMethod(method.Name, new[] {
                            typeof(Grpc.Core.Metadata),
                            typeof(System.DateTime?),
                            typeof(System.Threading.CancellationToken)
                        });
                        if (m != null) method = m;
                    }

                    _streamingFn = (Func<Grpc.Core.Metadata, System.DateTime?, System.Threading.CancellationToken, AsyncClientStreamingCall<TMsg, Std.Empty>>)
                        Delegate.CreateDelegate(
                            typeof(Func<Grpc.Core.Metadata, System.DateTime?, System.Threading.CancellationToken, AsyncClientStreamingCall<TMsg, Std.Empty>>),
                            client,
                            method
                        );
                }

                return _streamingFn(null, null, RosConnection.Instance.CancellationToken);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void SubscribeToRos()
        {
            if (_isSubscribedToRos) return;
            if (RosConnection.HasInstance)
            {
                RosConnection.Instance.OnConnected += HandleConnected;
                RosConnection.Instance.OnDisconnected += HandleDisconnected;
                _isSubscribedToRos = true;
            }
        }

        private void UnsubscribeFromRos()
        {
            if (!_isSubscribedToRos) return;
            if (RosConnection.HasInstance)
            {
                RosConnection.Instance.OnConnected -= HandleConnected;
                RosConnection.Instance.OnDisconnected -= HandleDisconnected;
            }
            _isSubscribedToRos = false;
        }

        /// <summary>
        /// On reconnection, nullify streamHandle and sync timer so SendMessagesThread starts
        /// a fresh stream on the new GrpcChannel without delay.
        /// </summary>
        private void HandleConnected(Grpc.Core.ChannelBase channel)
        {
            streamHandle = null;
            _prevMsgTime = Time.fixedTimeAsDouble;
        }

        /// <summary>
        /// On disconnection, clear streamHandle and purge any backlog in _msgQueue so old,
        /// stale sensor frames from before the disconnect are not sent upon reconnect.
        /// </summary>
        private void HandleDisconnected()
        {
            streamHandle = null;
            if (_msgQueue != null)
            {
                while (_msgQueue.TryDequeue(out _)) { }
            }
        }

        /// <summary>
        /// Used to write sensor reading messages
        /// </summary>
        /// <value></value>
        private IClientStreamWriter<TMsg> _streamWriter
        {
            get
            {
                if (streamHandle != null)
                    return streamHandle.RequestStream;
                return null;
            }
        }

        public Transform vehicle
        {
            get
            {
                _vehicle = Helpers.GetVehicle(transform);
                if (_vehicle == null)
                {
                    Debug.Log($@"Cannot get vehicle from sensor {transform.name}.
                        Using sensor as the vehicle transform");
                    return transform;
                }
                return _vehicle;
            }
        }

        #if UNITY_EDITOR
        protected void Reset()
        {
            if(gameObject.GetComponent<TfStreamerROS>() == null)
            {
                gameObject.AddComponent<TfStreamerROS>();
            }
            UpdateVehicle();
        }
        #endif

        public void UpdateVehicle()
        {
            var veh = vehicle;
            // reset address to empty if UpdateVehicle is not called from reset
            address = "";
            // if not same object, add vehicle name prefix to address
            if(veh != transform)
                address = $"{veh.name}/";

            address = $"{address}{gameObject.name}";
        }

        /// <summary>
        /// Call this from derived class after calling StreamSensor method
        /// Best to be called last in the derived Start method
        /// </summary>
        public void Start()
        {
            SetUpdateFrequency(UpdateFrequency);
            SetAddresSufix(_sensor.name);
            SubscribeToRos();
            if (_msgQueue == null)
            {
                _msgQueue = new ConcurrentQueue<TMsg>();
            }
            if (_sendMsgThread == null)
            {
                _killSendMsgsThread = false;
                _sendMsgThread = new Thread(SendMessagesThread) { IsBackground = true };
                _sendMsgThread.Priority = System.Threading.ThreadPriority.Highest;
                _sendMsgThread.Start();
            }
        }

        private void OnEnable()
        {
            SubscribeToRos();
            if (_sendMsgThread == null && _sensor != null)
            {
                if (_msgQueue == null)
                {
                    _msgQueue = new ConcurrentQueue<TMsg>();
                }
                _killSendMsgsThread = false;
                _sendMsgThread = new Thread(SendMessagesThread) { IsBackground = true };
                _sendMsgThread.Priority = System.Threading.ThreadPriority.Highest;
                _sendMsgThread.Start();
            }
        }

        protected void FixedUpdate()
        {
            SendMessage();
        }

        /// <summary>
        /// Must be called after StreamSensor is called
        /// </summary>
        void SetUpdateFrequency(float frequency)
        {
            // set to lesser freq
            var newFrequency = Mathf.Min(frequency, _sensor.SampleFrequency);
            // avoid 0 frequency
            newFrequency = newFrequency == 0 ? _sensor.SampleFrequency : newFrequency;
            if (newFrequency != UpdateFrequency)
            {
                Debug.Log(@$"{_sensor.name} send frequency is less then sample frequency.
                    Send frequency will be set to sample frequency.");
                UpdateFrequency = newFrequency;
            }
        }

        /// <summary>
        /// Set the address of the receiver to the ${sensor.vehicle.name}/{sufix}
        /// </summary>
        /// <param name="sufix"></param>
        protected void SetAddresSufix(string sufix)
        {
            if (string.IsNullOrEmpty(address))
                address = $"{_sensor.vehicle?.name}/{sufix}";
        }


        /// <summary>
        /// Must be called after StreamSensor is called
        /// </summary>
        void SendMessage()
        {
            SubscribeToRos();

            // if not connected, do not send
            if (!RosConnection.HasInstance || !RosConnection.Instance.IsConnected)
                return;

            var dt = 1.0f / UpdateFrequency;
            var time = Time.fixedTimeAsDouble;

            // If disconnected or lagging for longer than 2 intervals, reset timer to current time.
            // This prevents bursting a backlog of messages when connection is restored.
            if (time - _prevMsgTime > 2.0 * dt)
            {
                _prevMsgTime = time;
            }

            // calculate when msg has to be sent,
            // and send when enough time passes
            var nextMsgTime = _prevMsgTime + dt;
            if (time >= nextMsgTime)
            {
                var msg = ComposeMessage();
                _prevMsgTime = nextMsgTime;
                if (_msgQueue.Count == MessageQueueSize)
                {
                    // remove first element
                    _msgQueue.TryDequeue(out _);
                    Debug.Log($"Grpc Message overflow in {_sensor.name}");
                }
                _msgQueue.Enqueue(msg);
            }
        }

        /// <summary>
        /// Background worker thread dedicated to writing sensor messages to the gRPC stream.
        /// Handles reconnection asynchronously without blocking the Unity main rendering thread.
        /// </summary>
        private async void SendMessagesThread()
        {
            int count = 0;
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            while (true)
            {
                if (_killSendMsgsThread)
                {
                    return;
                }

                if (!RosConnection.HasInstance || !RosConnection.Instance.IsConnected)
                {
                    streamHandle = null;
                    Thread.Sleep(100);
                    continue;
                }

                // Dynamically establish/rebind client stream if not yet active
                if (streamHandle == null && _streamingMethod != null)
                {
                    try
                    {
                        streamHandle = StartClientStream();
                    }
                    catch
                    {
                        streamHandle = null;
                    }

                    if (streamHandle == null)
                    {
                        Thread.Sleep(200);
                        continue;
                    }
                }

                while (_msgQueue.TryDequeue(out var msg))
                {
                    if (_streamWriter == null)
                    {
                        break;
                    }

                    try
                    {
                        await _streamWriter.WriteAsync(msg);
                        count++;
                    }
                    catch (Exception)
                    {
                        // WriteAsync failed. Invalidate stream handle.
                        // If the server port is closed (container stopped), trigger OnConnectionDropped immediately.
                        streamHandle = null;
                        if (RosConnection.HasInstance && RosConnection.Instance.IsConnected && !RosConnection.Instance.IsServerPortOpen(50))
                        {
                            RosConnection.Instance.OnConnectionDropped();
                        }
                        break;
                    }
                }
                var dt = 1000.0 / UpdateFrequency; // in ms
                var sleepTime = dt - sw.ElapsedMilliseconds;
                if (sleepTime > 1) // minimum is 1 miliseconds
                {
                    Thread.Sleep((int)sleepTime);
                }

                if (sw.ElapsedMilliseconds > 5000)
                {
                    sw.Restart();
                    // check real frequency
                    // Debug.Log($"{Time.fixedTimeAsDouble}");
                }

            }
        }


        /// <summary>
        /// Main method to be called for streaming the sensor
        /// It must be called before any other function from this base class
        /// </summary>
        protected void StreamSensor(SensorBase sensor,
            // AsyncClientStreamingCall<TMsg, Std.Empty> streamingCall
            Func<Grpc.Core.Metadata, System.DateTime?, System.Threading.CancellationToken, AsyncClientStreamingCall<TMsg, Std.Empty>> streamingFn)
        {
            _sensor = sensor;
            _streamingFn = streamingFn;
            _streamingMethod = streamingFn?.Method;
            if (RosConnection.HasInstance && RosConnection.Instance.IsConnected)
            {
                streamHandle = StartClientStream();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromRos();
            _killSendMsgsThread = true;
            _sendMsgThread?.Join(500);
            _sendMsgThread = null;
            streamHandle = null;
        }

        private void OnDestroy()
        {
            UnsubscribeFromRos();
            _killSendMsgsThread = true;
            _sendMsgThread?.Join(500);
            _sendMsgThread = null;
            streamHandle = null;
        }

        protected abstract TMsg ComposeMessage();
    }
}

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
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using UnityEngine;
using Marus.Utils;
using Marus.CustomInspector;

// Only core dependencies remain here
using static Ping.Ping;
using static Simulationcontrol.SimulationControl;
using Ping;
using Simulationcontrol;

// Assuming Core, Tf, Time, and Param are part of marus2-core
using Marus.Core;
using static Tf.Tf;

namespace Marus.Networking
{
    /// <summary>
    /// Singleton class for configuring and connecting to
    /// ROS server
    /// </summary>
    [DefaultExecutionOrder(-1)]
    public class RosConnection : Singleton<RosConnection>
    {
        [Header("Server info")]
        public string serverIP = "localhost";
        public int serverPort = 30052;
        [HideInRuntimeInspector]
        public int connectionTimeout = 5;

        [Header("Simulation")]
        public bool DisplayTf = false;

        [HideInRuntimeInspector]
        public bool RealtimeSimulation = true;

        [ConditionalHideInInspector("RealtimeSimulation", true)]
        [HideInRuntimeInspector]
        public float SimulationSpeed = 1;

        [Header("Earth origin frame")]
        public string OriginFrameName = "map";
        public string OriginFrameLatitude = "/d2/LocalOriginLat";
        public string OriginFrameLongitude = "/d2/LocalOriginLon";

        public double DefaultLatitude = 45;
        public double DefaultLongitude = 15;

        GrpcChannel _streamingChannel;

        public GrpcChannel StreamingChannel => _streamingChannel;
        Dictionary<Type, ClientBase> _grpcClients;
        volatile bool _connected;
        public bool IsConnected => _connected;

        volatile bool _isConnecting;
        public bool IsConnecting => _isConnecting;

        CancellationTokenSource _cancellationTokenSource;
        CancellationToken _cancellationToken;
        public CancellationToken CancellationToken => _cancellationToken;

        private SimulationControlClient _simulationController;

        public event Action<ChannelBase> OnConnected;

        /// <summary>
        /// Optional custom HttpMessageHandler factory (e.g. for testing with mock handlers)
        /// </summary>
        public static Func<HttpMessageHandler> CustomHttpHandlerFactory { get; set; }

        /// <summary>
        /// Adds new client of type if it does not currently exists.
        /// </summary>
        public T AddNewClient<T>() where T : ClientBase
        {
            var t = typeof(T);
            if (_grpcClients.TryGetValue(t, out var clientBase))
                return clientBase as T;

            // Dynamically create the client using reflection.
            // This removes the need for RosConnection to know about the module in advance.
            var client = Activator.CreateInstance(typeof(T), _streamingChannel) as T;
            _grpcClients.Add(t, client);
            return client;
        }

        /// <summary>
        /// Get gRPC client of given type.
        /// Will instantiate it automatically if it doesn't exist yet.
        /// </summary>
        public T GetClient<T>() where T : ClientBase
        {
            if (_grpcClients.TryGetValue(typeof(T), out var client))
                return client as T;

            // Lazy load the client if requested by an external module
            return AddNewClient<T>();
        }

        private void Awake()
        {
            ThreadPool.SetMinThreads(12, 100);

            // Enable HTTP/2 cleartext (h2c) support for standard SocketsHttpHandler/HttpClientHandler fallback
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            var address = serverIP.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || serverIP.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? $"{serverIP}:{serverPort}"
                : $"http://{serverIP}:{serverPort}";

            var httpHandler = CreateHttpMessageHandler();
            var options = new GrpcChannelOptions
            {
                HttpHandler = httpHandler,
                MaxSendMessageSize = 1024 * 1024 * 100,
                MaxReceiveMessageSize = 1024 * 1024 * 100,
                DisposeHttpClient = true
            };

            _streamingChannel = GrpcChannel.ForAddress(address, options);
            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;

            _grpcClients = new Dictionary<Type, ClientBase>();

            CreateSingletons();
            Connect();

            StartCoroutine(WhileConnectionAwait());
        }

        /// <summary>
        /// Creates an HttpMessageHandler compatible with Unity.
        /// In Unity 6000.5 and above, utilizes UnityEngine.Networking.UnityHttpMessageHandler with HTTP/2 support.
        /// Falls back to standard HttpClientHandler when not running in Unity 6000.5+.
        /// </summary>
        private HttpMessageHandler CreateHttpMessageHandler()
        {
            if (CustomHttpHandlerFactory != null)
            {
                return CustomHttpHandlerFactory();
            }

#if UNITY_6000_5_OR_NEWER
            var handler = new UnityEngine.Networking.UnityHttpMessageHandler();
            handler.HttpForcedVersion = UnityEngine.Networking.HttpForcedVersion.HTTP2;
            return handler;
#else
            var unityHandlerType = Type.GetType("UnityEngine.Networking.UnityHttpMessageHandler, UnityEngine.UnityWebRequestModule")
                ?? Type.GetType("UnityEngine.Networking.UnityHttpMessageHandler, UnityEngine.CoreModule");
            if (unityHandlerType != null)
            {
                var handler = (HttpMessageHandler)Activator.CreateInstance(unityHandlerType);
                var prop = unityHandlerType.GetProperty("HttpForcedVersion");
                if (prop != null)
                {
                    try
                    {
                        var http2Value = Enum.Parse(prop.PropertyType, "HTTP2");
                        prop.SetValue(handler, http2Value);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Could not set HttpForcedVersion on UnityHttpMessageHandler: {ex.Message}");
                    }
                }
                return handler;
            }

            return new HttpClientHandler();
#endif
        }

        public void Connect()
        {
            if (!_connected && !_isConnecting)
            {
                _isConnecting = true;
                var t = new Thread(() =>
                {
                    _connected = TryConnect();
                    _isConnecting = false;
                });
                t.Start();
            }
        }

        void CreateSingletons()
        {
            // Only initialize core singletons here.
            // External modules (Visualization, Sensors) should initialize their own singletons
            // in their respective packages using RuntimeInitializeOnLoadMethod or Awake.
            var paramServer = ParamServerHandler.Instance;
            var tfHandler = TfHandler.Instance;
            var timeHandler = TimeHandler.Instance;
        }

        IEnumerator WhileConnectionAwait()
        {
            while (true)
            {
                if (_connected)
                {
                    OnRosConnected();
                    OnConnected?.Invoke(_streamingChannel);
                    break;
                }
                yield return null;
            }
        }

        void OnRosConnected()
        {
            if (!RealtimeSimulation)
            {
                GetSimulationController();
            }
        }

        void Update()
        {
            if (!RealtimeSimulation && IsConnected)
            {
                StartCoroutine(RosStep());
            }
        }

        IEnumerator RosStep()
        {
            yield return new WaitForEndOfFrame();

            if (_simulationController != null)
            {
                _simulationController.Step(
                    new StepRequest
                    {
                        TotalTimeSecs = TimeHandler.Instance.TotalTimeSecs,
                        TotalTimeNsecs = TimeHandler.Instance.TotalTimeNsecs
                    }
                );
            }
        }

        private void GetSimulationController()
        {
            _simulationController = GetClient<SimulationControlClient>();
            _simulationController.SetStartTime(
                new SetStartTimeRequest
                {
                    TimeSecs = TimeHandler.Instance.StartTimeSecs,
                    TimeNsecs = TimeHandler.Instance.StartTimeNsecs
                }
            );
        }

        /// <summary>
        /// Ping gRPC service to see if connection if established.
        /// </summary>
        bool TryConnect()
        {
            Debug.Log("Awaiting connection with ROS Server...");
            var pingClient = GetClient<PingClient>();
            try
            {
                var response = pingClient.Ping(new PingMsg(), deadline: DateTime.UtcNow.AddSeconds(connectionTimeout), cancellationToken: _cancellationToken);
                if (response.Value == 1)
                {
                    Debug.Log("Connected to the ROS Server");
                    return true;
                }
            }
            catch (RpcException e)
            {
                Debug.Log($"Could not establish a connection to ROS Server. {e.Message}");
            }
            catch (Exception e)
            {
                Debug.Log($"Could not establish a connection to ROS Server. {e.Message}");
            }
            return false;
        }

        void OnDisable()
        {
            if (_streamingChannel == null)
            {
                return;
            }

            Debug.Log("Shutting down grpc clients and channel...");
            try
            {
                _cancellationTokenSource?.Cancel();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Exception during cancellation: {ex.Message}");
            }

            try
            {
                _streamingChannel?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Exception during GrpcChannel dispose: {ex.Message}");
            }
            finally
            {
                _streamingChannel = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _connected = false;
                _isConnecting = false;
            }
            Debug.Log("Shut down of grpc channel successful.");
        }
    }
}
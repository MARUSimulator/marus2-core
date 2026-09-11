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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
        readonly ConcurrentDictionary<Type, ClientBase> _grpcClients = new ConcurrentDictionary<Type, ClientBase>();
        readonly object _clientsLock = new object();
        volatile bool _connected;
        public bool IsConnected => _connected;

        volatile bool _isConnecting;
        public bool IsConnecting => _isConnecting;
        Thread _connectThread;

        CancellationTokenSource _cancellationTokenSource;
        CancellationToken _cancellationToken;
        public CancellationToken CancellationToken => _cancellationToken;

        private SimulationControlClient _simulationController;

        public event Action<ChannelBase> OnConnected;
        public event Action OnDisconnected;
        private volatile bool _fireConnected;
        private volatile bool _fireDisconnected;
        private Task _heartbeatTask;
        private readonly object _connectionLock = new object();

        [Header("Reconnection")]
        public float reconnectInterval = 2.0f;

        /// <summary>
        /// Optional custom HttpMessageHandler factory (e.g. for testing with mock handlers)
        /// </summary>
        public static Func<HttpMessageHandler> CustomHttpHandlerFactory { get; set; }

        /// <summary>
        /// Adds new client of type if it does not currently exist.
        /// </summary>
        public T AddNewClient<T>() where T : ClientBase
        {
            var t = typeof(T);
            if (_grpcClients.TryGetValue(t, out var clientBase))
                return clientBase as T;

            lock (_clientsLock)
            {
                if (_grpcClients.TryGetValue(t, out clientBase))
                    return clientBase as T;

                if (_streamingChannel == null)
                {
                    Debug.LogError($"Cannot create gRPC client {typeof(T).Name}: StreamingChannel is null.");
                    return null;
                }

                // Dynamically create the client using reflection.
                // This removes the need for RosConnection to know about the module in advance.
                var client = Activator.CreateInstance(typeof(T), _streamingChannel) as T;
                _grpcClients[t] = client;
                return client;
            }
        }

        /// <summary>
        /// Get gRPC client of given type.
        /// Will instantiate it automatically if it doesn't exist yet.
        /// </summary>
        public T GetClient<T>() where T : ClientBase
        {
            if (_streamingChannel == null)
            {
                InitChannel();
            }

            if (_grpcClients.TryGetValue(typeof(T), out var client))
                return client as T;

            // Lazy load the client if requested by an external module
            return AddNewClient<T>();
        }

        protected override void Awake()
        {
            base.Awake();
            if (instance != this)
            {
                return;
            }

            ThreadPool.SetMinThreads(12, 100);

            // Enable HTTP/2 cleartext (h2c) support for standard SocketsHttpHandler/HttpClientHandler fallback
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;

            InitChannel();

            CreateSingletons();
            StartCoroutine(ConnectionLifecycleLoop());
        }

        public void InitChannel()
        {
            lock (_clientsLock)
            {
                if (_streamingChannel != null)
                {
                    try
                    {
                        _streamingChannel.Dispose();
                    }
                    catch { }
                    _streamingChannel = null;
                }

                _grpcClients.Clear();

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
            }
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
            if (_connected || _isConnecting)
            {
                return;
            }

            _isConnecting = true;
            Debug.Log($"Awaiting connection with ROS Server ({serverIP}:{serverPort})...");

            _connectThread = new Thread(() =>
            {
                try
                {
                    var connected = TryConnect();
                    if (connected)
                    {
                        _connected = true;
                        _fireConnected = true;
                    }
                }
                catch (ThreadAbortException)
                {
                    // Domain reload or thread abort - exit gracefully
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Exception during TryConnect: {ex.Message}");
                }
                finally
                {
                    _isConnecting = false;
                }
            })
            {
                IsBackground = true
            };
            _connectThread.Start();
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

        /// <summary>
        /// Connection lifecycle coroutine.
        /// Continually ensures the connection state stays synchronized. If disconnected, waits for
        /// reconnectInterval cooldown before initiating the next connection attempt in a background thread.
        /// </summary>
        IEnumerator ConnectionLifecycleLoop()
        {
            // Initial attempt on startup
            if (!_connected && !_isConnecting)
            {
                Connect();
            }

            while (true)
            {
                float connectStartTime = Time.realtimeSinceStartup;
                while (_isConnecting && (Time.realtimeSinceStartup - connectStartTime) < (connectionTimeout + 1.0f))
                {
                    yield return null;
                }

                if (_isConnecting)
                {
                    Debug.LogWarning("Connection attempt timed out. Resetting connecting state.");
                    _isConnecting = false;
                    try { _connectThread?.Abort(); } catch { }
                }

                if (!_connected)
                {
                    // Enforce cooldown before attempting reconnect to prevent CPU spin and socket thrashing
                    yield return new WaitForSecondsRealtime(reconnectInterval);

                    if (!_connected && !_isConnecting)
                    {
                        Connect();
                    }
                }
                else
                {
                    yield return new WaitForSecondsRealtime(1.0f);
                }
            }
        }

        void OnRosConnected()
        {
            if (!RealtimeSimulation)
            {
                GetSimulationController();
            }
            StartHeartbeat();
        }

        void StartHeartbeat()
        {
            if (_heartbeatTask == null || _heartbeatTask.IsCompleted)
            {
                _heartbeatTask = HeartbeatLoop(_cancellationToken);
            }
        }

        /// <summary>
        /// Background heartbeat task.
        /// Probes the server port via IsServerPortOpen(100) every second.
        /// This detects container shutdown or port closure in &lt; 0.1ms without issuing an HTTP/2 gRPC call
        /// that could block or throw curl broken pipe errors in UnityHttpMessageHandler.
        /// </summary>
        async Task HeartbeatLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (_connected && !cancellationToken.IsCancellationRequested)
                {
                    if (!IsServerPortOpen(100))
                    {
                        if (_connected && !cancellationToken.IsCancellationRequested)
                        {
                            OnConnectionDropped();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Called when connection to ROS server is lost, severed, or container is stopped.
        /// 1. Marks state as disconnected and flags OnDisconnected event to be fired on main thread.
        /// 2. Cancels active streams via CancellationTokenSource so streaming writers fail-fast.
        /// 3. Cleans up old GrpcChannel and clears cached client instances so fresh ones are bound upon reconnect.
        /// </summary>
        public void OnConnectionDropped()
        {
            lock (_connectionLock)
            {
                if (!_connected)
                {
                    return;
                }

                _connected = false;
                Debug.Log("Disconnected from ROS server");
                _fireDisconnected = true;

                // Cancel active streams so callers abort promptly instead of writing to closed socket
                try
                {
                    _cancellationTokenSource?.Cancel();
                    _cancellationTokenSource?.Dispose();
                }
                catch { }
                _cancellationTokenSource = new CancellationTokenSource();
                _cancellationToken = _cancellationTokenSource.Token;

                // Reset channel so broken HTTP/2 connection is discarded
                lock (_clientsLock)
                {
                    try
                    {
                        _streamingChannel?.Dispose();
                    }
                    catch { }
                    _streamingChannel = null;
                    _grpcClients.Clear();
                }
            }
        }

        void Update()
        {
            if (_fireConnected)
            {
                _fireConnected = false;
                OnRosConnected();
                try
                {
                    OnConnected?.Invoke(_streamingChannel);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Exception in OnConnected subscriber: {ex.Message}");
                }
            }

            if (_fireDisconnected)
            {
                _fireDisconnected = false;
                try
                {
                    OnDisconnected?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Exception in OnDisconnected subscriber: {ex.Message}");
                }
            }

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

        private string GetCleanHost()
        {
            var host = serverIP;
            if (string.IsNullOrEmpty(host))
                return "127.0.0.1";

            if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring(7);
            else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring(8);

            var slashIndex = host.IndexOf('/');
            if (slashIndex >= 0)
                host = host.Substring(0, slashIndex);

            var colonIndex = host.IndexOf(':');
            if (colonIndex >= 0)
                host = host.Substring(0, colonIndex);

            return string.IsNullOrEmpty(host) ? "127.0.0.1" : host;
        }

        /// <summary>
        /// Rapid non-blocking TCP socket check to verify whether the target port is open.
        /// When Unity connects before the server starts or while the container is down, calling gRPC Ping directly
        /// causes UnityHttpMessageHandler (built on libcurl) to throw verbose broken pipe / curl errors and
        /// potentially block threads. This socket check finishes in &lt; 0.1ms on localhost, cleanly failing fast.
        /// Works cross-platform on Linux (POSIX), Windows (Winsock), and macOS.
        /// </summary>
        public bool IsServerPortOpen(int timeoutMs = 200)
        {
            try
            {
                var host = GetCleanHost();
                System.Net.IPAddress ip;
                if (string.IsNullOrEmpty(host) || host == "localhost" || host == "127.0.0.1")
                {
                    ip = System.Net.IPAddress.Loopback;
                }
                else if (!System.Net.IPAddress.TryParse(host, out ip))
                {
                    var addresses = System.Net.Dns.GetHostAddresses(host);
                    if (addresses == null || addresses.Length == 0) return false;
                    ip = addresses[0];
                }

                using (var socket = new System.Net.Sockets.Socket(ip.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp))
                {
                    socket.Blocking = false;
                    try
                    {
                        socket.Connect(new System.Net.IPEndPoint(ip, serverPort));
                        return true;
                    }
                    catch (System.Net.Sockets.SocketException ex)
                    {
                        if (ex.SocketErrorCode == System.Net.Sockets.SocketError.WouldBlock ||
                            ex.SocketErrorCode == System.Net.Sockets.SocketError.InProgress)
                        {
                            bool canWrite = socket.Poll(timeoutMs * 1000, System.Net.Sockets.SelectMode.SelectWrite);
                            if (!canWrite) return false;

                            int error = (int)socket.GetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.Error);
                            return error == 0;
                        }
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Handshake procedure to establish and verify connection with ROS Server:
        /// 1. Verifies TCP port is open (fails in &lt; 1ms if server is down, avoiding curl errors).
        /// 2. Ensures GrpcChannel and PingClient are ready on the active channel.
        /// 3. Sends gRPC Ping and verifies response == 1.
        /// 4. Pauses 150ms to verify server stability and ensure it is not mid-shutdown before declaring connected.
        /// </summary>
        bool TryConnect()
        {
            // First verify that the TCP port is open.
            // If the server is not running, this returns false in < 1ms on localhost,
            // avoiding any deadlock or curl errors in UnityHttpMessageHandler.
            if (!IsServerPortOpen(200))
            {
                return false;
            }

            if (_streamingChannel == null)
            {
                InitChannel();
            }

            var pingClient = GetClient<PingClient>();
            if (pingClient == null)
            {
                return false;
            }

            try
            {
                var timeout = Math.Min(connectionTimeout, 2);
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout)))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, _cancellationToken))
                {
                    var response = pingClient.Ping(
                        new PingMsg(), 
                        deadline: DateTime.UtcNow.AddSeconds(timeout), 
                        cancellationToken: linked.Token
                    );
                    if (response != null && response.Value == 1)
                    {
                        // Verify stability: pause briefly to ensure server is steady and not in mid-shutdown
                        Thread.Sleep(150);
                        if (!IsServerPortOpen(100))
                        {
                            return false;
                        }

                        Debug.Log("Connected to the ROS Server");
                        return true;
                    }
                }
            }
            catch (ThreadAbortException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (RpcException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

        void OnDisable()
        {
            if (_streamingChannel == null && !_connected)
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

            if (_connectThread != null && _connectThread.IsAlive)
            {
                try
                {
                    _connectThread.Join(200);
                }
                catch { }
                _connectThread = null;
            }

            lock (_clientsLock)
            {
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
                    _grpcClients.Clear();
                    _streamingChannel = null;
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                    _connected = false;
                    _isConnecting = false;
                }
            }
            Debug.Log("Shut down of grpc channel successful.");
        }
    }
}
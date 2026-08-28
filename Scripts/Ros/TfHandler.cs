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
using System.Linq;
using Grpc.Core;
using UnityEngine;
using Marus.Core;
using Marus.Utils;
using static Tf.Tf;
using Tf;

// Removed Marus.Visualization dependency

namespace Marus.Networking
{
    /// <summary>
    /// Singleton class for configuring and connecting to
    /// ROS server
    /// </summary>
    public class TfHandler : Singleton<TfHandler>
    {
        private Transform _mapFrame;
        private GameObject _tf;
        private GeographicFrame _originGeoFrame;
        public GeographicFrame OriginGeoFrame => _originGeoFrame;

        public bool StreamFrames = true;
        AsyncServerStreamingCall<TfFrameList> _frameStream;

        ServerStreamer<TfFrameList> _serverStreamer;

        // NEW: Event that external modules (like Visualization) can subscribe to
        public event Action<Transform> OnNewTfFrameCreated;

        protected override void Initialize()
        {
            _serverStreamer = new ServerStreamer<TfFrameList>(UpdateTf);
            transform.parent = RosConnection.Instance.transform;
            InitializeTf();
            RosConnection.Instance.OnConnected += OnConnected;
        }

        public void OnConnected(ChannelBase channel)
        {
            SetGeoOriginAndMap();
        }

        public void SetGeoOriginAndMap()
        {
            double lat, lon;
            var rosConn = RosConnection.Instance;
            var originFrameLatitude = rosConn.OriginFrameLatitude;
            var originFrameLongitude = rosConn.OriginFrameLongitude;
            var originFrameName = rosConn.OriginFrameName;

            var paramServer = ParamServerHandler.Instance;
            bool latLonRetreived = true;
            if (!paramServer.TryGetParameter<double>(originFrameLatitude, out lat))
            {
                latLonRetreived &= true;
                lat = _originGeoFrame.origin.latitude;
                paramServer.TrySetParameter<double>(originFrameLatitude, lat);
            }

            if(!paramServer.TryGetParameter<double>(originFrameLongitude, out lon))
            {
                latLonRetreived &= true;
                lon = _originGeoFrame.origin.longitude;
                paramServer.TrySetParameter<double>(originFrameLongitude, lon);
            }

            if (latLonRetreived)
            {
                _originGeoFrame = new GeographicFrame(_mapFrame.transform, lat, lon, 0);
            }
        }

        public void GetTfFrameStream()
        {
        }

        void Update()
        {
            var rosConn = RosConnection.Instance;
            if (StreamFrames)
            {
                if (!_serverStreamer.IsStreaming)
                {
                    var frameClient = rosConn.GetClient<TfClient>();
                    var frameStream = frameClient.StreamAllFrames(new Std.Empty(),
                            cancellationToken:rosConn.CancellationToken);
                    _serverStreamer.StartStream(frameStream);
                }
                _serverStreamer.HandleNewMessages();
            }
        }

        private void UpdateTf(TfFrameList frameList)
        {
            var rosConn = RosConnection.Instance;
            foreach (var frame in frameList.Frames)
            {
                if (frame.ChildFrameId == rosConn.OriginFrameName)
                    continue;

                var frameObj = GetOrCreateGameObjectForFrame(frame);

                // set parent if it is not already set
                if (frameObj.transform.parent == null
                    || frameObj.transform.parent.name != frame.FrameId)
                {
                    var parentFrame = frameList.Frames.FirstOrDefault(x => x.ChildFrameId == frame.FrameId);
                    if (parentFrame != null)
                    {
                        var parentFrameObj = GetOrCreateGameObjectForFrame(parentFrame);
                        frameObj.transform.parent = parentFrameObj.transform;
                    }
                }

                var position = frame.Translation.AsUnity().Map2Unity();
                var rotation = frame.Rotation.AsUnity().Map2Unity();
                frameObj.transform.localPosition = position;
                frameObj.transform.localRotation = rotation;
            }
        }

        private void InitializeTf()
        {
            _tf = new GameObject("tf");

            // Parent to TfHandler instead of the Visualizer
            _tf.transform.parent = this.transform;

            var mapFrame = new GameObject("map");
            mapFrame.transform.position = Vector3.zero;
            mapFrame.transform.rotation = Quaternion.identity;
            mapFrame.transform.parent = _tf.transform;
            _mapFrame = mapFrame.transform;

            // Invoke event instead of hardcoding Visualizer.Instance.AddTransform
            OnNewTfFrameCreated?.Invoke(_mapFrame);

            var defaultLatitude = RosConnection.Instance.DefaultLatitude;
            var defaultLongitude = RosConnection.Instance.DefaultLongitude;
            _originGeoFrame = new GeographicFrame(mapFrame.transform, defaultLatitude, defaultLongitude, 0f);

            // (The Visualizer.Instance.DrawFilter logic was removed here.
            // The visualization package will handle that internally now.)
        }

        private GameObject GetOrCreateGameObjectForFrame(TfFrame frame)
        {
            var obj = Helpers.FindGameObjectInChildren(frame.ChildFrameId, _tf);
            if (obj == null)
            {
                obj = new GameObject(frame.ChildFrameId);
                obj.transform.parent = _tf.transform;

                // Invoke event instead of hardcoding Visualizer.Instance.AddTransform
                OnNewTfFrameCreated?.Invoke(obj.transform);
            }
            return obj;
        }
    }
}
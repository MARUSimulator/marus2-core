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
using Unity.Collections;
using UnityEngine;

namespace Marus.Core
{
    /// <summary>
    /// Interface for any sensor that generates a point cloud.
    /// External visualizers can listen to these events without knowing the specific sensor type.
    /// </summary>
    public interface IPointCloudSensor
    {
        event Action<GameObject, string, int, Material, ComputeShader> OnPointCloudInitialized;
        event Action<NativeArray<Vector3>> OnPointCloudUpdated;
    }
}
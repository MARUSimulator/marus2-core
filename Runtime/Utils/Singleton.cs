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
using System.Reflection;
using System.Threading;

namespace Marus.Utils
{
    internal static class SingletonManager
    {
        public static int MainThreadId { get; private set; }
        public static bool IsQuitting { get; set; }

        public static bool IsMainThread => MainThreadId == 0 || Thread.CurrentThread.ManagedThreadId == MainThreadId;

        static SingletonManager()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            IsQuitting = false;
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeSceneLoad()
        {
            IsQuitting = false;
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnApplicationQuitting()
        {
            IsQuitting = true;
        }
    }

    /// <summary>
    /// This is generic singleton implementation.
    /// </summary>
    public class Singleton<T> : MonoBehaviour where T : Component
    {
        protected static T instance;

        /// <summary>
        /// Check if an instance currently exists without triggering lazy instantiation.
        /// Useful during OnDestroy or scene teardown to prevent creating unwanted ghost instances.
        /// </summary>
        public static bool HasInstance => instance != null && !SingletonManager.IsQuitting;

        public static T Instance
        {
            get
            {
                if (SingletonManager.IsQuitting)
                {
                    return instance;
                }

                if (instance == null)
                {
                    // Unity API calls must be called from the main thread.
                    // If called from a worker thread before initialization, return current instance without throwing.
                    if (!SingletonManager.IsMainThread)
                    {
                        return instance;
                    }

#if UNITY_6000_0_OR_NEWER
                    instance = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
                    instance = Object.FindObjectOfType<T>();
#endif
                    if (instance == null)
                    {
                        if (SingletonManager.IsQuitting || !Application.isPlaying)
                        {
                            return null;
                        }

                        GameObject obj = new GameObject(typeof(T).Name);
                        instance = obj.AddComponent<T>();
                        if (Application.isPlaying && instance.transform.parent == null)
                        {
                            DontDestroyOnLoad(instance.gameObject);
                        }
                    }
                    var init = typeof(T).GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Instance);
                    init?.Invoke(instance, null);
                }
                return instance;
            }
        }

        protected virtual void Awake()
        {
            if (instance == null)
            {
                instance = this as T;
                if (Application.isPlaying && transform.parent == null)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (instance != this)
            {
                Debug.LogWarning($"Duplicate instance of singleton {typeof(T).Name} detected on '{gameObject.name}'. Destroying duplicate.");
                Destroy(gameObject);
                return;
            }
        }

        protected virtual void OnApplicationQuit()
        {
            SingletonManager.IsQuitting = true;
        }

        protected virtual void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        protected virtual void Initialize()
        {
        }
    }
}

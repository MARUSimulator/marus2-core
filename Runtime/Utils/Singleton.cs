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
        private static volatile bool _isPlaying;
        private static volatile bool _isQuitting;

        public static bool IsMainThread => MainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == MainThreadId;

        public static bool IsPlaying
        {
            get
            {
                if (IsMainThread)
                {
                    _isPlaying = Application.isPlaying;
                }
                return _isPlaying;
            }
        }

        public static bool IsQuitting
        {
            get => IsPlaying ? _isQuitting : false;
            set => _isQuitting = value;
        }

        static SingletonManager()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
#if UNITY_EDITOR
            _isPlaying = UnityEditor.EditorApplication.isPlaying;
#else
            _isPlaying = true;
#endif
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitEditor()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            _isPlaying = UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
            _isQuitting = false;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            switch (state)
            {
                case UnityEditor.PlayModeStateChange.EnteredEditMode:
                    MainThreadId = Thread.CurrentThread.ManagedThreadId;
                    _isPlaying = false;
                    _isQuitting = false;
                    break;
                case UnityEditor.PlayModeStateChange.ExitingEditMode:
                    MainThreadId = Thread.CurrentThread.ManagedThreadId;
                    _isQuitting = false;
                    break;
                case UnityEditor.PlayModeStateChange.EnteredPlayMode:
                    MainThreadId = Thread.CurrentThread.ManagedThreadId;
                    _isPlaying = true;
                    _isQuitting = false;
                    break;
                case UnityEditor.PlayModeStateChange.ExitingPlayMode:
                    _isQuitting = true;
                    break;
            }
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            _isQuitting = false;
            _isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeSceneLoad()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            _isQuitting = false;
            _isPlaying = true;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnApplicationQuitting()
        {
            _isQuitting = true;
        }
    }

    /// <summary>
    /// This is generic singleton implementation.
    /// </summary>
    public class Singleton<T> : MonoBehaviour where T : Component
    {
        protected static T instance;

        /// <summary>
        /// Check if an instance currently exists without creating a new GameObject.
        /// In Edit mode or after domain reload, searches the scene if the static reference is null.
        /// </summary>
        public static bool HasInstance
        {
            get
            {
                if (SingletonManager.IsQuitting) return false;
                if (instance != null) return true;
                if (!SingletonManager.IsMainThread) return false;

#if UNITY_6000_0_OR_NEWER
                instance = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
                instance = Object.FindObjectOfType<T>();
#endif
                return instance != null;
            }
        }

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
                        if (SingletonManager.IsQuitting || !SingletonManager.IsPlaying)
                        {
                            return null;
                        }

                        GameObject obj = new GameObject(typeof(T).Name);
                        instance = obj.AddComponent<T>();
                        if (SingletonManager.IsPlaying && instance.transform.parent == null)
                        {
                            DontDestroyOnLoad(instance.gameObject);
                        }
                    }
                    (instance as Singleton<T>)?.EnsureInitialized();
                }
                return instance;
            }
        }

        protected bool _isInitialized;

        protected void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Initialize();
            }
        }

        protected virtual void Awake()
        {
            if (instance == null)
            {
                instance = this as T;
                if (SingletonManager.IsPlaying && transform.parent == null)
                {
                    DontDestroyOnLoad(gameObject);
                }
                EnsureInitialized();
            }
            else if (instance != this)
            {
                if (SingletonManager.IsPlaying)
                {
                    Debug.LogWarning($"Duplicate instance of singleton {typeof(T).Name} detected on '{gameObject.name}'. Destroying duplicate.");
                    Destroy(gameObject);
                    return;
                }
            }
        }

        protected virtual void OnEnable()
        {
            if (instance == null)
            {
                instance = this as T;
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

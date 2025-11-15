//
// Copyright (c) 2022 Off The Beaten Track UG
// All rights reserved.
//
// Maintainer: Jens Bahr
//

using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Sparrow.Utilities
{
    /// <summary>
    /// Generic Singleton-Implementation. Has a WhenReady-Function to call logic once the class has been initialized.
    /// </summary>
    public class Singleton<T> : MonoBehaviour where T : Singleton<T>
    {
        static T s_Instance;
        bool m_IsInit = false;

        [SerializeField] bool m_DontDestroyOnLoad = false;

        public static bool exists => s_Instance != null;

        public static async void WhenReady(Action p)
        {
            while (s_Instance == null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }
            while (!s_Instance.m_IsInit)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }
            p.Invoke();
        }

        public static T instance
        {
            get
            {
                if (s_Instance == null)
                {
#if UNITY_2021_1_OR_NEWER
                    var objs = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None) as T[];
#else
                    var objs = FindObjectsOfType(typeof(T)) as T[];
#endif
                    if (objs.Length > 0)
                        s_Instance = objs[0];

                    if (objs.Length > 1)
                    {
                        Debug.LogError("[Singleton] There is more than one instance of " + typeof(T).Name + " in the scene.");
                    }

                    if (s_Instance == null)
                    {
                        Debug.Log("[Singleton] Creating first instance of singleton " + typeof(T).Name + ".");
                        GameObject obj = new GameObject();
                        obj.hideFlags = HideFlags.DontSave;
                        s_Instance = obj.AddComponent<T>();
                    } 
                }
                if (!s_Instance.m_IsInit)
                {
                    s_Instance.Init();
                    
                }
                return s_Instance;
            }
        }

        public void SetDontDestroyOnLoad()
        {
            if(!m_DontDestroyOnLoad)
                DontDestroyOnLoad(s_Instance);
            m_DontDestroyOnLoad = true;
        }

        private void Start()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(this);
                return;
            }
        }

        public virtual void Init()
        {
            if (m_IsInit) return;
            m_IsInit = true;

            if (s_Instance.m_DontDestroyOnLoad)
                DontDestroyOnLoad(s_Instance);

            InitializeInherit();
        }

        protected virtual void InitializeInherit() { }

        protected virtual void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }
    }
}

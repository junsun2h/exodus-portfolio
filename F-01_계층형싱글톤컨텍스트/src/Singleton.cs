using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PX
{
    public class SingletonBase : MonoEvent
    {
        public virtual void InitData()
        {
        }

        public virtual void ClearAllData()
        {
        }

        /// <summary>
        /// Firebase 네이티브 리소스를 해제하기 직전에 호출된다.
        /// Firestore 스냅샷 리스너처럼 네이티브 콜백을 물고 있는 것들을 여기서 끊어야 한다.
        /// 정리하지 않으면 네이티브 객체가 파괴된 뒤에도 Firebase 워커 스레드가
        /// 해제된 메모리를 참조해 에디터가 액세스 위반으로 죽는다.
        /// </summary>
        public virtual void ShutdownFirebase()
        {
        }

        public virtual void InitAfterGameDB()
        {
        }
        public virtual void InitAfterUserData()
        {
        }
    }

    public class Singleton<T> : SingletonBase where T : SingletonBase
    {
        private static object _InstanceLock = new object();
        private static T _Instance = null;

        public static T Instance
        {
            get
            {
                lock (_InstanceLock)
                {
                    if (_Instance == null)
                    {
                        Type t = typeof(T);

                        // Ensure there are no public constructors...
                        ConstructorInfo[] ctors = t.GetConstructors();
                        if (ctors.Length > 0)
                        {
                            throw new InvalidOperationException(String.Format("{0} has at least one accesible ctor making it impossible to enforce singleton behaviour", t.Name));
                        }

                        // Create an instance via the private constructor
                        _Instance = (T)Activator.CreateInstance(t, true);
                    }
                }

                return _Instance;
            }
        }
    }

    public class SingletonDependency<T> : SingletonBase where T : SingletonBase
    {
        private static object _InstanceLock = new object();
        private static T _Instance = null;

        Dictionary<string, SingletonBase> _genericDic = new Dictionary<string, SingletonBase>();

        public Dictionary<string, SingletonBase> GetGenericDic
        {
            get
            {
                return _genericDic;
            }
        }

        public static bool IsInit
        {
            get
            {
                return _Instance != null;
            }
        }


        public static T Instance
        {
            get
            {
                if (_Instance == null)
                {
                    Type t = typeof(T);
                    Debug.LogError($"Not AddSingleton Instance, {t.ToString()}");
                }

                return _Instance;
            }
        }

        public static T CreateInstance()
        {
            lock (_InstanceLock)
            {
                Type t = typeof(T);

                if (_Instance == null)
                {
                    // Ensure there are no public constructors...
                    ConstructorInfo[] ctors = t.GetConstructors();
                    if (ctors.Length > 0)
                    {
                        throw new InvalidOperationException(String.Format("{0} has at least one accesible ctor making it impossible to enforce singleton behaviour", t.Name));
                    }

                    // Create an instance via the private constructor
                    /*Debug.Log("SingletonDependency 생성자" + t.Name);*/
                    _Instance = (T)Activator.CreateInstance(t, true);
                }
                else
                {
                    //SingletonDependency은 중복 생성 호출을 불가합니다.
                    throw new Exception();
                }
            }

            return _Instance;
        }


        protected void AddSingleton<TS>(SingletonBase InData)
        {
            Type t = typeof(TS);
            string TargetName = t.Name;
            if (_genericDic.ContainsKey(TargetName) == false)
            {
                _genericDic.Add(TargetName, InData);
            }
        }
        protected void InitSingleton()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in _genericDic)
                EntryData.Value.InitData();
        }

        public SingletonBase GetSingleton<TS>()
        {
            Type t = typeof(TS);
            string TargetName = t.Name;
            SingletonBase TargetBase;

            if (_genericDic.TryGetValue(TargetName, out TargetBase))
                return TargetBase;

            return null;
        }

        ~SingletonDependency()
        {
            _genericDic.Clear();
            _Instance = null;
            _InstanceLock = null;


            Type t = typeof(T);
        }
    }
}

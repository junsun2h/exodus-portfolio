using System.Collections.Generic;
using UnityEngine;

//


namespace PX
{
    public class ApplicationContext : PXMonoBehaviour
    {
        public LogicContext _logicContext { get; private set; }
        public MonoContext _monoContext { get; private set; }

        static ApplicationContext m_Instance;

        List<MonoEvent> ContextMonoEventList;

#if UNITY_EDITOR
        public bool m_bIsTestBattleMode;
        public static bool IsTestBattleMode;
        public bool m_bIsTestCharacterMode;
        //public static bool IsTestCharacterMode;
#endif

        ApplicationContext()
        {
        }
        ~ApplicationContext()
        {
            ContextMonoEventList.Clear();
        }

        public static ApplicationContext Instance
        {
            get
            {
                if (m_Instance == null)
                {
                    m_Instance = FindObjectOfType<ApplicationContext>();
                    if (m_Instance == null)
                    {
                        Debug.LogError("ApplicationContext is null");
                    }
                }
                return m_Instance;
            }
        }

        void InitData()
        {
            _logicContext = LogicContext.CreateInstance();
            _monoContext = MonoContext.CreateInstance();

            ContextMonoEventList = new List<MonoEvent>
            {
                _logicContext,
                _monoContext,
            };

            foreach (SingletonBase EntryData in ContextMonoEventList)
                EntryData.InitData();
        }

        protected override void Awake()
        {

#if UNITY_EDITOR
            //IsTestCharacterMode = m_bIsTestCharacterMode;
            IsTestBattleMode = m_bIsTestBattleMode;

            // Log/Warning 에 붙는 스택트레이스 제거.
            // 로그 한 건마다 20줄 남짓한 스택이 따라붙으면 에디터 프로파일러의
            // UI 이벤트 버퍼가 한 프레임 한계를 넘어 에디터가 크래시한다
            // ("Allocating too large buffer for profiler!" → profiling::Dispatcher::AcquireFreeBuffer).
            // Error/Exception 은 원인 추적에 스택이 필요하므로 그대로 둔다.
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
#endif

            DontDestroyOnLoad(this.gameObject);

            InitData();

            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.Awake();
            }
        }

        public void OnEnable()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnEnable();
            }
        }

        protected override void Start()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.Start();
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.PostStart();
            }
        }

        void FixedUpdate()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.FixedUpdate();
            }
        }

        // Update is called once per frame
        void Update()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.Update();
            }
        }

        void LateUpdate()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.LateUpdate();
            }
        }

        void OnWillRenderObject()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnWillRenderObject();
            }
        }

        void OnPreCull()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnPreCull();
            }
        }

        void OnBecameVisible()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnBecameVisible();
            }
        }

        void OnBecameInvisible()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnBecameInvisible();
            }
        }

        void OnPreRender()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnPreRender();
            }
        }

        void OnRenderObject()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnRenderObject();
            }
        }

        void OnPostRender()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnPostRender();
            }
        }

        void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnRenderImage(src, dest);
            }
        }

        void OnDrawGizmos()
        {
            //OnDrawGizmos 는 Object 가 아닌 객체에 대한 레퍼런스를 참조할 수 없음. OnDrawGizmos 는 별도의 MonoSingleton 으로 구현하여 활용하는 것이 좋을 것
        }

        void OnGUI()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnGUI();
            }
        }

        void OnApplicationPause()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnApplicationPause();
            }
        }

        void OnDisable()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnDisable();
            }
        }

        void OnApplicationQuit()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnApplicationQuit();
            }

            // 각 매니저의 종료 처리가 끝난 뒤, Firebase 네이티브 리소스를 물고 있는
            // 스냅샷 리스너들을 정리한다. 정리하지 않으면 네이티브 객체가 파괴된 뒤에도
            // Firebase 워커 스레드가 해제된 메모리를 참조해 액세스 위반이 발생한다.
            // (에디터에서는 FirebaseEditorShutdownHook이 더 이른 시점에 먼저 처리한다)
            FBMainManager.ShutdownAll();
        }

        void OnDestroy()
        {
            if (ContextMonoEventList != null)
            {
                foreach (MonoEvent InData in ContextMonoEventList)
                    InData.OnDestroy();
            }
        }
    }
}


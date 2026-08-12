using System.Collections.Generic;
using UnityEngine;

namespace PX
{
    public class LogicContext : SingletonDependency<LogicContext>
    {
        private LogicContext() { }

        ~LogicContext() { }

        public override void InitData()
        {
            /**
             * Game Manager
             */
            AddSingleton<GameCryptoManager>(GameCryptoManager.CreateInstance());
            AddSingleton<GameDBClientManager>(GameDBClientManager.CreateInstance());
            AddSingleton<GameCharacterManager>(GameCharacterManager.CreateInstance());
            AddSingleton<GameBuffManager>(GameBuffManager.CreateInstance());
            AddSingleton<GameControlManager>(GameControlManager.CreateInstance());
            AddSingleton<GameAnimationManager>(GameAnimationManager.CreateInstance());
            AddSingleton<GameUIManager>(GameUIManager.CreateInstance());
            AddSingleton<GameBattleControlManager>(GameBattleControlManager.CreateInstance());
            AddSingleton<GameBattleUtilityManager>(GameBattleUtilityManager.CreateInstance());
            AddSingleton<GameProjectileManager>(GameProjectileManager.CreateInstance());
            AddSingleton<GameObjectPoolManager>(GameObjectPoolManager.CreateInstance());
            AddSingleton<GameTextDamageManager>(GameTextDamageManager.CreateInstance());

            AddSingleton<GameCameraManager>(GameCameraManager.CreateInstance());
            AddSingleton<GameAssetBundleManager>(GameAssetBundleManager.CreateInstance());
            AddSingleton<GameEffectAreaManager>(GameEffectAreaManager.CreateInstance());
            AddSingleton<GameEffectManager>(GameEffectManager.CreateInstance());
            AddSingleton<GamePrefabManager>(GamePrefabManager.CreateInstance());
            AddSingleton<GameAttachmentManager>(GameAttachmentManager.CreateInstance());
            AddSingleton<GameRewardCacheManager>(GameRewardCacheManager.CreateInstance());
            AddSingleton<GameAchievementManager>(GameAchievementManager.CreateInstance());
            AddSingleton<GameQuestManager>(GameQuestManager.CreateInstance());
            AddSingleton<GameReddotManager>(GameReddotManager.CreateInstance());
            AddSingleton<GamePlayerPrefsManager>(GamePlayerPrefsManager.CreateInstance());
            AddSingleton<GameTestManager>(GameTestManager.CreateInstance());
            AddSingleton<GameContentManager>(GameContentManager.CreateInstance());


            /**
             * Firebase Manager
             */
            AddSingleton<FBMainManager>(FBMainManager.CreateInstance());
            AddSingleton<FBAuthManager>(FBAuthManager.CreateInstance());
            AddSingleton<FBRemoteConfigManager>(FBRemoteConfigManager.CreateInstance());
            AddSingleton<FBMessagingManager>(FBMessagingManager.CreateInstance());

            /**
             * Security Manager
             */
            AddSingleton<GameSecurityManager>(GameSecurityManager.CreateInstance());

            /**
            * Game API Manager
            */
            AddSingleton<GameAPIManager>(GameAPIManager.CreateInstance());
            AddSingleton<GameAPIServerVersionCheckManager>(GameAPIServerVersionCheckManager.CreateInstance());
            AddSingleton<GameAPIDataBundleManager>(GameAPIDataBundleManager.CreateInstance());
            AddSingleton<GameAPIAccountManager>(GameAPIAccountManager.CreateInstance());
            AddSingleton<GameAPIUserManager>(GameAPIUserManager.CreateInstance());
            AddSingleton<GameAPIPlayerManager>(GameAPIPlayerManager.CreateInstance());
            AddSingleton<GameAPIAchivementManager>(GameAPIAchivementManager.CreateInstance());
            AddSingleton<GameAPICollectionManager>(GameAPICollectionManager.CreateInstance());
            AddSingleton<GameAPIConstellationManager>(GameAPIConstellationManager.CreateInstance());
            AddSingleton<GameAPIContentsManager>(GameAPIContentsManager.CreateInstance());
            AddSingleton<GameAPICurrencyManager>(GameAPICurrencyManager.CreateInstance());
            AddSingleton<GameAPIEquipmentManager>(GameAPIEquipmentManager.CreateInstance());
            AddSingleton<GameAPINebulaManager>(GameAPINebulaManager.CreateInstance());
            AddSingleton<GameAPIQuestManager>(GameAPIQuestManager.CreateInstance());
            AddSingleton<GameAPISkillManager>(GameAPISkillManager.CreateInstance());
            AddSingleton<GameAPIPetManager>(GameAPIPetManager.CreateInstance());
            AddSingleton<GameAPIGachaManager>(GameAPIGachaManager.CreateInstance());
            AddSingleton<GameAPIStageManager>(GameAPIStageManager.CreateInstance());
            AddSingleton<GameAPIModManager>(GameAPIModManager.CreateInstance());
            AddSingleton<GameAPIMonsterManager>(GameAPIMonsterManager.CreateInstance());
            AddSingleton<GameAPIMailboxManager>(GameAPIMailboxManager.CreateInstance());
            AddSingleton<GameAPIProductManager>(GameAPIProductManager.CreateInstance());
            AddSingleton<GameAPIRankManager>(GameAPIRankManager.CreateInstance());
            AddSingleton<GameAPIChatManager>(GameAPIChatManager.CreateInstance());
            AddSingleton<GameAPINoticeManager>(GameAPINoticeManager.CreateInstance());
            AddSingleton<GameAPIPresetManager>(GameAPIPresetManager.CreateInstance());
            AddSingleton<GameAPIUpdateDailyUserData>(GameAPIUpdateDailyUserData.CreateInstance());
            AddSingleton<GameAPIOfflineManager>(GameAPIOfflineManager.CreateInstance());

            /**
           * Init Instances
           */
            InitSingleton();
        }

        public override void Awake()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.Awake();
        }

        public override void Start()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.Start();

            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.PostStart();
        }

        public override void OnEnable()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnEnable();
        }

        public override void Update()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.Update();
        }

        public override void LateUpdate()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.LateUpdate();
        }

        public override void FixedUpdate()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.FixedUpdate();
        }

        public override void OnWillRenderObject()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnWillRenderObject();
        }

        public override void OnPreCull()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnPreCull();
        }

        public override void OnBecameVisible()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnBecameVisible();
        }

        public override void OnBecameInvisible()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnBecameInvisible();
        }

        public override void OnPreRender()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnPreRender();
        }

        public override void OnRenderObject()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnRenderObject();
        }

        public override void OnPostRender()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnPostRender();
        }

        public override void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnRenderImage(src, dest);
        }

        public override void OnGUI()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnGUI();
        }

        public override void OnApplicationPause()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnApplicationPause();
        }

        public override void OnDisable()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnDisable();
        }

        public override void OnApplicationQuit()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnApplicationQuit();
        }

        /// <summary>
        /// Firebase 네이티브 해제 직전 전파. 한 매니저가 실패해도 나머지는 정리되도록
        /// 개별로 예외를 격리한다 (정리 누락이 곧 크래시로 이어지기 때문).
        /// </summary>
        public override void ShutdownFirebase()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
            {
                try
                {
                    EntryData.Value.ShutdownFirebase();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"ShutdownFirebase failed: {EntryData.Key} / {e}");
                }
            }
        }

        public override void OnDestroy()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.OnDestroy();
        }

        public override void InitAfterGameDB()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.InitAfterGameDB();
        }

        public override void InitAfterUserData()
        {
            foreach (KeyValuePair<string, SingletonBase> EntryData in GetGenericDic)
                EntryData.Value.InitAfterUserData();
        }
    }
}

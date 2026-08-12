using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PX
{
    /// <summary>
    /// 게임 사운드(BGM, 효과음)를 통합 관리하는 MonoBehaviour 기반 매니저
    /// - Addressables 시스템 기반 오디오 로딩
    /// - BGM: 배경음악 재생/정지/페이드/볼륨 조절
    /// - SFX: 효과음 원샷 재생/볼륨 조절
    /// - PlayerPrefs 설정 연동
    /// - PowerSave 모드 지원
    /// - 백그라운드 실행 시 자동 일시정지/재개
    /// </summary>
    public class GameSoundManager : SingletonMono<GameSoundManager>
    {
        #region Constants

        /// <summary>
        /// Battle 효과음 AudioSource 풀 최대 개수.
        ///
        /// 티어별 동시 상한의 합(사망 8 + 히트 4 + 발사 3 + 스킬 2 = 17)이 풀보다 크면
        /// 각 상한을 지켜도 풀 자체가 포화된다. 포화되면 GetAvailableBattleSFXSource 가
        /// 티어와 무관하게 '가장 먼저 끝날 소스'를 뺏으므로, 드물게 울리는 스킬·오라음이
        /// 초당 수십 번 나는 히트음에 밀려 사라진다.
        /// 실측에서 10개가 전부 차는 구간이 관측돼 16으로 올렸다
        /// (재생하지 않는 AudioSource 의 유지 비용은 사실상 없다)
        /// </summary>
        private const int BATTLE_SFX_POOL_SIZE = 16;

        /// <summary>
        /// UI 효과음 AudioSource 풀 최대 개수
        /// </summary>
        private const int UI_SFX_POOL_SIZE = 5;

        /// <summary>
        /// BGM 기본 페이드 시간 (초)
        /// </summary>
        private const float DEFAULT_BGM_FADE_TIME = 1.0f;

        /// <summary>
        /// BGM 내부 기본 볼륨 배율 (0.0 ~ 1.0)
        /// 옵션은 BGM on/off만 제공하므로, 효과음 대비 배경음악이 크게 들리는 것을
        /// 이 값으로 내부 보정한다.
        /// 평소에는 GameSoundVolumeConfig.bgmMasterVolume을 쓰고,
        /// 이 상수는 Config 에셋을 찾지 못했을 때의 대체값으로만 쓰인다.
        /// </summary>
        private const float BGM_BASE_VOLUME = 0.5f;

        /// <summary>
        /// PowerSave 모드 시 BGM 볼륨 감소 비율
        /// </summary>
        private const float POWER_SAVE_BGM_VOLUME_RATIO = 0.5f;

        /// <summary>
        /// Battle 효과음 최소 재생 간격 (초) - 동일 SFX 중복 재생 방지
        /// </summary>
        private const float BATTLE_SFX_MIN_INTERVAL = 0.05f;

        /// <summary>
        /// 동시 재생 한도가 찼을 때, 울리던 소리를 끊고 자리를 물려받아도 되는 최소 재생 진행도.
        ///
        /// 사망음 후보 3종을 실측하니 70% 지점의 남은 진폭이 피크 대비 0.5~1.6% 라
        /// 여기서 끊으면 새 소리의 어택에 묻혀 들리지 않는다.
        /// 이보다 앞에서 끊으면(예: 50% 지점은 1.6~8.3%) 파형이 끊긴 자리가 '툭' 하고 튄다
        /// </summary>
        private const float BATTLE_SFX_STEAL_PROGRESS = 0.7f;

        /// <summary>
        /// 전투음 티어별 피치 랜덤 폭 (±비율).
        ///
        /// 같은 클립이 원음 그대로 반복되면 소리가 하나로 뭉쳐 기계적으로 들린다.
        /// 빈도가 높을수록 크게 흔든다 — 히트음은 초당 수십 회라 반복감이 가장 먼저 드러나고,
        /// 스킬·오라음은 음색 자체가 스킬 정체성이라 크게 흔들면 무슨 스킬인지 흐려진다.
        ///
        /// ⚠️ 풀의 AudioSource 를 Play() 로 재사용하므로 재생할 때마다 pitch 를 다시 대입해야 한다.
        /// 대입을 빠뜨리면 앞 소리의 피치가 그대로 남아 다음 소리를 오염시킨다
        /// </summary>
        private const float BATTLE_SFX_PITCH_HIT = 0.08f;
        private const float BATTLE_SFX_PITCH_PROJECTILE = 0.05f;
        private const float BATTLE_SFX_PITCH_SKILL = 0.03f;

        /// <summary>
        /// 피치 랜덤 폭의 상한. 이보다 크게 흔들면 클립의 음정이 바뀐 것으로 들려
        /// 같은 소리로 인지되지 않는다
        /// </summary>
        private const float BATTLE_SFX_PITCH_RANGE_MAX = 0.5f;

        /// <summary>
        /// 전투음 티어별 동시 재생 상한.
        ///
        /// Battle 풀은 AudioSource 를 모든 전투음이 공유한다. 한 종류가 독차지하면
        /// 다른 전투음이 통째로 밀려나고, 같은 클립이 여러 겹 쌓이면 위상 간섭으로
        /// '지직' 하는 콤 필터링 잡음이 난다.
        /// (사망음은 상황별 조정이 필요해 GameClientPlayConfig.Death 에서 따로 지정한다)
        /// </summary>
        private const int BATTLE_SFX_CONCURRENT_HIT = 4;
        private const int BATTLE_SFX_CONCURRENT_PROJECTILE = 3;
        private const int BATTLE_SFX_CONCURRENT_SKILL = 2;

        /// <summary>
        /// 같은 전투음이 이미 울리고 있을 때 하나당 새 소리에 곱해지는 볼륨 배율.
        /// 겹칠수록 합산 음량이 선형으로 커져 찌그러지는 것을 막는다
        /// </summary>
        private const float BATTLE_SFX_ATTENUATION_HIT = 0.8f;
        private const float BATTLE_SFX_ATTENUATION_PROJECTILE = 0.85f;

        /// <summary>
        /// Addressables BGM 번들명
        /// </summary>
        private const string BGM_BUNDLE_NAME = "sound_bgm";

        /// <summary>
        /// Addressables SFX 번들명
        /// </summary>
        private const string SFX_BUNDLE_NAME = "sound_sfx";

        #endregion

        #region Private Fields

        // ===== AudioSource =====
        /// <summary>
        /// BGM 전용 AudioSource
        /// </summary>
        private AudioSource _bgmSource;

        /// <summary>
        /// Battle 효과음용 AudioSource 풀 (Play() 사용, 실시간 제어 가능)
        /// </summary>
        private List<AudioSource> _sfxBattleSourcePool = new List<AudioSource>();

        /// <summary>
        /// UI 효과음용 AudioSource 풀 (PlayOneShot 사용)
        /// </summary>
        private List<AudioSource> _sfxUISourcePool = new List<AudioSource>();

        /// <summary>
        /// 재생 중인 Battle 효과음 한 건의 정보
        /// 볼륨 재계산 시 호출부가 지정한 volumeScale과 Config 조회용 클립 이름이 모두 필요하다.
        /// </summary>
        private struct BattleSFXPlayInfo
        {
            /// <summary>재생 중인 효과음 이름 (Config의 클립별 볼륨 조회용)</summary>
            public string SfxName;

            /// <summary>재생 시 호출부가 지정한 볼륨 배율</summary>
            public float VolumeScale;
        }

        /// <summary>
        /// 재생 중인 Battle 효과음 AudioSource 추적 목록
        /// Key: AudioSource, Value: 재생 시 지정된 이름/volumeScale
        /// (볼륨 재계산 시 원본 값을 보존해야 호출부가 지정한 크기가 유지된다)
        /// </summary>
        private Dictionary<AudioSource, BattleSFXPlayInfo> _playingBattleSources = new Dictionary<AudioSource, BattleSFXPlayInfo>();

        /// <summary>
        /// 재생이 끝난 AudioSource 제거용 임시 버퍼 (Dictionary 순회 중 수정 방지)
        /// </summary>
        private List<AudioSource> _finishedBattleSources = new List<AudioSource>();

        /// <summary>
        /// AudioSource들을 담을 부모 GameObject
        /// </summary>
        private GameObject _audioSourceContainer;

        // ===== 설정 캐시 =====
        /// <summary>
        /// BGM 활성화 여부
        /// </summary>
        private bool _isBGMEnabled = true;

        /// <summary>
        /// 효과음 활성화 여부
        /// </summary>
        private bool _isSFXEnabled = true;

        /// <summary>
        /// PowerSave 모드 활성화 여부
        /// </summary>
        private bool _isPowerSaveMode = false;

        /// <summary>
        /// BGM 마스터 볼륨 (0.0 ~ 1.0)
        /// </summary>
        private float _bgmMasterVolume = 1.0f;

        /// <summary>
        /// 효과음 마스터 볼륨 (0.0 ~ 1.0)
        /// </summary>
        private float _sfxMasterVolume = 1.0f;

        // ===== Battle SFX 제어 =====
        /// <summary>
        /// Battle 사운드 가청 여부 (인게임 화면 가시성 플래그)
        /// </summary>
        private bool _isBattleSoundAudible = true;

        /// <summary>
        /// Battle 효과음 볼륨 배율 (0.0 ~ 1.0)
        /// </summary>
        private float _battleSFXVolumeMultiplier = 1.0f;

        /// <summary>
        /// Battle 사운드 페이드 코루틴 (중복 실행 방지용)
        /// </summary>
        private Coroutine _battleSFXFadeCoroutine = null;

        /// <summary>
        /// Battle 효과음 마지막 재생 시간 추적 (중복 재생 방지용)
        /// Key: SFX 이름, Value: 마지막 재생 시간 (Time.time)
        /// </summary>
        private Dictionary<string, float> _battleSFXLastPlayTime = new Dictionary<string, float>();

        // ===== BGM 상태 =====
        /// <summary>
        /// 현재 재생 중인 BGM 클립 이름
        /// </summary>
        private string _currentBGMName = "";

        /// <summary>
        /// BGM 페이드 진행 중 여부
        /// </summary>
        private bool _isBGMFading = false;

        /// <summary>
        /// 현재 실행 중인 페이드 코루틴 (중복 실행 방지용)
        /// </summary>
        private Coroutine _currentFadeCoroutine = null;

        /// <summary>
        /// 백그라운드 전환 전 BGM 재생 상태
        /// </summary>
        private bool _wasBGMPlayingBeforePause = false;

        /// <summary>
        /// BGM 로딩 중 여부
        /// </summary>
        private bool _isBGMLoading = false;

        // ===== 비디오 모드 =====
        /// <summary>
        /// 비디오 모드 활성화 여부 (비디오 재생 중 BGM/Battle SFX 차단)
        /// </summary>
        private bool _isVideoMode = false;

        /// <summary>
        /// 비디오 모드 중 보류된 BGM 이름
        /// </summary>
        private string _pendingBGMName = "";

        /// <summary>
        /// 비디오 모드 중 보류된 BGM 페이드 시간
        /// </summary>
        private float _pendingBGMFadeTime = 0f;

        /// <summary>
        /// 비디오 모드 진입 전 BGM 재생 중이었는지
        /// </summary>
        private bool _wasBGMPlayingBeforeVideoMode = false;

        /// <summary>
        /// 비디오 모드 진입 전 BGM 이름
        /// </summary>
        private string _bgmNameBeforeVideoMode = "";

        /// <summary>
        /// 비디오 모드 진입 전 Battle SFX 가청 상태
        /// </summary>
        private bool _wasBattleSFXAudibleBeforeVideoMode = true;

        // ===== 오디오 클립 캐시 =====
        /// <summary>
        /// 로드된 BGM 클립 캐시 (Addressables 내부 캐시 활용)
        /// 이 Dictionary는 로딩 상태 추적용
        /// </summary>
        private Dictionary<string, AudioClip> _bgmClipCache = new Dictionary<string, AudioClip>();

        /// <summary>
        /// 로드된 효과음 클립 캐시 (Addressables 내부 캐시 활용)
        /// 이 Dictionary는 로딩 상태 추적용
        /// </summary>
        private Dictionary<string, AudioClip> _sfxClipCache = new Dictionary<string, AudioClip>();

        /// <summary>
        /// 로딩 대기 중인 BGM 요청 큐
        /// </summary>
        private Queue<System.Action> _pendingBGMRequests = new Queue<System.Action>();

        // ===== 볼륨 Config =====
        /// <summary>
        /// 마지막으로 반영한 GameSoundVolumeConfig 개정 번호 (-1이면 아직 반영 전)
        /// </summary>
        private int _lastVolumeConfigRevision = -1;

        #endregion

        #region Unity Lifecycle

        protected override void InitData()
        {
            base.InitData();
        }

        protected override void Awake()
        {
            base.Awake();
            // AudioSource 컨테이너 생성
            CreateAudioSourceContainer();

            // BGM AudioSource 생성
            CreateBGMAudioSource();

            // Battle 효과음 AudioSource 풀 생성
            CreateBattleSFXAudioSourcePool();

            // UI 효과음 AudioSource 풀 생성
            CreateUISFXAudioSourcePool();

            // 설정 로드
            LoadSettings();

            Debug.Log("[GameSoundManager] Awake completed - AudioSources initialized");
        }

        protected override void Start()
        {
            base.Start();
            // 설정 적용
            ApplySettings();

            Debug.Log("[GameSoundManager] Initialized successfully (Addressables)");
        }

        protected override void Update()
        {
            // PowerSave 모드 변경 감지 (매 프레임 체크는 비효율적이므로 필요 시 이벤트 방식으로 변경 권장)
            CheckPowerSaveMode();

            // GameSoundVolumeConfig 실시간 반영
            RefreshVolumeFromConfig();
        }

        void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                // 백그라운드 진입 시
                OnEnterBackground();
            }
            else
            {
                // 백그라운드 복귀 시
                OnExitBackground();
            }
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus)
            {
                // 포커스 잃음
                OnLostFocus();
            }
            else
            {
                // 포커스 복귀
                OnGainFocus();
            }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// AudioSource들을 담을 컨테이너 GameObject 생성
        /// </summary>
        private void CreateAudioSourceContainer()
        {
            _audioSourceContainer = new GameObject("[GameSoundManager_AudioSources]");
            _audioSourceContainer.transform.SetParent(this.transform);
        }

        /// <summary>
        /// BGM 전용 AudioSource 생성
        /// </summary>
        private void CreateBGMAudioSource()
        {
            GameObject bgmObject = new GameObject("BGM_AudioSource");
            bgmObject.transform.SetParent(_audioSourceContainer.transform);

            _bgmSource = bgmObject.AddComponent<AudioSource>();
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;
            _bgmSource.priority = 128; // 낮은 우선순위
            _bgmSource.spatialBlend = 0f; // 2D 사운드
        }

        /// <summary>
        /// Battle 효과음용 AudioSource 풀 생성 (Play() 사용, 실시간 제어 가능)
        /// </summary>
        private void CreateBattleSFXAudioSourcePool()
        {
            for (int i = 0; i < BATTLE_SFX_POOL_SIZE; i++)
            {
                GameObject sfxObject = new GameObject($"BattleSFX_AudioSource_{i}");
                sfxObject.transform.SetParent(_audioSourceContainer.transform);

                AudioSource sfxSource = sfxObject.AddComponent<AudioSource>();
                sfxSource.loop = false;
                sfxSource.playOnAwake = false;
                sfxSource.priority = 64; // 중간 우선순위
                sfxSource.spatialBlend = 0f; // 2D 사운드

                _sfxBattleSourcePool.Add(sfxSource);
            }

            Debug.Log($"[GameSoundManager] Created {BATTLE_SFX_POOL_SIZE} Battle SFX AudioSources");
        }

        /// <summary>
        /// UI 효과음용 AudioSource 풀 생성 (PlayOneShot 사용)
        /// </summary>
        private void CreateUISFXAudioSourcePool()
        {
            for (int i = 0; i < UI_SFX_POOL_SIZE; i++)
            {
                GameObject sfxObject = new GameObject($"UISFX_AudioSource_{i}");
                sfxObject.transform.SetParent(_audioSourceContainer.transform);

                AudioSource sfxSource = sfxObject.AddComponent<AudioSource>();
                sfxSource.loop = false;
                sfxSource.playOnAwake = false;
                sfxSource.priority = 64; // 중간 우선순위
                sfxSource.spatialBlend = 0f; // 2D 사운드

                _sfxUISourcePool.Add(sfxSource);
            }

            Debug.Log($"[GameSoundManager] Created {UI_SFX_POOL_SIZE} UI SFX AudioSources");
        }

        #endregion

        #region Settings Management

        /// <summary>
        /// PlayerPrefs에서 사운드 설정 로드
        /// </summary>
        private void LoadSettings()
        {
            // PXPopup_Setting.ESettingType 활용
            _isBGMEnabled = PlayerPrefs.GetInt(PXPopup_Setting.ESettingType.BGSound.ToString(), 1) == 1;
            _isSFXEnabled = PlayerPrefs.GetInt(PXPopup_Setting.ESettingType.EffectSound.ToString(), 1) == 1;
            _isPowerSaveMode = PlayerPrefs.GetInt(PXPopup_Setting.ESettingType.PowerSave.ToString(), 0) == 1;

        }

        /// <summary>
        /// 로드된 설정 적용
        /// </summary>
        private void ApplySettings()
        {
            // BGM 볼륨 적용
            ApplyBGMVolume();

            // 효과음 볼륨 적용 (풀의 모든 AudioSource에 적용)
            ApplySFXVolume();
        }

        /// <summary>
        /// PowerSave 모드 변경 감지 및 적용
        /// </summary>
        private void CheckPowerSaveMode()
        {
            bool currentPowerSaveMode = PlayerPrefs.GetInt(PXPopup_Setting.ESettingType.PowerSave.ToString(), 0) == 1;

            if (_isPowerSaveMode != currentPowerSaveMode)
            {
                _isPowerSaveMode = currentPowerSaveMode;
                Debug.Log($"[GameSoundManager] PowerSave mode changed: {_isPowerSaveMode}");

                // PowerSave 모드에 따라 BGM 볼륨 조정
                ApplyBGMVolume();
            }
        }

        /// <summary>
        /// 설정 새로고침 (외부에서 설정 변경 시 호출)
        /// </summary>
        public void RefreshSettings()
        {
            LoadSettings();
            ApplySettings();
        }

        /// <summary>
        /// GameSoundVolumeConfig의 값 변경을 재생 중인 사운드에 반영한다.
        /// - 에디터: 인스펙터 슬라이더를 드래그하는 즉시 들리도록 매 프레임 재계산한다
        ///   (재계산 대상은 BGM 1개 + 재생 중인 전투 효과음 최대 BATTLE_SFX_POOL_SIZE 개라 비용이 미미하다)
        /// - 빌드: Config 값이 바뀌지 않으므로 개정 번호가 달라질 때(사실상 최초 1회)만 재계산한다
        ///
        /// UI 효과음은 PlayOneShot으로 재생되어 사후 볼륨 제어가 불가능하므로 다음 재생부터 반영된다.
        /// </summary>
        private void RefreshVolumeFromConfig()
        {
            var config = GameSoundVolumeConfig.Instance;
            if (config == null)
            {
                return;
            }

#if !UNITY_EDITOR
            if (_lastVolumeConfigRevision == config.Revision)
            {
                return;
            }
#endif
            _lastVolumeConfigRevision = config.Revision;

            ApplyBGMVolume();
            UpdateBattleSFXVolume();
        }

        #endregion

        #region BGM Control

        /// <summary>
        /// BGM 재생 (Addressables 비동기 로딩)
        /// </summary>
        /// <param name="bgmName">재생할 BGM 이름 (확장자 제외)</param>
        /// <param name="fadeTime">페이드 인 시간 (초), 0이면 즉시 재생</param>
        /// <param name="forceRestart">이미 재생 중인 BGM을 강제로 재시작할지 여부</param>
        public void PlayBGM(string bgmName, float fadeTime = DEFAULT_BGM_FADE_TIME, bool forceRestart = false)
        {
            if (string.IsNullOrEmpty(bgmName))
            {
                Debug.LogWarning("[GameSoundManager] PlayBGM: bgmName is null or empty");
                return;
            }

            if (_bgmSource == null)
            {
                Debug.LogError("[GameSoundManager] PlayBGM: _bgmSource is null - not initialized");
                return;
            }

            // 비디오 모드일 때는 BGM 요청을 보류
            if (_isVideoMode)
            {
                _pendingBGMName = bgmName;
                _pendingBGMFadeTime = fadeTime;
                Debug.Log($"[GameSoundManager] PlayBGM: Video mode active - BGM '{bgmName}' pending");
                return;
            }

            // 이미 재생 중인 BGM과 동일하면 무시 (forceRestart가 false일 때)
            if (!forceRestart && _currentBGMName == bgmName && _bgmSource.isPlaying)
            {
                Debug.Log($"[GameSoundManager] PlayBGM: {bgmName} is already playing");
                return;
            }

            // BGM 클립 비동기 로드
            LoadBGMClipAsync(bgmName, (bgmClip) =>
            {
                if (bgmClip == null)
                {
                    Debug.LogError($"[GameSoundManager] PlayBGM: Failed to load BGM '{bgmName}'");
                    return;
                }

                // 비동기 로드 완료 후 비디오 모드 재확인 (로드 중 비디오가 시작됐을 수 있음)
                if (_isVideoMode)
                {
                    _pendingBGMName = bgmName;
                    _pendingBGMFadeTime = fadeTime;
                    Debug.Log($"[GameSoundManager] PlayBGM: Video mode active after load - BGM '{bgmName}' pending");
                    return;
                }

                // 기존 페이드 코루틴 중지 (중복 실행 방지)
                if (_currentFadeCoroutine != null)
                {
                    StopCoroutine(_currentFadeCoroutine);
                    _currentFadeCoroutine = null;
                    _isBGMFading = false;
                }

                // 즉시 정지 (코루틴 없이)
                if (_bgmSource.isPlaying)
                {
                    _bgmSource.Stop();
                }

                // 새 BGM 설정
                _bgmSource.clip = bgmClip;
                _currentBGMName = bgmName;

                // 페이드 인 재생
                if (fadeTime > 0f)
                {
                    _currentFadeCoroutine = StartCoroutine(FadeInBGM(fadeTime));
                }
                else
                {
                    // 즉시 재생
                    _bgmSource.volume = CalculateBGMVolume();
                    _bgmSource.Play();
                    Debug.Log($"[GameSoundManager] PlayBGM: {bgmName} started (instant)");
                }
            });
        }

        /// <summary>
        /// BGM 정지
        /// </summary>
        /// <param name="fadeTime">페이드 아웃 시간 (초), 0이면 즉시 정지</param>
        public void StopBGM(float fadeTime = DEFAULT_BGM_FADE_TIME)
        {
            if (!_bgmSource.isPlaying)
            {
                return;
            }

            // 기존 페이드 코루틴 중지 (중복 실행 방지)
            if (_currentFadeCoroutine != null)
            {
                StopCoroutine(_currentFadeCoroutine);
                _currentFadeCoroutine = null;
                _isBGMFading = false;
            }

            if (fadeTime > 0f)
            {
                _currentFadeCoroutine = StartCoroutine(FadeOutBGM(fadeTime));
            }
            else
            {
                _bgmSource.Stop();
                _currentBGMName = "";
                Debug.Log("[GameSoundManager] StopBGM: BGM stopped (instant)");
            }
        }

        /// <summary>
        /// BGM 일시정지
        /// </summary>
        public void PauseBGM()
        {
            if (_bgmSource.isPlaying)
            {
                _bgmSource.Pause();
                Debug.Log("[GameSoundManager] PauseBGM: BGM paused");
            }
        }

        /// <summary>
        /// BGM 재개
        /// </summary>
        public void ResumeBGM()
        {
            if (!_bgmSource.isPlaying && _bgmSource.clip != null)
            {
                _bgmSource.UnPause();
                Debug.Log("[GameSoundManager] ResumeBGM: BGM resumed");
            }
        }

        /// <summary>
        /// BGM 마스터 볼륨 설정 (0.0 ~ 1.0)
        /// </summary>
        /// <param name="volume">볼륨 값</param>
        public void SetBGMMasterVolume(float volume)
        {
            _bgmMasterVolume = Mathf.Clamp01(volume);
            ApplyBGMVolume();
        }

        /// <summary>
        /// BGM 볼륨 적용 (설정 + PowerSave 고려)
        /// </summary>
        private void ApplyBGMVolume()
        {
            if (_bgmSource == null)
            {
                Debug.LogWarning("[GameSoundManager] ApplyBGMVolume: _bgmSource is null");
                return;
            }

            // 페이드 중에는 코루틴이 볼륨을 직접 제어한다.
            // 여기서 덮어쓰면 페이드가 끊기므로 건너뛴다 (코루틴이 매 프레임 최신 값을 반영한다)
            if (_isBGMFading)
            {
                return;
            }

            _bgmSource.volume = CalculateBGMVolume();
        }

        /// <summary>
        /// 최종 BGM 볼륨 계산 (설정 + PowerSave + 마스터 볼륨 + Config 배율)
        /// </summary>
        /// <returns>최종 볼륨 값</returns>
        private float CalculateBGMVolume()
        {
            if (!_isBGMEnabled)
            {
                return 0f;
            }

            // Config 기반 배율 (에셋이 없으면 코드 기본값으로 대체)
            var config = GameSoundVolumeConfig.Instance;
            float configMaster = config != null ? config.bgmMasterVolume : BGM_BASE_VOLUME;
            float clipVolume = config != null ? config.GetClipVolume(_currentBGMName) : 1f;

            float volume = _bgmMasterVolume * configMaster * clipVolume;

            // PowerSave 모드 시 볼륨 감소
            if (_isPowerSaveMode)
            {
                volume *= POWER_SAVE_BGM_VOLUME_RATIO;
            }

            return volume;
        }

        /// <summary>
        /// 현재 재생 중인 BGM 이름 반환
        /// </summary>
        public string GetCurrentBGMName()
        {
            return _currentBGMName;
        }

        /// <summary>
        /// BGM 재생 중인지 확인
        /// </summary>
        public bool IsBGMPlaying()
        {
            return _bgmSource.isPlaying;
        }

        #endregion

        #region BGM Fade

        /// <summary>
        /// BGM 페이드 인 코루틴
        /// </summary>
        /// <param name="fadeTime">페이드 시간 (초)</param>
        private IEnumerator FadeInBGM(float fadeTime)
        {
            _isBGMFading = true;

            _bgmSource.volume = 0f;
            _bgmSource.Play();

            float elapsed = 0f;

            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;

                // 페이드 중 Config 값이 바뀌어도 즉시 반영되도록 목표 볼륨을 매 프레임 다시 구한다
                float targetVolume = CalculateBGMVolume();
                _bgmSource.volume = Mathf.Lerp(0f, targetVolume, elapsed / fadeTime);
                yield return null;
            }

            _bgmSource.volume = CalculateBGMVolume();
            _isBGMFading = false;
            _currentFadeCoroutine = null; // 코루틴 완료

        }

        /// <summary>
        /// BGM 페이드 아웃 코루틴
        /// </summary>
        /// <param name="fadeTime">페이드 시간 (초)</param>
        private IEnumerator FadeOutBGM(float fadeTime)
        {
            _isBGMFading = true;

            float startVolume = _bgmSource.volume;
            float elapsed = 0f;

            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                _bgmSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeTime);
                yield return null;
            }

            _bgmSource.volume = 0f;
            _bgmSource.Stop();
            _currentBGMName = "";
            _isBGMFading = false;
            _currentFadeCoroutine = null; // 코루틴 완료

            Debug.Log("[GameSoundManager] FadeOutBGM: BGM fade out completed");
        }

        #endregion

        #region SFX Control

        /// <summary>
        /// Battle 효과음 재생 (Play() 사용, 실시간 제어 가능) - Addressables 비동기 로딩
        /// </summary>
        /// <param name="sfxName">재생할 효과음 이름 (확장자 제외)</param>
        /// <param name="volumeScale">볼륨 배율 (0.0 ~ 1.0), 기본값 1.0</param>
        /// <param name="minInterval">
        /// 동일 효과음의 최소 재생 간격 (초). 기본값은 BATTLE_SFX_MIN_INTERVAL.
        /// 다발로 발사되는 투사체처럼 짧은 클립이 여러 겹 쌓여 뭉치는 경우
        /// 호출부에서 클립 길이에 맞춰 더 긴 값을 지정한다.
        /// </param>
        /// <param name="maxConcurrent">
        /// 같은 이름의 효과음이 동시에 울릴 수 있는 최대 개수. 0 이하면 제한 없음(기존 동작).
        /// Battle 풀은 AudioSource 를 모든 전투음이 공유하므로, 몬스터 사망처럼
        /// 한꺼번에 쏟아지는 소리는 여기를 걸어 두지 않으면 풀을 독차지해 다른 전투음을 밀어낸다.
        /// </param>
        /// <param name="overlapAttenuation">
        /// 이미 울리고 있는 같은 효과음 하나당 새 소리에 곱해지는 배율 (1이면 감쇠 없음).
        /// 같은 클립이 겹칠수록 합산 음량이 선형으로 커져 찌그러지는 것을 막는다.
        /// </param>
        /// <param name="pitchRandomRange">
        /// 피치를 흔들 폭 (±비율). 0이면 원음 그대로 재생한다.
        /// 티어별 권장값은 BATTLE_SFX_PITCH_* 상수를 쓴다.
        /// </param>
        public void PlaySFX_Battle(string sfxName, float volumeScale = 1.0f, float minInterval = BATTLE_SFX_MIN_INTERVAL,
                                   int maxConcurrent = 0, float overlapAttenuation = 1.0f, float pitchRandomRange = 0f)
        {
            if (string.IsNullOrEmpty(sfxName))
            {
                Debug.LogWarning("[GameSoundManager] PlaySFX_Battle: sfxName is null or empty");
                return;
            }

            // 효과음 비활성화 시 무시
            if (!_isSFXEnabled)
            {
                return;
            }

            // 동일 SFX 중복 재생 방지 (최소 간격 체크)
            float currentTime = Time.time;
            if (_battleSFXLastPlayTime.ContainsKey(sfxName))
            {
                float timeSinceLastPlay = currentTime - _battleSFXLastPlayTime[sfxName];
                if (timeSinceLastPlay < minInterval)
                {
                    // 최소 간격 이내 재생 시도 - 무시
                    return;
                }
            }

            // 마지막 재생 시간 기록 (비동기 로드 시작 전)
            _battleSFXLastPlayTime[sfxName] = currentTime;

            // 효과음 클립 비동기 로드
            LoadSFXClipAsync(sfxName, (sfxClip) =>
            {
                if (sfxClip == null)
                {
                    Debug.LogError($"[GameSoundManager] PlaySFX_Battle: Failed to load SFX '{sfxName}'");
                    return;
                }

                // 같은 효과음이 지금 몇 개나 울리고 있는지 — 동시 개수 제한과 겹침 감쇠 모두 이 값을 쓴다.
                // 클립이 캐시된 뒤로는 이 콜백이 동기 호출이라, 여기서 세는 값이 곧 재생 직전의 실제 상태다
                bool needOverlapCheck = maxConcurrent > 0 || overlapAttenuation < 1.0f;
                int overlapCount = needOverlapCheck ? CountPlayingBattleSFX(sfxName) : 0;

                AudioSource availableSource = null;

                if (maxConcurrent > 0 && overlapCount >= maxConcurrent)
                {
                    // 한도가 찼다. 잔향만 남은 소리가 자리를 붙들고 있는데 그냥 건너뛰면,
                    // 한바탕 몰려 죽은 직후에 떨어져 죽은 한 마리가 통째로 무음이 된다.
                    // 그래서 '충분히 감쇠된' 소리가 있으면 그것을 끊고 자리를 물려받는다
                    availableSource = FindStealableBattleSFX(sfxName);

                    if (availableSource == null)
                    {
                        // 울리고 있는 것이 전부 아직 한창인 소리뿐이다 = 방금 같은 소리가 크게 났다는 뜻이라
                        // 여기서 걸러도 피드백이 비지 않는다. 한창인 소리를 끊으면 그 지점이 '툭' 하고 튄다
                        return;
                    }

                    // 하나를 뺏었으니 이 소리와 함께 울리는 기존 소리는 이만큼 남는다
                    overlapCount = maxConcurrent - 1;
                }

                // 감쇠를 volumeScale 자체에 접어 넣는다. UpdateBattleSFXVolume 이 매 프레임 이 값으로
                // 볼륨을 다시 계산하므로, 따로 두면 재계산 순간 감쇠가 풀려 소리가 커진다
                float finalVolumeScale = volumeScale;

                if (overlapCount > 0 && overlapAttenuation < 1.0f)
                {
                    finalVolumeScale *= Mathf.Pow(Mathf.Clamp01(overlapAttenuation), overlapCount);
                }

                // 뺏을 대상이 없었으면(=한도에 안 걸렸으면) 평소대로 빈 자리를 찾는다
                if (availableSource == null)
                    availableSource = GetAvailableBattleSFXSource();

                if (availableSource == null)
                {
                    Debug.LogWarning("[GameSoundManager] PlaySFX_Battle: No available AudioSource in Battle pool");
                    return;
                }

                // 효과음 재생 (Play() 사용 - 실시간 제어 가능)
                availableSource.clip = sfxClip;
                availableSource.volume = CalculateBattleSFXVolume(sfxName, finalVolumeScale);
                availableSource.mute = !_isBattleSoundAudible; // 가시성 플래그에 따라 mute

                // 풀에서 돌려쓰는 AudioSource 라 직전 재생의 피치가 남아 있다.
                // 흔들 폭이 0이어도 1로 되돌려야 앞 소리의 피치가 이 소리를 오염시키지 않는다.
                // (AudioSource.time 은 피치와 무관한 클립 내 위치라 FindStealableBattleSFX 의 진행도 계산은 그대로 유효하다)
                float pitchRange = Mathf.Clamp(pitchRandomRange, 0f, BATTLE_SFX_PITCH_RANGE_MAX);
                availableSource.pitch = pitchRange > 0f ? 1f + Random.Range(-pitchRange, pitchRange) : 1f;

                availableSource.Play();

                // 재생 중 목록에 추가 (볼륨 재계산 시 쓰도록 이름/volumeScale도 함께 보존)
                _playingBattleSources[availableSource] = new BattleSFXPlayInfo
                {
                    SfxName = sfxName,
                    VolumeScale = finalVolumeScale
                };
            });
        }

        /// <summary>
        /// 히트음 재생 — 초당 수십 회 울리는 가장 흔한 전투음.
        ///
        /// 반복감이 가장 먼저 드러나므로 피치를 제일 크게 흔들고,
        /// 풀을 독차지하지 않도록 동시 개수를 4로 묶는다
        /// </summary>
        public void PlaySFX_BattleHit(string sfxName, float volumeScale = 1.0f, float minInterval = BATTLE_SFX_MIN_INTERVAL)
        {
            PlaySFX_Battle(sfxName, volumeScale, minInterval,
                           BATTLE_SFX_CONCURRENT_HIT, BATTLE_SFX_ATTENUATION_HIT, BATTLE_SFX_PITCH_HIT);
        }

        /// <summary>
        /// 투사체 발사음 재생.
        ///
        /// 히트음보다 빈도가 낮고 음정이 또렷해 크게 흔들면 어색하다.
        /// 다발로 나가는 스킬이 있어 상한은 걸되 히트음보다 한 자리 적게 준다
        /// </summary>
        public void PlaySFX_BattleProjectile(string sfxName, float volumeScale = 1.0f, float minInterval = BATTLE_SFX_MIN_INTERVAL)
        {
            PlaySFX_Battle(sfxName, volumeScale, minInterval,
                           BATTLE_SFX_CONCURRENT_PROJECTILE, BATTLE_SFX_ATTENUATION_PROJECTILE, BATTLE_SFX_PITCH_PROJECTILE);
        }

        /// <summary>
        /// 스킬·오라 발동음 재생.
        ///
        /// 음색 자체가 스킬 정체성이라 피치는 최소만 흔들고 겹침 감쇠도 걸지 않는다.
        /// 대신 상시 발동되는 오라가 풀을 잠식하지 않도록 동시 개수를 2로 가장 좁게 묶는다
        /// </summary>
        public void PlaySFX_BattleSkill(string sfxName, float volumeScale = 1.0f, float minInterval = BATTLE_SFX_MIN_INTERVAL)
        {
            PlaySFX_Battle(sfxName, volumeScale, minInterval,
                           BATTLE_SFX_CONCURRENT_SKILL, 1.0f, BATTLE_SFX_PITCH_SKILL);
        }

        /// <summary>
        /// 같은 이름으로 지금 재생 중인 Battle 효과음 개수를 센다.
        ///
        /// _playingBattleSources 는 재생이 끝나도 UpdateBattleSFXVolume 이 돌기 전까지 항목이 남아 있어
        /// 사전 등록 여부만으로는 판단할 수 없다. AudioSource 를 직접 확인한다.
        /// (키가 풀의 AudioSource 라 항목 수는 풀 크기를 넘지 않는다 — 순회 비용은 무시할 수준)
        /// </summary>
        private int CountPlayingBattleSFX(string InSFXName)
        {
            int playingCount = 0;

            foreach (var pair in _playingBattleSources)
            {
                AudioSource source = pair.Key;

                if (source == null || source.isPlaying == false)
                    continue;

                if (pair.Value.SfxName == InSFXName)
                    playingCount++;
            }

            return playingCount;
        }

        /// <summary>
        /// 동시 개수 한도가 찼을 때 끊고 자리를 물려받을 AudioSource.
        ///
        /// 같은 이름으로 재생 중인 것 중 가장 많이 진행된 것을 고르되,
        /// 진행도가 BATTLE_SFX_STEAL_PROGRESS 에 못 미치면 아무것도 돌려주지 않는다.
        /// 한창 울리는 소리를 끊으면 파형이 그 자리에서 끊겨 '툭' 하는 잡음이 되기 때문이다.
        /// 뒤쪽 잔향 구간에서는 진폭이 피크의 몇 % 수준이라 새 소리의 어택에 그대로 묻힌다
        /// </summary>
        private AudioSource FindStealableBattleSFX(string InSFXName)
        {
            AudioSource oldestSource = null;
            float maxProgress = 0f;

            foreach (var pair in _playingBattleSources)
            {
                AudioSource source = pair.Key;

                if (source == null || source.isPlaying == false)
                    continue;

                if (pair.Value.SfxName != InSFXName)
                    continue;

                if (source.clip == null || source.clip.length <= 0f)
                    continue;

                //같은 클립끼리 비교하므로 재생 위치가 곧 나이다
                float progress = source.time / source.clip.length;

                if (progress > maxProgress)
                {
                    maxProgress = progress;
                    oldestSource = source;
                }
            }

            return maxProgress >= BATTLE_SFX_STEAL_PROGRESS ? oldestSource : null;
        }

        /// <summary>
        /// UI 효과음 재생 (PlayOneShot 사용) - Addressables 비동기 로딩
        /// </summary>
        /// <param name="sfxName">재생할 효과음 이름 (확장자 제외)</param>
        /// <param name="volumeScale">볼륨 배율 (0.0 ~ 1.0), 기본값 1.0</param>        
        public void PlaySFX_UI_Global(string sfxName, float volumeScale = 1.0f)
        {
            if (string.IsNullOrEmpty(sfxName))
            {
                Debug.LogWarning("[GameSoundManager] PlaySFX_UI: sfxName is null or empty");
                return;
            }

            // 효과음 비활성화 시 무시
            if (!_isSFXEnabled)
            {
                return;
            }

            // 효과음 클립 비동기 로드
            LoadSFXClipAsync(sfxName, (sfxClip) =>
            {
                if (sfxClip == null)
                {
                    Debug.LogError($"[GameSoundManager] PlaySFX_UI: Failed to load SFX '{sfxName}'");
                    return;
                }

                // 사용 가능한 UI AudioSource 찾기
                AudioSource availableSource = GetAvailableUISFXSource();
                if (availableSource == null)
                {
                    Debug.LogWarning("[GameSoundManager] PlaySFX_UI: No available AudioSource in UI pool");
                    return;
                }

                // 효과음 재생 (PlayOneShot - 제어 불필요)
                float finalVolume = CalculateUISFXVolume(sfxName, volumeScale);
                availableSource.PlayOneShot(sfxClip, finalVolume);
            });
        }
        /// <summary>
        /// UI 효과음 조건부 재생 - 최상위 UIFull 팝업 또는 그 자식 위젯에서만 재생
        /// </summary>
        /// <param name="InSoundWidget">사운드를 요청하는 위젯</param>
        /// <param name="sfxName">재생할 효과음 이름 (확장자 제외)</param>
        /// <param name="volumeScale">볼륨 배율 (0.0 ~ 1.0), 기본값 1.0</param>
        public void PlaySFX_UI_Local(BaseUserWidget InSoundWidget, string sfxName, float volumeScale = 1.0f)
        {
            if (string.IsNullOrEmpty(sfxName))
            {
                Debug.LogWarning("[GameSoundManager] PlaySFX_UI_Local: sfxName is null or empty");
                return;
            }

            if (InSoundWidget == null)
            {
                Debug.LogWarning("[GameSoundManager] PlaySFX_UI_Local: InSoundWidget is null");
                return;
            }

            // 효과음 비활성화 시 무시
            if (!_isSFXEnabled)
            {
                return;
            }

            // 최상위 UIFull 팝업에서 요청한 사운드인지 확인
            if (!IsWidgetInTopFullScreenPopup(InSoundWidget))
            {
                return;
            }

            // 조건 충족 시 사운드 재생
            LoadSFXClipAsync(sfxName, (sfxClip) =>
            {
                if (sfxClip == null)
                {
                    Debug.LogError($"[GameSoundManager] PlaySFX_UI_Local: Failed to load SFX '{sfxName}'");
                    return;
                }

                AudioSource availableSource = GetAvailableUISFXSource();
                if (availableSource == null)
                {
                    Debug.LogWarning("[GameSoundManager] PlaySFX_UI_Local: No available AudioSource in UI pool");
                    return;
                }

                float finalVolume = CalculateUISFXVolume(sfxName, volumeScale);
                availableSource.PlayOneShot(sfxClip, finalVolume);
            });
        }

        /// <summary>
        /// 위젯이 최상위 노출 팝업이거나 그 자식인지 확인
        /// - UIFull 팝업이 있으면: 최상위 UIFull 팝업 기준
        /// - UIFull 팝업이 없으면: 최상위 Parent 팝업 기준
        /// </summary>
        /// <param name="InWidget">확인할 위젯</param>
        /// <returns>최상위 노출 팝업 또는 그 자식이면 true</returns>
        private bool IsWidgetInTopFullScreenPopup(BaseUserWidget InWidget)
        {
            if (InWidget == null)
                return false;

            var widgetDataList = GameUIManager.Instance.widgetDataList;
            if (widgetDataList == null || widgetDataList.Count == 0)
                return false;

            // 뒤에서부터 탐색하여 최상위 UIFull Parent 팝업 찾기
            UIPopup topFullScreenPopup = null;
            UIPopup topParentPopup = null;

            for (int i = widgetDataList.Count - 1; i >= 0; i--)
            {
                var widgetData = widgetDataList[i];
                if (widgetData.eWidgetType == EWidgetType.Parent && widgetData.widget != null)
                {
                    // 최상위 Parent 팝업 기록 (아직 없으면)
                    if (topParentPopup == null)
                    {
                        topParentPopup = widgetData.widget;
                    }

                    // UIFull 팝업 찾으면 중단
                    if (widgetData.widget.eFullUIScreenType == EUIFullScreenType.UIFull)
                    {
                        topFullScreenPopup = widgetData.widget;
                        break;
                    }
                }
            }

            // 기준 팝업 결정: UIFull 팝업이 있으면 사용, 없으면 최상위 Parent 팝업 사용
            UIPopup referencePopup = topFullScreenPopup ?? topParentPopup;

            if (referencePopup == null)
                return false;

            // InWidget이 기준 팝업이거나 그 자식인지 Transform 계층으로 확인
            return InWidget.transform.IsChildOf(referencePopup.transform);
        }

        /// <summary>
        /// 모든 Battle 효과음 정지
        /// </summary>
        public void StopAllBattleSFX()
        {
            foreach (var sfxSource in _sfxBattleSourcePool)
            {
                sfxSource.Stop();
            }

            _playingBattleSources.Clear();

            Debug.Log("[GameSoundManager] StopAllBattleSFX: All Battle SFX stopped");
        }

        /// <summary>
        /// 모든 UI 효과음 정지
        /// </summary>
        public void StopAllUISFX()
        {
            foreach (var sfxSource in _sfxUISourcePool)
            {
                sfxSource.Stop();
            }

            Debug.Log("[GameSoundManager] StopAllUISFX: All UI SFX stopped");
        }

        /// <summary>
        /// 모든 효과음 정지 (Battle + UI)
        /// </summary>
        public void StopAllSFX()
        {
            StopAllBattleSFX();
            StopAllUISFX();

            Debug.Log("[GameSoundManager] StopAllSFX: All SFX stopped");
        }

        /// <summary>
        /// 효과음 마스터 볼륨 설정 (0.0 ~ 1.0)
        /// </summary>
        /// <param name="volume">볼륨 값</param>
        public void SetSFXMasterVolume(float volume)
        {
            _sfxMasterVolume = Mathf.Clamp01(volume);
            ApplySFXVolume();
        }

        /// <summary>
        /// Battle 사운드 가청 여부 설정 (인게임 화면 가시성 제어)
        /// </summary>
        /// <param name="isAudible">가청 여부 (true: 들림, false: 안 들림)</param>
        /// <param name="fadeDuration">페이드 시간 (초), 0이면 즉시 적용</param>
        public void SetBattleSoundAudible(bool isAudible, float fadeDuration = 0.3f)
        {
            if (_isBattleSoundAudible == isAudible)
                return;

            _isBattleSoundAudible = isAudible;

            // 기존 페이드 코루틴 중지 (중복 실행 방지)
            if (_battleSFXFadeCoroutine != null)
            {
                StopCoroutine(_battleSFXFadeCoroutine);
                _battleSFXFadeCoroutine = null;
                Debug.Log("[GameSoundManager] SetBattleSoundAudible: Stopped previous fade coroutine");
            }

            if (fadeDuration > 0f)
            {
                // 페이드로 자연스럽게 전환
                float targetMultiplier = isAudible ? 1.0f : 0.0f;
                _battleSFXFadeCoroutine = StartCoroutine(FadeBattleSFX(targetMultiplier, fadeDuration));
            }
            else
            {
                // 즉시 mute/unmute
                foreach (var source in _sfxBattleSourcePool)
                {
                    source.mute = !isAudible;
                }

                _battleSFXVolumeMultiplier = isAudible ? 1.0f : 0.0f;

                Debug.Log($"[GameSoundManager] SetBattleSoundAudible: Battle sound {(isAudible ? "enabled" : "disabled")} (instant)");
            }
        }

        public bool IsBattleSoundAudible()
        {
            return _isBattleSoundAudible;
        }

        /// <summary>
        /// 효과음 볼륨 적용 (모든 풀의 AudioSource)
        /// </summary>
        private void ApplySFXVolume()
        {
            // Battle SFX 볼륨 업데이트
            UpdateBattleSFXVolume();

            // UI SFX는 PlayOneShot으로 재생되므로 실시간 볼륨 조절 불필요
        }

        /// <summary>
        /// 재생 중인 Battle 효과음 볼륨 업데이트
        /// </summary>
        private void UpdateBattleSFXVolume()
        {
            _finishedBattleSources.Clear();

            // 재생 중인 Battle 사운드만 업데이트 - 재생 시 지정된 volumeScale을 그대로 유지한다
            foreach (var pair in _playingBattleSources)
            {
                AudioSource source = pair.Key;

                if (source == null || !source.isPlaying)
                {
                    _finishedBattleSources.Add(source);
                    continue;
                }

                source.volume = CalculateBattleSFXVolume(pair.Value.SfxName, pair.Value.VolumeScale);
            }

            // 재생 완료된 소스 제거 (순회 종료 후 처리)
            foreach (var finished in _finishedBattleSources)
            {
                _playingBattleSources.Remove(finished);
            }

            _finishedBattleSources.Clear();
        }

        /// <summary>
        /// Battle 효과음 페이드 코루틴 (볼륨 멀티플라이어 조절)
        /// </summary>
        /// <param name="targetMultiplier">목표 멀티플라이어 (0.0 ~ 1.0)</param>
        /// <param name="duration">페이드 시간 (초)</param>
        private IEnumerator FadeBattleSFX(float targetMultiplier, float duration)
        {
            float startMultiplier = _battleSFXVolumeMultiplier;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _battleSFXVolumeMultiplier = Mathf.Lerp(startMultiplier, targetMultiplier, elapsed / duration);

                // 재생 중인 Battle 사운드들의 volume 업데이트
                UpdateBattleSFXVolume();

                yield return null;
            }

            _battleSFXVolumeMultiplier = targetMultiplier;

            // targetMultiplier가 0이면 mute로 전환 (완전히 소리 끄기)
            if (targetMultiplier == 0f)
            {
                foreach (var source in _sfxBattleSourcePool)
                {
                    source.mute = true;
                }
            }
            else
            {
                foreach (var source in _sfxBattleSourcePool)
                {
                    source.mute = false;
                }
            }

            _battleSFXFadeCoroutine = null; // 코루틴 완료

        }

        /// <summary>
        /// UI 효과음 최종 볼륨 계산
        /// (마스터 볼륨 × Config 효과음 마스터 × Config UI 카테고리 × Config 클립 볼륨 × 호출부 배율)
        /// </summary>
        /// <param name="sfxName">효과음 이름 (Config의 클립별 볼륨 조회용)</param>
        /// <param name="volumeScale">호출부가 지정한 볼륨 배율</param>
        /// <returns>최종 볼륨 값</returns>
        private float CalculateUISFXVolume(string sfxName, float volumeScale = 1.0f)
        {
            if (!_isSFXEnabled)
            {
                return 0f;
            }

            var config = GameSoundVolumeConfig.Instance;
            float configVolume = config != null
                ? config.sfxMasterVolume * config.uiSFXCategoryVolume * config.GetClipVolume(sfxName)
                : 1f;

            return _sfxMasterVolume * configVolume * Mathf.Clamp01(volumeScale);
        }

        /// <summary>
        /// Battle 효과음 최종 볼륨 계산
        /// (마스터 볼륨 × Config 효과음 마스터 × Config 전투 카테고리 × Config 클립 볼륨 × 호출부 배율 × Battle 멀티플라이어)
        /// </summary>
        /// <param name="sfxName">효과음 이름 (Config의 클립별 볼륨 조회용)</param>
        /// <param name="volumeScale">호출부가 지정한 볼륨 배율 (0.0 ~ 1.0)</param>
        /// <returns>최종 볼륨</returns>
        private float CalculateBattleSFXVolume(string sfxName, float volumeScale = 1.0f)
        {
            if (!_isSFXEnabled)
            {
                return 0f;
            }

            var config = GameSoundVolumeConfig.Instance;
            float configVolume = config != null
                ? config.sfxMasterVolume * config.battleSFXCategoryVolume * config.GetClipVolume(sfxName)
                : 1f;

            return _sfxMasterVolume * configVolume * Mathf.Clamp01(volumeScale) * _battleSFXVolumeMultiplier;
        }

        /// <summary>
        /// 사용 가능한 Battle 효과음 AudioSource 찾기
        /// </summary>
        /// <returns>사용 가능한 AudioSource, 없으면 null</returns>
        private AudioSource GetAvailableBattleSFXSource()
        {
            // 재생 중이지 않은 AudioSource 찾기
            foreach (var sfxSource in _sfxBattleSourcePool)
            {
                if (!sfxSource.isPlaying)
                {
                    return sfxSource;
                }
            }

            // 모든 AudioSource가 사용 중이면 가장 먼저 끝날 것을 반환
            AudioSource earliestSource = _sfxBattleSourcePool[0];
            float minTime = earliestSource.time;

            foreach (var sfxSource in _sfxBattleSourcePool)
            {
                if (sfxSource.time < minTime)
                {
                    minTime = sfxSource.time;
                    earliestSource = sfxSource;
                }
            }

            return earliestSource;
        }

        /// <summary>
        /// 사용 가능한 UI 효과음 AudioSource 찾기
        /// </summary>
        /// <returns>사용 가능한 AudioSource, 없으면 null</returns>
        private AudioSource GetAvailableUISFXSource()
        {
            // 재생 중이지 않은 AudioSource 찾기
            foreach (var sfxSource in _sfxUISourcePool)
            {
                if (!sfxSource.isPlaying)
                {
                    return sfxSource;
                }
            }

            // 모든 AudioSource가 사용 중이면 가장 먼저 끝날 것을 반환
            AudioSource earliestSource = _sfxUISourcePool[0];
            float minTime = earliestSource.time;

            foreach (var sfxSource in _sfxUISourcePool)
            {
                if (sfxSource.time < minTime)
                {
                    minTime = sfxSource.time;
                    earliestSource = sfxSource;
                }
            }

            return earliestSource;
        }

        /// <summary>
        /// 사용 가능한 효과음 AudioSource 찾기 (호환성 유지 - Battle 풀 사용)
        /// </summary>
        /// <returns>사용 가능한 AudioSource, 없으면 null</returns>
        [System.Obsolete("Use GetAvailableBattleSFXSource or GetAvailableUISFXSource instead")]
        private AudioSource GetAvailableSFXSource()
        {
            // 호환성 유지 - Battle 풀로 리다이렉트
            return GetAvailableBattleSFXSource();
        }

        #endregion

        #region Audio Clip Loading (Addressables)

        /// <summary>
        /// BGM 클립 비동기 로드 (Addressables + 캐싱)
        /// </summary>
        /// <param name="bgmName">BGM 이름 (확장자 제외)</param>
        /// <param name="onComplete">로드 완료 콜백</param>
        private void LoadBGMClipAsync(string bgmName, System.Action<AudioClip> onComplete)
        {
            // 캐시에 있으면 즉시 반환
            if (_bgmClipCache.ContainsKey(bgmName))
            {
                onComplete?.Invoke(_bgmClipCache[bgmName]);
                return;
            }

            // Addressables를 통한 비동기 로드
            GameAssetBundleManager.Instance.LoadFromFileAsync<AudioClip>(
                BGM_BUNDLE_NAME,
                bgmName,
                (clip) =>
                {
                    if (clip != null)
                    {
                        _bgmClipCache[bgmName] = clip;
                        onComplete?.Invoke(clip);
                    }
                    else
                    {
                        Debug.LogError($"[GameSoundManager] LoadBGMClipAsync: Failed to load '{BGM_BUNDLE_NAME}/{bgmName}'");
                        onComplete?.Invoke(null);
                    }
                }
            );
        }

        /// <summary>
        /// 효과음 클립 비동기 로드 (Addressables + 캐싱)
        /// </summary>
        /// <param name="sfxName">효과음 이름 (확장자 제외)</param>
        /// <param name="onComplete">로드 완료 콜백</param>
        private void LoadSFXClipAsync(string sfxName, System.Action<AudioClip> onComplete)
        {
            // 캐시에 있으면 즉시 반환
            if (_sfxClipCache.ContainsKey(sfxName))
            {
                onComplete?.Invoke(_sfxClipCache[sfxName]);
                return;
            }

            // Addressables를 통한 비동기 로드
            GameAssetBundleManager.Instance.LoadFromFileAsync<AudioClip>(
                SFX_BUNDLE_NAME,
                sfxName,
                (clip) =>
                {
                    if (clip != null)
                    {
                        _sfxClipCache[sfxName] = clip;
                        onComplete?.Invoke(clip);
                    }
                    else
                    {
                        Debug.LogError($"[GameSoundManager] LoadSFXClipAsync: Failed to load '{SFX_BUNDLE_NAME}/{sfxName}'");
                        onComplete?.Invoke(null);
                    }
                }
            );
        }

        /// <summary>
        /// 효과음 클립을 미리 받아 캐시에 올려 둔다.
        ///
        /// PlaySFX_Battle 은 클립이 캐시에 없으면 Addressables 비동기 로드를 기다렸다 재생한다.
        /// 몬스터 사망음처럼 첫 발이 몰려서 오는 소리는 그 지연이 그대로 타격감 밀림으로 들리므로
        /// 전투 시작 시 한 번 불러 둔다 (이미 캐시에 있으면 아무것도 하지 않는다)
        /// </summary>
        /// <param name="sfxName">효과음 이름 (확장자 제외)</param>
        public void PreloadSFX(string sfxName)
        {
            if (string.IsNullOrEmpty(sfxName))
                return;

            if (_sfxClipCache.ContainsKey(sfxName))
                return;

            LoadSFXClipAsync(sfxName, null);
        }

        /// <summary>
        /// 캐시된 오디오 클립 제거
        /// Addressables 캐시는 GameAssetBundleManager에서 관리하므로
        /// 여기서는 로컬 캐시만 클리어
        /// </summary>
        public void ClearAudioClipCache()
        {
            _bgmClipCache.Clear();
            _sfxClipCache.Clear();

            Debug.Log("[GameSoundManager] ClearAudioClipCache: Local cache cleared (Addressables cache remains)");
        }

        /// <summary>
        /// Addressables 에셋 해제 (메모리 확보용)
        /// </summary>
        /// <param name="bgmName">해제할 BGM 이름</param>
        public void ReleaseBGM(string bgmName)
        {
            if (_bgmClipCache.ContainsKey(bgmName))
            {
                GameAssetBundleManager.Instance.ReleaseAsset(BGM_BUNDLE_NAME, bgmName);
                _bgmClipCache.Remove(bgmName);
                Debug.Log($"[GameSoundManager] ReleaseBGM: Released '{bgmName}'");
            }
        }

        /// <summary>
        /// Addressables 에셋 해제 (메모리 확보용)
        /// </summary>
        /// <param name="sfxName">해제할 효과음 이름</param>
        public void ReleaseSFX(string sfxName)
        {
            if (_sfxClipCache.ContainsKey(sfxName))
            {
                GameAssetBundleManager.Instance.ReleaseAsset(SFX_BUNDLE_NAME, sfxName);
                _sfxClipCache.Remove(sfxName);
                Debug.Log($"[GameSoundManager] ReleaseSFX: Released '{sfxName}'");
            }
        }

        #endregion

        #region Background/Focus Handling

        /// <summary>
        /// 백그라운드 진입 시 처리
        /// </summary>
        private void OnEnterBackground()
        {
            // 현재 BGM 재생 상태 저장
            _wasBGMPlayingBeforePause = _bgmSource.isPlaying;

            // BGM 일시정지
            if (_wasBGMPlayingBeforePause)
            {
                PauseBGM();
            }

            // 모든 효과음 정지
            StopAllSFX();

            Debug.Log("[GameSoundManager] OnEnterBackground: Audio paused");
        }

        /// <summary>
        /// 백그라운드 복귀 시 처리
        /// </summary>
        private void OnExitBackground()
        {
            // BGM 설정 확인 후 재개
            LoadSettings(); // 설정이 변경되었을 수 있으므로 다시 로드

            if (_isBGMEnabled && _wasBGMPlayingBeforePause)
            {
                ResumeBGM();
                ApplyBGMVolume(); // 볼륨 재적용
            }

        }

        /// <summary>
        /// 포커스 잃음 시 처리
        /// </summary>
        private void OnLostFocus()
        {
            // 백그라운드 진입과 동일한 처리
            // 필요 시 별도 로직 추가 가능
        }

        /// <summary>
        /// 포커스 복귀 시 처리
        /// </summary>
        private void OnGainFocus()
        {
            // 백그라운드 복귀와 동일한 처리
            // 필요 시 별도 로직 추가 가능
        }

        #endregion

        #region Video Mode Control

        /// <summary>
        /// 비디오 모드 설정 (비디오 재생 중 BGM/Battle SFX 차단)
        /// - enabled=true: BGM 일시정지, Battle SFX 음소거, 이후 BGM 요청 보류
        /// - enabled=false: 보류된 BGM 처리 또는 기존 BGM 재개, Battle SFX 복원
        /// </summary>
        /// <param name="enabled">비디오 모드 활성화 여부</param>
        public void SetVideoMode(bool enabled)
        {
            if (_isVideoMode == enabled)
                return;

            _isVideoMode = enabled;

            if (enabled)
            {
                // 비디오 모드 진입
                EnterVideoMode();
            }
            else
            {
                // 비디오 모드 종료
                ExitVideoMode();
            }

            Debug.Log($"[GameSoundManager] SetVideoMode: {enabled}");
        }

        /// <summary>
        /// 비디오 모드 진입 처리
        /// </summary>
        private void EnterVideoMode()
        {
            // 현재 상태 저장
            _wasBGMPlayingBeforeVideoMode = _bgmSource.isPlaying;
            _bgmNameBeforeVideoMode = _currentBGMName;
            _wasBattleSFXAudibleBeforeVideoMode = _isBattleSoundAudible;

            // 보류 상태 초기화
            _pendingBGMName = "";
            _pendingBGMFadeTime = 0f;

            // BGM 일시정지
            if (_bgmSource.isPlaying)
            {
                _bgmSource.Pause();
                Debug.Log("[GameSoundManager] EnterVideoMode: BGM paused");
            }

            // Battle SFX 음소거
            if (_wasBattleSFXAudibleBeforeVideoMode)
            {
                SetBattleSoundAudible(false, 0.3f);
            }

            Debug.Log($"[GameSoundManager] EnterVideoMode - BGM was playing: {_wasBGMPlayingBeforeVideoMode}, BGM name: {_bgmNameBeforeVideoMode}, Battle SFX was audible: {_wasBattleSFXAudibleBeforeVideoMode}");
        }

        /// <summary>
        /// 비디오 모드 종료 처리
        /// </summary>
        private void ExitVideoMode()
        {
            // 보류된 BGM이 있으면 재생
            if (!string.IsNullOrEmpty(_pendingBGMName))
            {
                Debug.Log($"[GameSoundManager] ExitVideoMode: Playing pending BGM '{_pendingBGMName}'");
                string pendingName = _pendingBGMName;
                float pendingFade = _pendingBGMFadeTime;
                _pendingBGMName = "";
                _pendingBGMFadeTime = 0f;

                // 보류된 BGM 재생
                PlayBGM(pendingName, pendingFade);
            }
            // 보류된 BGM이 없고, 기존에 재생 중이었으면 재개
            else if (_wasBGMPlayingBeforeVideoMode)
            {
                _bgmSource.UnPause();
                Debug.Log($"[GameSoundManager] ExitVideoMode: BGM resumed '{_bgmNameBeforeVideoMode}'");
            }

            // Battle SFX 복원
            if (_wasBattleSFXAudibleBeforeVideoMode)
            {
                SetBattleSoundAudible(true, 0.3f);
            }

            Debug.Log($"[GameSoundManager] ExitVideoMode - Pending BGM was: {_pendingBGMName}, Restored Battle SFX: {_wasBattleSFXAudibleBeforeVideoMode}");
        }

        /// <summary>
        /// 비디오 모드 활성화 여부 반환
        /// </summary>
        public bool IsVideoMode()
        {
            return _isVideoMode;
        }

        #endregion

        #region Public Utility

        /// <summary>
        /// BGM 활성화 여부 반환
        /// </summary>
        public bool IsBGMEnabled()
        {
            return _isBGMEnabled;
        }

        /// <summary>
        /// 효과음 활성화 여부 반환
        /// </summary>
        public bool IsSFXEnabled()
        {
            return _isSFXEnabled;
        }

        /// <summary>
        /// PowerSave 모드 활성화 여부 반환
        /// </summary>
        public bool IsPowerSaveMode()
        {
            return _isPowerSaveMode;
        }

        /// <summary>
        /// 현재 BGM 마스터 볼륨 반환
        /// </summary>
        public float GetBGMMasterVolume()
        {
            return _bgmMasterVolume;
        }

        /// <summary>
        /// 현재 효과음 마스터 볼륨 반환
        /// </summary>
        public float GetSFXMasterVolume()
        {
            return _sfxMasterVolume;
        }

        /// <summary>
        /// BGM 로딩 중 여부 반환
        /// </summary>
        public bool IsBGMLoading()
        {
            return _isBGMLoading;
        }

        #endregion
    }
}

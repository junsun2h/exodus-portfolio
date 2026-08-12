// 사운드 브라우저 — 에디터 프리뷰 재생 상태 관리
// AudioUtil은 "무엇을 재생 중인지"를 알려주지 않으므로, 현재 클립과 일시정지 여부를 여기서 들고 있는다.

using UnityEditor;
using UnityEngine;

namespace PX.SoundBrowser
{
    /// <summary>
    /// 플레이 모드에 들어가지 않고 오디오 클립을 재생한다.
    /// </summary>
    public sealed class SoundPreviewPlayer
    {
        private AudioClip _clip;

        /// <summary>현재 올려둔 클립의 GUID (재생이 끝나도 유지된다)</summary>
        public string CurrentGuid { get; private set; }

        /// <summary>일시정지 상태인지</summary>
        public bool IsPaused { get; private set; }

        /// <summary>반복 재생 여부</summary>
        public bool Loop { get; set; }

        /// <summary>에디터 프리뷰 재생 API를 쓸 수 있는지</summary>
        public static bool IsAvailable => SoundEditorAudio.IsAvailable;

        /// <summary>실제로 소리가 나고 있는지</summary>
        public bool IsPlaying => !IsPaused && _clip != null && SoundEditorAudio.IsPlaying();

        /// <summary>현재 올려둔 클립의 길이(초)</summary>
        public float ClipLength => _clip != null ? _clip.length : 0f;

        /// <summary>재생 위치(초)</summary>
        public float Position
        {
            get
            {
                if (_clip == null) return 0f;

                float position = SoundEditorAudio.GetPosition();
                return Mathf.Clamp(position, 0f, _clip.length);
            }
        }

        /// <summary>재생 진행도 (0~1)</summary>
        public float NormalizedPosition
        {
            get
            {
                float length = ClipLength;
                return length > 0f ? Mathf.Clamp01(Position / length) : 0f;
            }
        }

        #region 재생 제어

        /// <summary>
        /// 클립을 처음부터(또는 지정 위치부터) 재생한다.
        /// </summary>
        /// <param name="guid">클립 에셋의 GUID (현재 선택 추적용)</param>
        /// <param name="clip">재생할 클립</param>
        /// <param name="normalizedStart">시작 위치 (0~1)</param>
        public void Play(string guid, AudioClip clip, float normalizedStart = 0f)
        {
            if (clip == null) return;

            // 압축 클립은 데이터가 올라와 있지 않으면 무음으로 재생될 수 있다
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                clip.LoadAudioData();
            }

            SoundEditorAudio.Stop();

            _clip = clip;
            CurrentGuid = guid;
            IsPaused = false;

            int startSample = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp01(normalizedStart) * clip.samples), 0, Mathf.Max(0, clip.samples - 1));

            SoundEditorAudio.Play(clip, startSample, Loop);
            SoundEditorAudio.SetLoop(Loop);
        }

        /// <summary>재생을 멈추고 올려둔 클립을 내린다.</summary>
        public void Stop()
        {
            SoundEditorAudio.Stop();
            _clip = null;
            CurrentGuid = null;
            IsPaused = false;
        }

        /// <summary>재생만 멈춘다 (선택은 유지).</summary>
        public void StopPlayback()
        {
            SoundEditorAudio.Stop();
            IsPaused = false;
        }

        /// <summary>일시정지 / 재개를 전환한다.</summary>
        public void TogglePause()
        {
            if (_clip == null) return;

            if (IsPaused)
            {
                SoundEditorAudio.Resume();
                IsPaused = false;
                return;
            }

            if (SoundEditorAudio.IsPlaying())
            {
                SoundEditorAudio.Pause();
                IsPaused = true;
            }
        }

        /// <summary>반복 설정을 바꾸고 재생 중이면 즉시 반영한다.</summary>
        public void SetLoop(bool loop)
        {
            Loop = loop;
            if (_clip != null)
            {
                SoundEditorAudio.SetLoop(loop);
            }
        }

        /// <summary>
        /// 재생 위치를 옮긴다. 멈춰 있었다면 그 지점부터 다시 재생한다.
        /// </summary>
        /// <param name="normalized">0~1 위치</param>
        public void Seek(float normalized)
        {
            if (_clip == null) return;

            bool wasPlaying = SoundEditorAudio.IsPlaying();
            int sample = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp01(normalized) * _clip.samples), 0, Mathf.Max(0, _clip.samples - 1));

            if (wasPlaying)
            {
                SoundEditorAudio.SetSamplePosition(_clip, sample);
                IsPaused = false;
                return;
            }

            // 멈춘 상태에서의 위치 이동은 재생 중이 아니면 반영되지 않으므로 그 지점부터 다시 튼다
            Play(CurrentGuid, _clip, normalized);
        }

        /// <summary>현재 클립을 잡아둔 참조를 놓는다 (윈도우 종료 시 호출).</summary>
        public void Dispose()
        {
            SoundEditorAudio.Stop();
            _clip = null;
            CurrentGuid = null;
            IsPaused = false;
        }

        #endregion

        #region 에디터 음소거

        /// <summary>에디터 전체 오디오 음소거 상태</summary>
        public static bool MasterMute
        {
            get => EditorUtility.audioMasterMute;
            set => EditorUtility.audioMasterMute = value;
        }

        #endregion
    }
}

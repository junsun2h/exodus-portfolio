// 사운드 브라우저 — 파형 배치 분석
// EditorApplication.update에 얹어 프레임 예산 안에서 조금씩 처리한다.
// 수백 개를 한 번에 돌리면 긴 BGM에서 에디터가 몇 초씩 멈추므로, 중단 가능한 점진 처리로 만든다.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PX.SoundBrowser
{
    /// <summary>
    /// 오디오 클립의 파형을 순차적으로 분석하는 백그라운드 러너.
    /// </summary>
    public sealed class SoundAnalysisBatcher
    {
        /// <summary>한 프레임에 쓸 최대 시간(ms). 초과하면 다음 프레임으로 넘긴다.</summary>
        private const double FrameBudgetMs = 30.0;

        /// <summary>이 건수마다 로드한 클립을 정리한다. 긴 클립이 쌓이면 메모리가 크게 는다.</summary>
        private const int UnloadInterval = 40;

        private readonly List<SoundEntry> _queue = new List<SoundEntry>();
        private int _index;
        private int _sinceLastUnload;
        private readonly Stopwatch _frameTimer = new Stopwatch();

        #region 상태

        /// <summary>실행 중인지</summary>
        public bool IsRunning { get; private set; }

        /// <summary>전체 대상 건수</summary>
        public int Total => _queue.Count;

        /// <summary>처리 완료 건수</summary>
        public int Processed { get; private set; }

        /// <summary>파형을 얻지 못한 건수</summary>
        public int Failed { get; private set; }

        /// <summary>0~1 진행률</summary>
        public float Progress => _queue.Count == 0 ? 0f : (float)_index / _queue.Count;

        /// <summary>현재 처리 중인 클립 이름</summary>
        public string CurrentName { get; private set; } = string.Empty;

        /// <summary>진행 상황이 갱신될 때 발생 (윈도우 Repaint용)</summary>
        public event Action OnProgressChanged;

        /// <summary>전체 완료 또는 중단 시 발생</summary>
        public event Action OnFinished;

        #endregion

        #region 실행 제어

        /// <summary>배치 분석을 시작한다.</summary>
        /// <param name="targets">분석할 엔트리 목록</param>
        public void Start(IEnumerable<SoundEntry> targets)
        {
            Stop();

            _queue.Clear();
            _queue.AddRange(targets);

            if (_queue.Count == 0)
            {
                return;
            }

            _index = 0;
            Processed = 0;
            Failed = 0;
            _sinceLastUnload = 0;
            CurrentName = string.Empty;

            IsRunning = true;
            EditorApplication.update += Tick;
        }

        /// <summary>배치 분석을 중단한다.</summary>
        public void Stop()
        {
            if (!IsRunning) return;

            EditorApplication.update -= Tick;
            IsRunning = false;
            CurrentName = string.Empty;

            OnFinished?.Invoke();
        }

        #endregion

        #region 처리 루프

        private void Tick()
        {
            _frameTimer.Restart();

            bool processedAny = false;

            while (_index < _queue.Count)
            {
                // 최소 1건은 처리해 진행이 멈추지 않도록 한다
                if (processedAny && _frameTimer.Elapsed.TotalMilliseconds >= FrameBudgetMs)
                {
                    break;
                }

                var entry = _queue[_index];
                CurrentName = entry.Name;

                if (!AnalyzeSingle(entry))
                {
                    Failed++;
                }

                _index++;
                Processed++;
                _sinceLastUnload++;
                processedAny = true;

                if (_sinceLastUnload >= UnloadInterval)
                {
                    _sinceLastUnload = 0;
                    EditorUtility.UnloadUnusedAssetsImmediate();
                }
            }

            _frameTimer.Stop();
            OnProgressChanged?.Invoke();

            if (_index >= _queue.Count)
            {
                Stop();
            }
        }

        /// <summary>클립 1건의 파형을 뽑아 엔트리에 채운다.</summary>
        /// <returns>파형을 얻었으면 true</returns>
        public static bool AnalyzeSingle(SoundEntry entry)
        {
            try
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(entry.Path);
                if (clip == null)
                {
                    // 파일이 사라졌다. 다시 시도해도 마찬가지이므로 분석 완료로 표시해 큐에서 빼둔다.
                    entry.Analyzed = true;
                    entry.SetEnvelope(null);
                    return false;
                }

                // 인덱스 빌드 때 클립을 못 읽었을 수 있으므로 이 김에 메타도 보정한다
                if (!entry.HasMeta)
                {
                    entry.Length = clip.length;
                    entry.Channels = clip.channels;
                    entry.Frequency = clip.frequency;
                    entry.Samples = clip.samples;
                }

                var envelope = SoundWaveform.ExtractEnvelope(
                    clip, SoundWaveform.ListResolution, out float peak, out float rms);

                entry.Analyzed = true;
                entry.SetEnvelope(envelope);
                entry.PeakLevel = peak;
                entry.RmsLevel = rms;

                SoundWaveformCache.Invalidate(entry.Guid);
                return envelope != null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SoundBrowser] 파형 분석 실패: {entry.Path} — {e.Message}");
                entry.Analyzed = true;
                return false;
            }
        }

        #endregion
    }
}

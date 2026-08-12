// 이펙트 브라우저 — 썸네일 배치 캡처
// EditorApplication.update에 얹어 프레임 예산 안에서 조금씩 처리한다.
// 수천 건을 한 번에 돌리면 에디터가 수십 분 얼어붙으므로, 중단 가능한 점진 처리로 만든다.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PX.EffectBrowser
{
    /// <summary>
    /// 이펙트 썸네일을 순차적으로 캡처하는 백그라운드 러너.
    /// </summary>
    public sealed class EffectThumbnailBatcher
    {
        /// <summary>한 프레임에 쓸 최대 시간(ms). 초과하면 다음 프레임으로 넘긴다.</summary>
        private const double FrameBudgetMs = 40.0;

        /// <summary>이 건수마다 미사용 에셋을 정리한다. 프리팹 로드가 누적되면 메모리가 계속 늘어난다.</summary>
        private const int UnloadInterval = 150;

        private readonly List<EffectEntry> _queue = new List<EffectEntry>();
        private EffectPreviewRenderer _renderer;
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

        /// <summary>캡처에 실패한 건수</summary>
        public int Failed { get; private set; }

        /// <summary>0~1 진행률</summary>
        public float Progress => _queue.Count == 0 ? 0f : (float)_index / _queue.Count;

        /// <summary>현재 처리 중인 이펙트 이름</summary>
        public string CurrentName { get; private set; } = string.Empty;

        /// <summary>진행 상황이 갱신될 때 발생 (윈도우 Repaint용)</summary>
        public event Action OnProgressChanged;

        /// <summary>전체 완료 또는 중단 시 발생</summary>
        public event Action OnFinished;

        #endregion

        #region 실행 제어

        /// <summary>
        /// 배치 캡처를 시작한다.
        /// </summary>
        /// <param name="targets">캡처할 엔트리 목록</param>
        public void Start(IEnumerable<EffectEntry> targets)
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

            _renderer = new EffectPreviewRenderer();
            IsRunning = true;

            EditorApplication.update += Tick;
        }

        /// <summary>배치 캡처를 중단하고 자원을 정리한다.</summary>
        public void Stop()
        {
            if (!IsRunning && _renderer == null) return;

            EditorApplication.update -= Tick;
            IsRunning = false;

            _renderer?.Dispose();
            _renderer = null;

            CurrentName = string.Empty;

            OnFinished?.Invoke();
        }

        #endregion

        #region 처리 루프

        private void Tick()
        {
            // 플레이 모드로 들어가면 프리뷰 씬 렌더가 불안정해지므로 중단한다
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Stop();
                return;
            }

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

                if (!CaptureSingle(entry))
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

        /// <summary>이펙트 1건을 캡처해 디스크에 저장한다.</summary>
        private bool CaptureSingle(EffectEntry entry)
        {
            Texture2D texture = null;

            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Path);
                if (prefab == null)
                {
                    return false;
                }

                if (!_renderer.SetTarget(prefab))
                {
                    return false;
                }

                // 파티클이 가장 많이 살아 있는 순간을 찾아 그 시점을 담는다
                float bestTime = _renderer.FindBestThumbnailTime();
                _renderer.SimulateTo(bestTime);

                // 파티클이 퍼진 상태의 바운드로 화각을 잡아야 이펙트가 화면에 꽉 찬다
                _renderer.FrameCamera();

                texture = _renderer.RenderStatic(EffectThumbnailCache.ThumbnailSize);
                if (texture == null)
                {
                    return false;
                }

                EffectThumbnailCache.Save(entry.Guid, texture);

                entry.Captured = true;
                entry.ParticleSystemCount = _renderer.ParticleSystemCount;
                entry.Duration = _renderer.EstimatedDuration;
                entry.HasAnimator = _renderer.HasAnimator;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EffectBrowser] 썸네일 캡처 실패: {entry.Path} — {e.Message}");
                return false;
            }
            finally
            {
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                _renderer?.ClearTarget();
            }
        }

        #endregion
    }
}

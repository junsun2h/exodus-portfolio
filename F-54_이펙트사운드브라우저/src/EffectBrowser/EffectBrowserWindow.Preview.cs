// 이펙트 브라우저 — 실시간 재생 프리뷰 패널
// 선택한 이펙트를 격리된 프리뷰 씬에서 실제로 재생한다. 씬을 열거나 플레이 모드에 들어갈 필요가 없다.

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PX.EffectBrowser
{
    public partial class EffectBrowserWindow
    {
        #region 상태

        private EffectPreviewRenderer _livePreview;
        private bool _playing;
        private bool _loopPlayback = true;
        private float _playTime;
        private float _playSpeed = 1f;
        private double _lastPreviewUpdate;

        #endregion

        #region 프리뷰 대상

        private void EnsurePreviewRenderer()
        {
            if (_livePreview == null)
            {
                _livePreview = new EffectPreviewRenderer();
            }
        }

        /// <summary>선택된 이펙트를 프리뷰에 올리고 처음부터 재생한다.</summary>
        private void LoadPreviewTarget(EffectEntry entry)
        {
            EnsurePreviewRenderer();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Path);
            if (prefab == null)
            {
                _livePreview.ClearTarget();
                _playing = false;
                return;
            }

            if (!_livePreview.SetTarget(prefab))
            {
                _playing = false;
                return;
            }

            // 파티클이 가장 퍼진 시점을 기준으로 화각을 확정한 뒤 처음으로 되감는다.
            // 이렇게 해야 재생 내내 카메라가 흔들리지 않고 이펙트가 화면에 들어온다.
            float peakTime = _livePreview.FindBestThumbnailTime();
            _livePreview.SimulateTo(peakTime);
            _livePreview.FrameCamera();
            _livePreview.ResetSimulation();

            _playTime = 0f;
            _playing = true;
            _lastPreviewUpdate = EditorApplication.timeSinceStartup;
        }

        private void StopPlayback()
        {
            _playing = false;
        }

        /// <summary>재생을 처음으로 되감고 다시 시작한다.</summary>
        private void RestartPlayback()
        {
            if (_livePreview == null || !_livePreview.HasTarget) return;

            _playTime = 0f;
            _livePreview.ResetSimulation();
            _playing = true;
            _lastPreviewUpdate = EditorApplication.timeSinceStartup;
        }

        private void DisposePreview()
        {
            _livePreview?.Dispose();
            _livePreview = null;
            _playing = false;
        }

        #endregion

        #region 재생 루프

        private void OnEditorUpdate()
        {
            // 배치 캡처가 도는 동안에는 프리뷰 렌더를 쉬어 캡처 속도를 확보한다
            if (_batcher.IsRunning) return;
            if (!_playing || _livePreview == null || !_livePreview.HasTarget) return;

            double now = EditorApplication.timeSinceStartup;
            float delta = Mathf.Clamp((float)(now - _lastPreviewUpdate), 0f, 0.1f);
            _lastPreviewUpdate = now;

            float scaled = delta * _playSpeed;
            _playTime += scaled;

            float duration = _livePreview.EstimatedDuration;
            if (_playTime >= duration)
            {
                if (_loopPlayback)
                {
                    _playTime = 0f;
                    _livePreview.ResetSimulation();
                }
                else
                {
                    _playTime = duration;
                    _playing = false;
                }
            }
            else
            {
                _livePreview.StepSimulation(scaled);
            }

            Repaint();
        }

        #endregion

        #region 패널 그리기

        private void DrawPreviewPanel(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.19f, 0.19f, 0.20f));

            if (_selected == null)
            {
                GUI.Label(rect, "왼쪽에서 이펙트를 선택하세요.", CellPlaceholderStyle);
                return;
            }

            const float margin = 8f;
            float x = rect.x + margin;
            float width = rect.width - margin * 2f;
            float y = rect.y + margin;

            // 제목
            var titleRect = new Rect(x, y, width, 20f);
            GUI.Label(titleRect, _selected.Name, EditorStyles.boldLabel);
            y += 20f;

            var subtitleRect = new Rect(x, y, width, 14f);
            GUI.Label(subtitleRect, _selected.Package, EditorStyles.miniLabel);
            y += 18f;

            // 뷰포트 — 정사각형에 가깝게, 남은 높이를 넘지 않도록 제한
            float viewportSize = Mathf.Min(width, rect.yMax - y - 190f);
            viewportSize = Mathf.Max(viewportSize, 120f);
            var viewportRect = new Rect(x, y, width, viewportSize);
            DrawViewport(viewportRect);
            y = viewportRect.yMax + 6f;

            // 재생 컨트롤
            y = DrawPlaybackControls(new Rect(x, y, width, 0f));

            // 정보
            y = DrawInfoBlock(new Rect(x, y, width, 0f));

            // 액션 버튼
            DrawActionButtons(new Rect(x, y, width, 0f));
        }

        private void DrawViewport(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.09f));

            if (_livePreview == null || !_livePreview.HasTarget)
            {
                GUI.Label(rect, "프리뷰를 만들 수 없습니다.", CellPlaceholderStyle);
                return;
            }

            if (_batcher.IsRunning)
            {
                GUI.Label(rect, "썸네일 배치 캡처 중에는\n프리뷰가 멈춥니다.", CellPlaceholderStyle);
                return;
            }

            // BeginPreview/EndPreview는 Repaint에서만 호출해야 한다
            if (Event.current.type == EventType.Repaint)
            {
                var texture = _livePreview.RenderLive(rect);
                if (texture != null)
                {
                    GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
                }
            }

            HandleViewportInput(rect);
        }

        /// <summary>뷰포트에서 드래그로 회전, 휠로 줌.</summary>
        private void HandleViewportInput(Rect rect)
        {
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Orbit);

            switch (evt.type)
            {
                case EventType.MouseDrag when evt.button == 0:
                    _livePreview.Yaw += evt.delta.x;
                    _livePreview.Pitch = Mathf.Clamp(_livePreview.Pitch + evt.delta.y, -85f, 85f);
                    _livePreview.ApplyCameraTransform();
                    evt.Use();
                    Repaint();
                    break;

                case EventType.ScrollWheel:
                    _livePreview.ZoomFactor = Mathf.Clamp(_livePreview.ZoomFactor * (1f + evt.delta.y * 0.05f), 0.1f, 6f);
                    _livePreview.ApplyCameraTransform();
                    evt.Use();
                    Repaint();
                    break;
            }
        }

        /// <summary>재생 버튼 줄과 타임라인. 다음 요소가 시작될 y를 반환한다.</summary>
        private float DrawPlaybackControls(Rect rect)
        {
            float duration = _livePreview != null && _livePreview.HasTarget ? _livePreview.EstimatedDuration : 1f;
            float y = rect.y;

            var rowRect = new Rect(rect.x, y, rect.width, 20f);
            GUILayout.BeginArea(rowRect);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_playing ? "❚❚ 정지" : "▶ 재생", GUILayout.Width(70f)))
                {
                    _playing = !_playing;
                    _lastPreviewUpdate = EditorApplication.timeSinceStartup;
                }

                if (GUILayout.Button("↻ 처음부터", GUILayout.Width(80f)))
                {
                    RestartPlayback();
                }

                _loopPlayback = GUILayout.Toggle(_loopPlayback, "반복", EditorStyles.miniButton, GUILayout.Width(44f));

                GUILayout.Label("속도", EditorStyles.miniLabel, GUILayout.Width(28f));
                _playSpeed = GUILayout.HorizontalSlider(_playSpeed, 0.1f, 2f);
            }
            GUILayout.EndArea();
            y += 24f;

            // 타임라인 스크러버
            var sliderRect = new Rect(rect.x, y, rect.width - 52f, 16f);
            EditorGUI.BeginChangeCheck();
            float scrubbed = GUI.HorizontalSlider(sliderRect, _playTime, 0f, duration);
            if (EditorGUI.EndChangeCheck() && _livePreview != null)
            {
                _playing = false;
                _playTime = scrubbed;
                _livePreview.SimulateTo(_playTime);
                Repaint();
            }

            var timeLabelRect = new Rect(sliderRect.xMax + 4f, y - 1f, 48f, 16f);
            GUI.Label(timeLabelRect, $"{_playTime:0.00}s", EditorStyles.miniLabel);
            y += 20f;

            // 카메라 리셋
            var resetRect = new Rect(rect.x, y, 100f, 16f);
            if (GUI.Button(resetRect, "시점 초기화", EditorStyles.miniButton) && _livePreview != null)
            {
                _livePreview.Yaw = 0f;
                _livePreview.Pitch = 8f;
                _livePreview.ZoomFactor = 1f;
                _livePreview.FrameCamera();
                Repaint();
            }
            y += 22f;

            return y;
        }

        private float DrawInfoBlock(Rect rect)
        {
            float y = rect.y;
            var style = EditorStyles.miniLabel;

            string categories = DescribeCategories(_selected.Categories);
            string particles = _selected.Captured
                ? $"파티클 시스템 {_selected.ParticleSystemCount}개 · 길이 {_selected.Duration:0.0}초"
                : "썸네일 미캡처 (부가 정보 없음)";

            var lines = new List<string>
            {
                particles,
                string.IsNullOrEmpty(categories) ? "분류: 없음" : $"분류: {categories}",
                _selected.Path,
            };

            foreach (var line in lines)
            {
                var lineRect = new Rect(rect.x, y, rect.width, 14f);
                GUI.Label(lineRect, new GUIContent(line, line), style);
                y += 14f;
            }

            return y + 6f;
        }

        private void DrawActionButtons(Rect rect)
        {
            float buttonWidth = (rect.width - 8f) / 3f;
            var buttonRect = new Rect(rect.x, rect.y, buttonWidth, 22f);

            if (GUI.Button(buttonRect, "씬에 배치"))
            {
                InstantiateIntoScene(_selected);
            }

            buttonRect.x += buttonWidth + 4f;
            if (GUI.Button(buttonRect, "Project"))
            {
                PingInProject(_selected);
            }

            buttonRect.x += buttonWidth + 4f;
            if (GUI.Button(buttonRect, "경로 복사"))
            {
                EditorGUIUtility.systemCopyBuffer = _selected.Path;
                ShowNotification(new GUIContent("경로를 복사했습니다"));
            }
        }

        /// <summary>카테고리 비트마스크를 한글 목록 문자열로 바꾼다.</summary>
        private static string DescribeCategories(int mask)
        {
            if (mask == 0) return string.Empty;

            var builder = new StringBuilder();
            foreach (var category in EffectCategoryClassifier.AllCategories)
            {
                if ((mask & (int)category) == 0) continue;

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(EffectCategoryClassifier.GetDisplayName(category));
            }
            return builder.ToString();
        }

        #endregion
    }
}

// 사운드 브라우저 — 프리뷰 패널
// 선택한 클립을 큰 파형으로 보여주고 그 자리에서 재생한다. 플레이 모드에 들어갈 필요가 없다.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PX.SoundBrowser
{
    public partial class SoundBrowserWindow
    {
        #region 상태

        /// <summary>선택된 클립. 재생과 탐색에 쓰이므로 참조를 들고 있는다</summary>
        private AudioClip _selectedClip;

        /// <summary>프리뷰용 고해상도 파형 텍스처</summary>
        private Texture2D _previewWaveTexture;

        /// <summary>지금 들고 있는 프리뷰 텍스처가 어느 항목의 것인지. 실패한 경우에도 채워 재시도를 막는다</summary>
        private string _previewWaveGuid;

        private Vector2 _previewScroll;

        #endregion

        #region 선택

        /// <summary>항목을 선택하고 필요하면 재생한다.</summary>
        private void Select(SoundEntry entry, int index)
        {
            bool sameEntry = ReferenceEquals(entry, _selected);

            if (!sameEntry)
            {
                _selected = entry;
                _selectedIndex = index;
                _selectedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(entry.Path);
                BuildPreviewWaveform(entry);
            }
            else
            {
                _selectedIndex = index;
            }

            // 이미 선택된 항목을 다시 누르면 처음부터 다시 들려준다
            if (_autoPlay || sameEntry)
            {
                PlaySelected(0f);
            }

            Repaint();
        }

        /// <summary>항목을 선택하고 지정 위치부터 재생한다 (자동 재생 설정과 무관).</summary>
        private void SelectAndPlay(SoundEntry entry, int index, float normalizedStart)
        {
            if (!ReferenceEquals(entry, _selected))
            {
                _selected = entry;
                _selectedIndex = index;
                _selectedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(entry.Path);
                BuildPreviewWaveform(entry);
            }
            else
            {
                _selectedIndex = index;
            }

            PlaySelected(normalizedStart);
            Repaint();
        }

        private void PlaySelected(float normalizedStart)
        {
            if (_selected == null) return;

            if (_selectedClip == null)
            {
                _selectedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(_selected.Path);
            }
            if (_selectedClip == null) return;

            _player.Play(_selected.Guid, _selectedClip, normalizedStart);
        }

        /// <summary>재생/정지를 전환한다 (스페이스바).</summary>
        private void TogglePlayback()
        {
            if (_selected == null)
            {
                if (_filtered.Count == 0) return;

                Select(_filtered[0], 0);
                PlaySelected(0f);
                return;
            }

            if (_player.IsPlaying)
            {
                _player.StopPlayback();
            }
            else
            {
                PlaySelected(0f);
            }
            Repaint();
        }

        #endregion

        #region 프리뷰 파형

        /// <summary>
        /// 프리뷰용 파형을 만든다.
        /// 클립을 읽어 고해상도로 뽑되, 읽지 못하면 인덱스에 저장된 저해상도 엔벨로프로 대신한다.
        /// </summary>
        /// <summary>
        /// 선택 항목에 딸린 자원(클립, 프리뷰 파형)이 살아 있는지 확인하고 없으면 다시 만든다.
        /// 스크립트를 고쳐 도메인이 리로드되면 텍스처가 파괴되므로, 그릴 때마다 확인해야 파형이 사라지지 않는다.
        /// </summary>
        private void EnsureSelectionResources()
        {
            if (_selected == null) return;

            if (_selectedClip == null)
            {
                _selectedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(_selected.Path);
            }

            if (_previewWaveTexture == null || !string.Equals(_previewWaveGuid, _selected.Guid, StringComparison.Ordinal))
            {
                BuildPreviewWaveform(_selected);
            }
        }

        private void BuildPreviewWaveform(SoundEntry entry)
        {
            DisposePreviewWaveform();
            if (entry == null) return;

            _previewWaveGuid = entry.Guid;

            byte[] envelope = null;

            if (_selectedClip != null)
            {
                envelope = SoundWaveform.ExtractEnvelope(
                    _selectedClip, SoundWaveform.PreviewResolution, out float peak, out float rms);

                // 아직 분석 전인 항목이라면 이 김에 인덱스도 채워둔다
                if (envelope != null && !entry.Analyzed)
                {
                    entry.Analyzed = true;
                    entry.PeakLevel = peak;
                    entry.RmsLevel = rms;

                    var listEnvelope = SoundWaveform.ExtractEnvelope(
                        _selectedClip, SoundWaveform.ListResolution, out _, out _);
                    entry.SetEnvelope(listEnvelope);
                    SoundWaveformCache.Invalidate(entry.Guid);
                }
            }

            envelope ??= entry.GetEnvelope();
            if (envelope == null) return;

            _previewWaveTexture = SoundWaveform.BuildTexture(envelope, 160);
        }

        private void DisposePreviewWaveform()
        {
            if (_previewWaveTexture != null)
            {
                DestroyImmediate(_previewWaveTexture);
                _previewWaveTexture = null;
            }
            _previewWaveGuid = null;
        }

        #endregion

        #region 패널 그리기

        private void DrawPreviewPanel(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.19f, 0.19f, 0.20f));

            if (_selected == null)
            {
                GUI.Label(rect, "왼쪽에서 사운드를 선택하세요.", PlaceholderStyle);
                return;
            }

            EnsureSelectionResources();

            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f));
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll);

            EditorGUILayout.LabelField(_selected.Name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_selected.ShortFolder, EditorStyles.miniLabel);

            GUILayout.Space(4f);
            DrawWaveformViewport();

            GUILayout.Space(4f);
            DrawTransport();

            GUILayout.Space(6f);
            DrawInfoBlock();

            GUILayout.Space(6f);
            DrawActionButtons();

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawWaveformViewport()
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(110f), GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(rect, new Color(0.11f, 0.11f, 0.13f));

            if (_previewWaveTexture == null)
            {
                GUI.Label(rect, _selected.Analyzed ? "파형을 읽을 수 없는 클립입니다." : "파형 미분석", PlaceholderStyle);
            }
            else
            {
                // 중앙 기준선
                EditorGUI.DrawRect(new Rect(rect.x, rect.center.y, rect.width, 1f), new Color(1f, 1f, 1f, 0.08f));

                var previous = GUI.color;
                GUI.color = new Color(0.55f, 0.78f, 0.98f);
                GUI.DrawTexture(rect, _previewWaveTexture, ScaleMode.StretchToFill, true);
                GUI.color = previous;
            }

            DrawPlayhead(rect);
            HandleWaveformInput(rect);
        }

        private void DrawPlayhead(Rect rect)
        {
            bool isCurrent = string.Equals(_player.CurrentGuid, _selected.Guid, StringComparison.Ordinal);
            if (!isCurrent) return;
            if (!_player.IsPlaying && !_player.IsPaused) return;

            float progress = _player.NormalizedPosition;
            float x = rect.x + rect.width * progress;

            // 지나온 구간을 살짝 덮어 재생 위치를 눈에 띄게 한다
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, x - rect.x, rect.height), new Color(1f, 1f, 1f, 0.06f));
            EditorGUI.DrawRect(new Rect(x, rect.y, 1.5f, rect.height), new Color(1f, 0.85f, 0.4f, 0.95f));
        }

        /// <summary>파형을 누르거나 끌어 재생 위치를 옮긴다.</summary>
        private void HandleWaveformInput(Rect rect)
        {
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Text);

            bool isDragOrDown = evt.type == EventType.MouseDown
                                || (evt.type == EventType.MouseDrag && evt.button == 0);
            if (!isDragOrDown || evt.button != 0) return;

            float normalized = Mathf.Clamp01((evt.mousePosition.x - rect.x) / rect.width);

            if (string.Equals(_player.CurrentGuid, _selected.Guid, StringComparison.Ordinal))
            {
                _player.Seek(normalized);
            }
            else
            {
                PlaySelected(normalized);
            }

            evt.Use();
            Repaint();
        }

        private void DrawTransport()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!SoundPreviewPlayer.IsAvailable))
                {
                    bool isCurrent = string.Equals(_player.CurrentGuid, _selected.Guid, StringComparison.Ordinal);
                    bool isPlaying = isCurrent && _player.IsPlaying;

                    if (GUILayout.Button(isPlaying ? "❚❚ 일시정지" : "▶ 재생", GUILayout.Width(80f)))
                    {
                        if (!isCurrent)
                        {
                            PlaySelected(0f);
                        }
                        else if (isPlaying || _player.IsPaused)
                        {
                            _player.TogglePause();
                        }
                        else
                        {
                            PlaySelected(0f);
                        }
                    }

                    if (GUILayout.Button("■ 정지", GUILayout.Width(56f)))
                    {
                        _player.StopPlayback();
                    }

                    if (GUILayout.Button("↻ 처음부터", GUILayout.Width(76f)))
                    {
                        PlaySelected(0f);
                    }
                }

                EditorGUI.BeginChangeCheck();
                bool loop = GUILayout.Toggle(_player.Loop, "반복", EditorStyles.miniButton, GUILayout.Width(38f));
                if (EditorGUI.EndChangeCheck())
                {
                    _player.SetLoop(loop);
                }

                bool mute = GUILayout.Toggle(SoundPreviewPlayer.MasterMute, "음소거", EditorStyles.miniButton, GUILayout.Width(50f));
                SoundPreviewPlayer.MasterMute = mute;

                GUILayout.FlexibleSpace();
            }

            // 진행 시간
            bool showTime = string.Equals(_player.CurrentGuid, _selected.Guid, StringComparison.Ordinal);
            string position = showTime ? FormatTime(_player.Position) : FormatTime(0f);
            EditorGUILayout.LabelField($"{position} / {FormatTime(_selected.Length)}", EditorStyles.miniLabel);
        }

        private void DrawInfoBlock()
        {
            var lines = new List<(string Label, string Value)>
            {
                ("길이", FormatLength(_selected.Length)),
                ("채널 / 주파수", _selected.Frequency > 0
                    ? $"{_selected.Channels}ch · {_selected.Frequency:N0}Hz · {_selected.Samples:N0} 샘플"
                    : "-"),
                ("파일", $"{FormatSize(_selected.FileSize)} · .{_selected.Extension}"),
                ("임포터", BuildImporterSummary()),
                ("음량", _selected.Analyzed
                    ? $"피크 {FormatDecibel(_selected.PeakLevel)} · 평균 {FormatDecibel(_selected.RmsLevel)}"
                    : "미분석"),
                ("분류", DescribeCategories(_selected.Categories)),
                ("사용", _selected.IsGameAsset
                    ? (_selected.IsReferenced ? "게임 편입 · 참조 중" : "게임 편입")
                    : (_selected.IsReferenced ? "스토어 에셋 · 참조 중" : "스토어 에셋")),
            };

            foreach (var line in lines)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(line.Label, EditorStyles.miniLabel, GUILayout.Width(84f));
                    GUILayout.Label(new GUIContent(line.Value, line.Value), EditorStyles.miniLabel);
                }
            }

            GUILayout.Space(2f);
            EditorGUILayout.SelectableLabel(_selected.Path, EditorStyles.miniLabel, GUILayout.Height(14f));
        }

        private string BuildImporterSummary()
        {
            if (string.IsNullOrEmpty(_selected.LoadType)) return "-";

            var summary = $"{_selected.LoadType} · {_selected.Compression}";
            if (_selected.ForceToMono)
            {
                summary += " · 모노 강제";
            }
            return summary;
        }

        private void DrawActionButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Project"))
                {
                    PingInProject(_selected);
                }

                if (GUILayout.Button("경로 복사"))
                {
                    EditorGUIUtility.systemCopyBuffer = _selected.Path;
                    ShowNotification(new GUIContent("경로를 복사했습니다"));
                }

                if (GUILayout.Button("파형 재분석"))
                {
                    ReanalyzeSingle(_selected);
                }
            }
        }

        #endregion

        #region 표시 형식

        private static string FormatTime(float seconds)
        {
            if (seconds < 0f) seconds = 0f;

            int minutes = (int)(seconds / 60f);
            float remainder = seconds - minutes * 60f;
            return $"{minutes}:{remainder:00.0}";
        }

        /// <summary>진폭(0~1)을 dBFS로 바꾼다.</summary>
        private static string FormatDecibel(float level)
        {
            if (level <= 0.0001f) return "-∞ dB";
            return $"{20f * Mathf.Log10(level):0.0} dB";
        }

        /// <summary>카테고리 비트마스크를 한글 목록 문자열로 바꾼다.</summary>
        private static string DescribeCategories(int mask)
        {
            if (mask == 0) return "없음";

            var builder = new StringBuilder();
            foreach (var category in SoundCategoryClassifier.AllCategories)
            {
                if ((mask & (int)category) == 0) continue;

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(SoundCategoryClassifier.GetDisplayName(category));
            }
            return builder.ToString();
        }

        #endregion
    }
}

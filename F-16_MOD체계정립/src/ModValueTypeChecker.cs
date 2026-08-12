using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// MOD ValueType 검증 도구
    /// 잘못된 ValueType 설정을 찾아냅니다.
    ///
    /// 규칙:
    /// - _inc, _more 접미사가 붙은 MOD → FLOAT_PER 여야 함
    /// - 접미사 없는 기본값 MOD → FLOAT 또는 INT 여야 함 (FLOAT_PER면 안됨)
    /// </summary>
    public class ModValueTypeChecker : EditorWindow
    {
        private Vector2 scrollPosition;
        private string resultText = "";
        private List<ModIssue> issues = new List<ModIssue>();
        private bool showAllMods = false;
        private bool showOnlyIssues = true;

        private struct ModIssue
        {
            public string modName;
            public EModValueType currentType;
            public EModValueType expectedType;
            public string reason;
            public bool isIssue;
        }

        [MenuItem("PX Editor/P1 Simulator/MOD ValueType 검증")]
        public static void ShowWindow()
        {
            var window = GetWindow<ModValueTypeChecker>("MOD ValueType 검증");
            window.minSize = new Vector2(600, 400);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("MOD ValueType 검증 도구", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "규칙:\n" +
                "• _inc, _more 접미사 → FLOAT_PER\n" +
                "• 기본값 MOD (접미사 없음) → FLOAT 또는 INT (FLOAT_PER면 안됨)\n" +
                "• _chance, _multiplier 등 → FLOAT (백분율 형태로 저장, GetValue 시 변환 없음)",
                MessageType.Info);

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("검증 실행", GUILayout.Height(30)))
            {
                CheckAllMods();
            }
            if (GUILayout.Button("클립보드 복사", GUILayout.Height(30)))
            {
                GUIUtility.systemCopyBuffer = resultText;
                Debug.Log("클립보드에 복사되었습니다.");
            }
            if (GUILayout.Button("리포트 출력", GUILayout.Height(30)))
            {
                ExportReport();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            showOnlyIssues = EditorGUILayout.Toggle("문제만 표시", showOnlyIssues);
            showAllMods = EditorGUILayout.Toggle("전체 MOD 표시", showAllMods);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 결과 표시
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (issues.Count > 0)
            {
                int issueCount = 0;
                foreach (var issue in issues)
                {
                    if (issue.isIssue) issueCount++;
                }

                EditorGUILayout.LabelField($"검증 결과: 총 {issues.Count}개 MOD, {issueCount}개 문제 발견", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                foreach (var issue in issues)
                {
                    if (showOnlyIssues && !issue.isIssue && !showAllMods) continue;

                    EditorGUILayout.BeginHorizontal();

                    if (issue.isIssue)
                    {
                        GUI.color = Color.red;
                        EditorGUILayout.LabelField("⚠️", GUILayout.Width(25));
                    }
                    else
                    {
                        GUI.color = Color.green;
                        EditorGUILayout.LabelField("✓", GUILayout.Width(25));
                    }

                    GUI.color = Color.white;
                    EditorGUILayout.LabelField(issue.modName, GUILayout.Width(300));
                    EditorGUILayout.LabelField(issue.currentType.ToString(), GUILayout.Width(100));

                    if (issue.isIssue)
                    {
                        GUI.color = Color.yellow;
                        EditorGUILayout.LabelField($"→ {issue.expectedType}", GUILayout.Width(120));
                        GUI.color = Color.white;
                        EditorGUILayout.LabelField(issue.reason);
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                EditorGUILayout.LabelField("검증 실행 버튼을 클릭하세요.");
            }

            EditorGUILayout.EndScrollView();
        }

        private void CheckAllMods()
        {
            issues.Clear();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========== MOD ValueType 검증 결과 ==========");
            sb.AppendLine();

            // GameDB에서 MOD 정보 가져오기
            if (GameDBClientManager.Instance == null || GameDBClientManager.Instance.GameDB_Mod == null)
            {
                resultText = "GameDB가 로드되지 않았습니다. 플레이 모드에서 실행하세요.";
                Debug.LogError(resultText);
                return;
            }

            var modMap = GameDBClientManager.Instance.GameDB_Mod.Mod.MapData;
            if (modMap == null)
            {
                resultText = "MOD 데이터가 없습니다.";
                Debug.LogError(resultText);
                return;
            }

            sb.AppendLine($"총 {modMap.Count}개 MOD 검증 중...");
            sb.AppendLine();

            int issueCount = 0;

            foreach (var kvp in modMap)
            {
                EMod mod = kvp.Key;
                GameDB_Client_Mod modData = kvp.Value;
                string modName = mod.ToString();
                EModValueType currentType = modData.ValueType;

                var issue = new ModIssue
                {
                    modName = modName,
                    currentType = currentType,
                    isIssue = false
                };

                // 규칙 검증
                // _inc, _more가 포함된 MOD는 증가율이므로 FLOAT_PER
                bool hasInc = modName.Contains("_inc");
                bool hasMore = modName.Contains("_more");
                bool isRatioMod = hasInc || hasMore;

                if (isRatioMod)
                {
                    // _inc, _more 포함 → FLOAT_PER 여야 함
                    if (currentType != EModValueType.FLOAT_PER)
                    {
                        issue.isIssue = true;
                        issue.expectedType = EModValueType.FLOAT_PER;
                        issue.reason = "_inc/_more 포함 MOD는 FLOAT_PER 필요";
                        issueCount++;
                    }
                }
                else
                {
                    // 기본값 MOD → FLOAT_PER면 안됨 (특수 케이스 제외)
                    if (currentType == EModValueType.FLOAT_PER)
                    {
                        // 특수 케이스: 실제로 백분율로 표시되어야 하는 MOD들은 예외
                        // 예: 저항률, 감소율 등 (필요시 여기에 예외 추가)
                        bool isException = IsFloatPerException(modName);

                        if (!isException)
                        {
                            issue.isIssue = true;
                            issue.expectedType = EModValueType.FLOAT;
                            issue.reason = "기본값 MOD는 FLOAT 권장 (FLOAT_PER면 GetValue 시 * 0.01 됨)";
                            issueCount++;
                        }
                    }
                }

                issues.Add(issue);

                // 텍스트 출력
                if (issue.isIssue)
                {
                    sb.AppendLine($"⚠️ {modName}");
                    sb.AppendLine($"   현재: {currentType} → 권장: {issue.expectedType}");
                    sb.AppendLine($"   이유: {issue.reason}");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("==========================================");
            sb.AppendLine($"검증 완료: {issueCount}개 문제 발견");
            sb.AppendLine("==========================================");

            resultText = sb.ToString();
            Debug.Log(resultText);

        }

        /// <summary>
        /// FLOAT_PER가 올바른 특수 케이스인지 확인
        /// </summary>
        private bool IsFloatPerException(string modName)
        {
            // 아래 패턴들은 FLOAT_PER가 올바른 경우:
            // - 저항/감소 계열: _resistance, _reduction 등
            // - 실제로 0~1 범위의 확률/비율로 사용되는 경우

            // 현재는 예외 없음 - 필요시 추가
            // if (modName.Contains("_resistance")) return true;
            // if (modName.Contains("_reduction")) return true;

            return false;
        }

        /// <summary>
        /// MOD ValueType 리포트를 MD 파일로 출력
        /// </summary>
        private void ExportReport()
        {
            // GameDB에서 MOD 정보 가져오기
            if (GameDBClientManager.Instance == null || GameDBClientManager.Instance.GameDB_Mod == null)
            {
                Debug.LogError("GameDB가 로드되지 않았습니다. 플레이 모드에서 실행하세요.");
                return;
            }

            var modMap = GameDBClientManager.Instance.GameDB_Mod.Mod.MapData;
            if (modMap == null)
            {
                Debug.LogError("MOD 데이터가 없습니다.");
                return;
            }

            StringBuilder sb = new StringBuilder();

            foreach (var kvp in modMap)
            {
                EMod mod = kvp.Key;
                GameDB_Client_Mod modData = kvp.Value;
                string modName = mod.ToString();
                EModValueType valueType = modData.ValueType;

                sb.AppendLine($"{modName} {valueType}");
            }

            // 파일 저장
            string reportPath = "Assets/Editor/BattleSimulator/Reports/ModValueType.md";
            string fullPath = Path.Combine(Application.dataPath, "../", reportPath);
            fullPath = Path.GetFullPath(fullPath);

            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, sb.ToString());
            AssetDatabase.Refresh();

            Debug.Log($"리포트 출력 완료: {reportPath}");
            EditorUtility.RevealInFinder(fullPath);
        }
    }
}

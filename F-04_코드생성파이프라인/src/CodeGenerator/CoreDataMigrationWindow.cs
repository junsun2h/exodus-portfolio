using System.Diagnostics;
using System.IO;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PX
{
    /// <summary>
    /// CoreData 마이그레이션 에디터 창
    /// C# CoreData 변경 시 TypeScript 마이그레이션 코드를 자동으로 생성합니다.
    /// </summary>
    public class CoreDataMigrationWindow : OdinEditorWindow
    {
        #region Menu Item
        [MenuItem("PX Editor/CoreData 마이그레이션")]
        private static void OpenWindow()
        {
            var window = GetWindow<CoreDataMigrationWindow>();
            window.titleContent = new GUIContent("CoreData 마이그레이션");
            window.minSize = new Vector2(500, 600);
            window.Show();
        }
        #endregion

        #region 경로 설정
        [TitleGroup("경로 설정")]
        [FolderPath(RequireExistingPath = true)]
        [LabelText("스키마 히스토리")]
        [ReadOnly]
        public string schemaHistoryPath;

        [TitleGroup("경로 설정")]
        [FolderPath(RequireExistingPath = true)]
        [LabelText("생성 파일 출력")]
        [ReadOnly]
        public string generatedOutputPath;
        #endregion

        #region 설명 - 마이그레이션이란?
        [TitleGroup("마이그레이션이란?")]
        [InfoBox(
            "CoreData 마이그레이션은 C# 코드 변경 시 기존 유저 데이터를 자동으로 업데이트하는 시스템입니다.\n\n" +
            "예시:\n" +
            "1. C#에서 AccountLoginInfo에 새 필드 추가: public string DeviceType;\n" +
            "2. 이미 운영 중인 유저 DB에는 DeviceType 필드가 없음\n" +
            "3. 마이그레이션이 자동으로 DeviceType: \"\" 필드를 추가\n\n" +
            "결과: 유저가 로그인할 때 서버가 자동으로 빠진 필드를 채워줍니다.",
            InfoMessageType.Info)]
        [ShowInInspector, HideLabel, DisplayAsString]
        [PropertyOrder(-100)]
        private string migrationDescription = "";

        [TitleGroup("마이그레이션이란?")]
        [Button("자세한 설명 보기", ButtonSizes.Medium), GUIColor(0.6f, 0.8f, 1f)]
        [PropertyOrder(-99)]
        private void OpenDocumentation()
        {
            string docPath = Path.Combine(Application.dataPath, "../Docs/작업/CoreData마이그레이션/사용설명서.md");
            if (File.Exists(docPath))
            {
                Process.Start(docPath);
            }
            else
            {
                EditorUtility.DisplayDialog("문서 없음", $"문서 경로를 찾을 수 없습니다:\n{docPath}", "확인");
            }
        }
        #endregion

        #region 메인 기능
        [TitleGroup("메인 기능", "C# CoreData 수정 후 이 버튼을 클릭하세요")]
        [Button("Generate CoreData + Migration", ButtonSizes.Large), GUIColor(0.3f, 0.8f, 0.3f)]
        [PropertyOrder(0)]
        [InfoBox(
            "CoreData.ts와 마이그레이션 코드를 동시에 생성합니다.\n\n" +
            "실행 순서:\n" +
            "1. P4 Checkout (자동)\n" +
            "2. CoreData.ts 생성\n" +
            "3. 현재 스키마와 이전 스키마 비교\n" +
            "4. 변경 감지 및 마이그레이션 코드 생성\n" +
            "5. 스키마 히스토리 업데이트",
            InfoMessageType.None)]
        private void GenerateCoreDataAndMigration()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    // P4 Checkout
                    if (!P4CheckoutMigrationFiles())
                    {
                        Debug.LogWarning("[CoreData Migration] P4 Checkout 실패 - 수동으로 파일을 checkout 해주세요");
                    }

                    // CoreData.ts 생성
                    string coreDataResult = GenerateCoreData();

                    // Migration 코드 생성
                    string migrationResult = GenerateMigration();

                    string fullResult = $"=== CoreData.ts ===\n{coreDataResult}\n\n{migrationResult}";
                    EditorUtility.DisplayDialog("Generate CoreData + Migration", fullResult, "확인");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[CoreData Migration] 생성 실패: {ex}");
                    EditorUtility.DisplayDialog("오류", ex.Message, "확인");
                }
            };
        }
        #endregion

        #region 버전 설정
        [TitleGroup("버전 설정")]
        [HorizontalGroup("버전 설정/Input", Width = 300)]
        [LabelText("MAJOR 버전 Override")]
        [LabelWidth(140)]
        public int majorVersionOverride = -1;

        [HorizontalGroup("버전 설정/Input")]
        [ShowInInspector, HideLabel, DisplayAsString]
        private string versionHint => majorVersionOverride < 0 ? "(자동)" : $"(v{majorVersionOverride}.0.0)";

        [TitleGroup("버전 설정")]
        [DetailedInfoBox("버전 관리 규칙",
            "-1 (자동 모드):\n" +
            "  - PATCH (0.0.X): 필드/중첩필드 추가\n" +
            "  - MINOR (0.X.0): 필드 삭제, 타입 변경, 이름 변경\n\n" +
            "0 이상 (MAJOR 지정 모드):\n" +
            "  - 현재 MAJOR와 다르면: 지정값.0.0 으로 리셋\n" +
            "  - 현재 MAJOR와 같으면: 이후 MINOR/PATCH는 자동 규칙 적용\n" +
            "  - 예: Override=2 → 2.0.0 → 2.0.1 → 2.1.0 ...")]
        [ShowInInspector, HideLabel, DisplayAsString]
        private string versionRulesPlaceholder = "";
        #endregion

        #region 고급 옵션
        [TitleGroup("고급 옵션")]
        [FoldoutGroup("고급 옵션/개별 기능", false)]
        [InfoBox(
            "대부분의 경우 위의 'Generate CoreData + Migration' 버튼만 사용하면 됩니다.\n" +
            "아래 기능들은 특수한 상황에서만 사용합니다.",
            InfoMessageType.Info)]
        [ShowInInspector, HideLabel, DisplayAsString]
        private string advancedOptionsInfo = "";

        [FoldoutGroup("고급 옵션/개별 기능")]
        [Button("Initialize Schema History", ButtonSizes.Medium), GUIColor(0.7f, 0.9f, 0.7f)]
        [InfoBox(
            "사용 시점: 새로운 CoreData 클래스를 추가했을 때\n\n" +
            "동작: 스키마 히스토리가 없는 CoreData만 v0.0.1로 초기화합니다.\n" +
            "기존 히스토리가 있는 CoreData는 건너뜁니다.\n\n" +
            "참고: 'Generate CoreData + Migration'도 자동으로 이 작업을 수행합니다.",
            InfoMessageType.None)]
        private void InitializeSchemaHistory()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    P4CheckoutMigrationFiles();

                    var generator = new CodeGenerator_CoreDataMigration(
                        schemaHistoryPath,
                        generatedOutputPath,
                        false);
                    string result = generator.InitializeAllSchemaHistory();
                    EditorUtility.DisplayDialog("Initialize Schema History", result, "확인");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[CoreData Migration] 초기화 실패: {ex}");
                    EditorUtility.DisplayDialog("오류", ex.Message, "확인");
                }
            };
        }

        [FoldoutGroup("고급 옵션/개별 기능")]
        [Button("Migration Only", ButtonSizes.Medium), GUIColor(0.7f, 0.8f, 1f)]
        [InfoBox(
            "사용 시점: 거의 사용하지 않음\n\n" +
            "동작: CoreData.ts는 생성하지 않고, 마이그레이션 코드만 재생성합니다.\n\n" +
            "사용 예시:\n" +
            "- 마이그레이션 템플릿(CoreDataMigrationTemplates.cs)을 수정한 후 재생성\n" +
            "- 디버깅/테스트 목적",
            InfoMessageType.None)]
        private void GenerateMigrationOnly()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    P4CheckoutMigrationFiles();

                    string result = GenerateMigration();
                    EditorUtility.DisplayDialog("CoreData Migration", result, "확인");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[CoreData Migration] 마이그레이션 생성 실패: {ex}");
                    EditorUtility.DisplayDialog("오류", ex.Message, "확인");
                }
            };
        }

        [FoldoutGroup("고급 옵션/위험 기능", false)]
        [Button("Force Reinitialize Schema", ButtonSizes.Medium), GUIColor(1f, 0.5f, 0.5f)]
        [InfoBox(
            "사용 시점: 프로젝트 초기 설정 또는 개발 환경 리셋\n\n" +
            "동작: 모든 CoreData의 스키마 히스토리를 삭제하고 v0.0.1로 재초기화합니다.\n\n" +
            "경고: 운영 환경에서는 절대 사용하지 마세요!\n" +
            "기존 버전 히스토리가 삭제되어 마이그레이션 체인이 끊어집니다.",
            InfoMessageType.Warning)]
        private void ForceReinitializeSchemaHistory()
        {
            if (!EditorUtility.DisplayDialog("경고",
                "모든 스키마 히스토리를 v0.0.1로 재초기화합니다.\n" +
                "기존 버전 히스토리가 삭제됩니다.\n\n" +
                "계속하시겠습니까?",
                "예", "아니오"))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                try
                {
                    P4CheckoutMigrationFiles();

                    var generator = new CodeGenerator_CoreDataMigration(
                        schemaHistoryPath,
                        generatedOutputPath,
                        false);
                    string result = generator.ForceReinitializeAllSchemaHistory();
                    EditorUtility.DisplayDialog("Force Reinitialize", result, "확인");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[CoreData Migration] 강제 재초기화 실패: {ex}");
                    EditorUtility.DisplayDialog("오류", ex.Message, "확인");
                }
            };
        }
        #endregion

        #region 자동 처리되는 변경 유형
        [TitleGroup("자동 처리되는 변경 유형")]
        [FoldoutGroup("자동 처리되는 변경 유형/변경 유형 상세")]
        [ShowInInspector, HideLabel, DisplayAsString]
        [InfoBox(
            "필드 추가: 완전 자동 (기본값으로 초기화)\n" +
            "필드 삭제: 완전 자동 (데이터에서 제거)\n" +
            "필드 이름 변경: 휴리스틱 감지 (타입 동일 + 이름 유사)\n" +
            "필드 타입 변경: 기본 타입 자동 변환\n" +
            "중첩 필드: 모든 변경 유형 자동 처리",
            InfoMessageType.None)]
        private string changeTypesInfo = "";

        [FoldoutGroup("자동 처리되는 변경 유형/타입 변환 규칙")]
        [ShowInInspector, HideLabel, DisplayAsString]
        [InfoBox(
            "number → string: String(value)\n" +
            "string → number: Number(value) || 0\n" +
            "boolean → string: String(value)\n" +
            "string → boolean: value === \"true\"\n" +
            "boolean → number: value ? 1 : 0\n" +
            "number → boolean: Boolean(value)\n" +
            "number/string → ModValueInt: ModValueInt.create(Number(value))\n" +
            "ModValueInt → number: value?.GetValue() ?? 0\n" +
            "그 외: TODO 주석으로 표시 (수작업 필요)",
            InfoMessageType.None)]
        private string typeConversionInfo = "";
        #endregion

        #region 서버 실행 흐름
        [TitleGroup("서버 실행 흐름")]
        [FoldoutGroup("서버 실행 흐름/상세 흐름")]
        [ShowInInspector, HideLabel, DisplayAsString]
        [InfoBox(
            "1. 유저가 로그인 요청\n" +
            "2. Account_Login 서버 함수 실행\n" +
            "3. performMigrationIfNeeded() 호출\n" +
            "4. 각 CoreData 버전 비교 (DB 저장 버전 vs 최신 버전)\n" +
            "5. 구버전이면 마이그레이션 함수 순차 실행\n" +
            "6. 마이그레이션된 데이터 저장\n" +
            "7. 로그인 완료\n\n" +
            "결론: 클라이언트는 아무것도 안 해도 됨!",
            InfoMessageType.None)]
        private string serverFlowInfo = "";
        #endregion

        #region Unity Lifecycle
        protected override void OnEnable()
        {
            base.OnEnable();
            InitializePaths();
        }

        private void InitializePaths()
        {
            schemaHistoryPath = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../FirebaseCLI/functions/src/API/Migration/SchemaHistory"));
            generatedOutputPath = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../FirebaseCLI/functions/src/API/Migration/Generated"));
        }
        #endregion

        #region Private Methods
        private string GenerateCoreData()
        {
            string outputDir = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../FirebaseCLI/functions/src/Data/Generated"));

            var generator = new CodeGenerator_CoreData("CoreData", "", outputDir, false);
            return generator.Generate();
        }

        private string GenerateMigration()
        {
            var generator = new CodeGenerator_CoreDataMigration(
                schemaHistoryPath,
                generatedOutputPath,
                false,
                majorVersionOverride);
            return generator.Generate();
        }

        /// <summary>
        /// Git 전환 후 P4 체크아웃 불필요 - 항상 true 반환
        /// </summary>
        private bool P4CheckoutMigrationFiles()
        {
            try
            {
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[CoreData Migration] 파일 접근 실패: {ex.Message}");
                return false;
            }
        }
        #endregion
    }
}

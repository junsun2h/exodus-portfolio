using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PX
{
    public class CodeGenerateEditor : EditorWindow
    {
        private string outputDirEnumCS;
        private string outputDirEnumTS;
        private string outputDirCoreData_ParseJsonNodeCS;
        private string outputDirGameDBClient_CS;
        private string outputDirGameDBClient_ParseJsonNodeCS;
        private string outputDirCoreDataTS;
        private string outputDirGameDBDataTS;
        private string fileName;

        private bool isDebug = false;
        private bool isLog = false;
        private bool isEnumExpanded = true;
        private bool isCoreData_ParseJsonNodeExpanded = true;
        private bool isGameDBClientExpanded = true;
        private bool isCoreDataExpanded = true;
        private bool isGameDBDataExpanded = true;

        public void Awake()
        {
            outputDirEnumCS = Path.GetFullPath(Path.Combine(Application.dataPath, $"Source/Repository/generated"));
            outputDirEnumTS = Path.GetFullPath(Path.Combine(Application.dataPath, $"../FirebaseCLI/functions/src/Data/Generated"));

            outputDirCoreData_ParseJsonNodeCS = Path.GetFullPath(Path.Combine(Application.dataPath, $"Source/Logic/Data/Generated"));

            outputDirGameDBClient_CS = Path.GetFullPath(Path.Combine(Application.dataPath, $"Source/Logic/Manager/GameDBManager/Generated"));
            outputDirGameDBClient_ParseJsonNodeCS = outputDirGameDBClient_CS;

            outputDirCoreDataTS = Path.GetFullPath(Path.Combine(Application.dataPath, $"../FirebaseCLI/functions/src/Data/Generated"));

            outputDirGameDBDataTS = Path.GetFullPath(Path.Combine(Application.dataPath, $"../FirebaseCLI/functions/src/Data/Generated"));
        }

        [MenuItem("PX Editor/Code Generator")]
        public static void ShowUpdateEditor()
        {
            CodeGenerateEditor window = GetWindow<CodeGenerateEditor>(false, "Code Generator", true);
            window.Show();
        }

        private Vector2 scrollPos = Vector2.zero;
        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            try
            {
            isDebug = EditorGUILayout.Toggle("temp 파일로 출력하기", isDebug);
            isLog = EditorGUILayout.Toggle("With Log(.ts only)", isLog);

            EditorGUILayout.LabelField("_____________________________________________________________");

            #region Generate All
            GUILayout.Space(10);
            GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
            if (GUILayout.Button($"Generate All (원클릭)"))
            {
                try
                {
                    string result = Generate_All();
                    if (!string.IsNullOrEmpty(result))
                        EditorUtility.DisplayDialog("Generate All", result, "Ok");
                }
                catch (System.Exception ex)
                {
                    Debug.Assert(false, $"Failed CodeGenerate All : {ex}");
                }
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(5);
            GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
            if (GUILayout.Button($"Clear ParseJsonNode (컴파일 에러 시)"))
            {
                RunClearParseJsonNodeBat();
            }
            GUI.backgroundColor = Color.white;
            #endregion

            EditorGUILayout.LabelField("_____________________________________________________________");

            #region CommonEnum
            fileName = "CommonEnum";
            isEnumExpanded = EditorGUILayout.Foldout(isEnumExpanded, fileName, true);
            if (isEnumExpanded)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"{fileName}.cs");
                outputDirEnumCS = Path.GetFullPath(outputDirEnumCS);
                EditorGUILayout.TextField(outputDirEnumCS);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"{fileName}.ts");
                outputDirEnumTS = Path.GetFullPath(outputDirEnumTS);
                EditorGUILayout.TextField(outputDirEnumTS);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(10);
                if (GUILayout.Button($"Generate"))
                {
                    try
                    {
                        string result = Generate_CommonEnum();
                        if (!string.IsNullOrEmpty(result))
                            EditorUtility.DisplayDialog(fileName, result, "Ok");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.Assert(false, $"Failed {fileName} CodeGenerate : {ex}");
                    }
                }
            }
            #endregion

            EditorGUILayout.LabelField("_____________________________________________________________");

            #region CoreData
            fileName = "CoreData";
            isCoreDataExpanded = EditorGUILayout.Foldout(isCoreDataExpanded, fileName, true);
            if (isCoreDataExpanded)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"{fileName}.ts");
                outputDirCoreDataTS = Path.GetFullPath(outputDirCoreDataTS);
                EditorGUILayout.TextField(outputDirCoreDataTS);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(10);
                if (GUILayout.Button($"Generate"))
                {
                    try
                    {
                        string result = Generate_CoreData();
                        if (!string.IsNullOrEmpty(result))
                            EditorUtility.DisplayDialog(fileName, result, "Ok");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.Assert(false, $"Failed {fileName} CodeGenerate : {ex}");
                    }
                }
            }
            #endregion

            EditorGUILayout.LabelField("_____________________________________________________________");

            #region CoreData_ParseJsonNode
            fileName = "CoreData_ParseJsonNode";
            isCoreData_ParseJsonNodeExpanded = EditorGUILayout.Foldout(isCoreData_ParseJsonNodeExpanded, fileName, true);
            if (isCoreData_ParseJsonNodeExpanded)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"{fileName}.cs");
                outputDirCoreData_ParseJsonNodeCS = Path.GetFullPath(outputDirCoreData_ParseJsonNodeCS);
                EditorGUILayout.TextField(outputDirCoreData_ParseJsonNodeCS);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(10);
                if (GUILayout.Button($"Generate"))
                {
                    try
                    {
                        string result = Generate_CoreData_ParseJsonNode();
                        if (!string.IsNullOrEmpty(result))
                            EditorUtility.DisplayDialog(fileName, result, "Ok");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.Assert(false, $"Failed {fileName} CodeGenerate : {ex}");
                    }
                }
            }
            #endregion

            EditorGUILayout.LabelField("_____________________________________________________________");

            #region GameDBClient
            fileName = "GameDBClient";
            isGameDBClientExpanded = EditorGUILayout.Foldout(isGameDBClientExpanded, fileName, true);
            if (isGameDBClientExpanded)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"{fileName}.cs");
                outputDirGameDBClient_CS = Path.GetFullPath(outputDirGameDBClient_CS);
                EditorGUILayout.TextField(outputDirGameDBClient_CS);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"{fileName}_ParseJsonNode.cs");
                outputDirGameDBClient_ParseJsonNodeCS = Path.GetFullPath(outputDirGameDBClient_ParseJsonNodeCS);
                EditorGUILayout.TextField(outputDirGameDBClient_ParseJsonNodeCS);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(10);
                if (GUILayout.Button($"Generate"))
                {
                    try
                    {
                        /*
                         * https://realspace3.atlassian.net/wiki/spaces/Pixel/pages/2176319569/CodeGenerator
                         * 알고 있어야 하는 구동 방식에 대한 이해
                         * 
                         * Generate_GameDBClient()로 생성한 파일은 생성된 직후에는 컴파일 되지 않았기 때문에 어셈블리에 포함되어있지 않다.
                         * AppDomain.CurrentDomain.GetAssemblies() 함수를 돌려도 안 나온다는 얘기
                         * 
                         * Generate_GameDBClient_ParseJsonNode() 실행시 사용하는 Type 정보는 싱핼시점 컴파일된 어셈블리에서 얻은 정보만으로 수집한다.
                         * 즉, 이 타입들의 원본은 Generate_GameDBClient()이 새로 생성되기 전에 원래 있던 프로퍼티의 타입을 사용한다.
                         */
                        string result1 = Generate_GameDBClient();
                        string result2 = Generate_GameDBClient_ParseJsonNode();

                        if (!string.IsNullOrEmpty(result1) && !string.IsNullOrEmpty(result2))
                            EditorUtility.DisplayDialog(fileName, $"{result1}\n{result2}", "Ok");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.Assert(false, $"Failed {fileName} CodeGenerate : {ex}");
                    }
                }
            }
            #endregion


            EditorGUILayout.LabelField("_____________________________________________________________");

            #region GameDBData
            fileName = "GameDBData";
            isGameDBDataExpanded = EditorGUILayout.Foldout(isGameDBDataExpanded, fileName, true);
            if (isGameDBDataExpanded)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"{fileName}.ts");
                outputDirGameDBDataTS = Path.GetFullPath(outputDirGameDBDataTS);
                EditorGUILayout.TextField(outputDirGameDBDataTS);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(10);
                if (GUILayout.Button($"Generate"))
                {
                    try
                    {
                        string result = GenerateGameDBData();
                        if (!string.IsNullOrEmpty(result))
                            EditorUtility.DisplayDialog(fileName, result, "Ok");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.Assert(false, $"Failed {fileName} CodeGenerate : {ex}");
                    }
                }
            }
            #endregion

            EditorGUILayout.LabelField("_____________________________________________________________");
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private string GenerateGameDBData()
        {
            fileName = "GameDBData";
            outputDirGameDBDataTS = Path.GetFullPath(outputDirGameDBDataTS);

            var generator = new CodeGenerator_GameDBData(fileName, outputDirGameDBDataTS, isDebug, isLog);
            return generator.Generate();
        }

        private string Generate_All()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Generate_CommonEnum());
            sb.AppendLine(); sb.AppendLine();
            sb.Append(Generate_CoreData());
            sb.AppendLine(); sb.AppendLine();
            sb.Append(Generate_CoreData_ParseJsonNode());
            sb.AppendLine(); sb.AppendLine();
            sb.Append(Generate_GameDBClient());
            sb.AppendLine(); sb.AppendLine();
            sb.Append(Generate_GameDBClient_ParseJsonNode());
            sb.AppendLine(); sb.AppendLine();
            sb.Append(GenerateGameDBData());

            return sb.ToString();
        }

        private string Generate_CommonEnum()
        {
            fileName = "CommonEnum";
            outputDirEnumCS = Path.GetFullPath(outputDirEnumCS);
            outputDirEnumTS = Path.GetFullPath(outputDirEnumTS);

            CodeGenerator_CommonEnum generator = new CodeGenerator_CommonEnum(fileName, outputDirEnumCS, outputDirEnumTS, isDebug);
            return generator.Generate();
        }

        private string Generate_CoreData()
        {
            fileName = "CoreData";
            outputDirCoreDataTS = Path.GetFullPath(outputDirCoreDataTS);

            CodeGenerator_CoreData generator = new CodeGenerator_CoreData(fileName,
                "", outputDirCoreDataTS, isDebug);
            return generator.Generate();
        }

        private string Generate_CoreData_ParseJsonNode()
        {
            fileName = "CoreData_ParseJsonNode";
            outputDirCoreData_ParseJsonNodeCS = Path.GetFullPath(outputDirCoreData_ParseJsonNodeCS);

            CodeGenerator_CoreData_ParseJsonNode generator = new CodeGenerator_CoreData_ParseJsonNode(fileName,
                outputDirCoreData_ParseJsonNodeCS, isDebug);
            return generator.Generate();
        }

        private string Generate_GameDBClient()
        {
            fileName = "GameDBClient";
            outputDirGameDBClient_CS = Path.GetFullPath(outputDirGameDBClient_CS);

            CodeGenerator_GameDB_Client generator1 = new CodeGenerator_GameDB_Client(fileName, outputDirGameDBClient_CS, isDebug);
            return generator1.Generate();
        }

        private string Generate_GameDBClient_ParseJsonNode()
        {
            fileName = "GameDBClient_ParseJsonNode";
            outputDirGameDBClient_ParseJsonNodeCS = Path.GetFullPath(outputDirGameDBClient_ParseJsonNodeCS);

            CodeGenerator_GameDB_Client_ParseJsonNode generator2 = new CodeGenerator_GameDB_Client_ParseJsonNode(fileName, outputDirGameDBClient_ParseJsonNodeCS, isDebug);
            return generator2.Generate();
        }

        private void RunClearParseJsonNodeBat()
        {
            string batFile = Path.GetFullPath(Path.Combine(Application.dataPath, "../Batch/ClearParseJsonNode.bat"));

            ProcessStartInfo processInfo = new ProcessStartInfo("cmd.exe", "/c " + batFile)
            {
                CreateNoWindow = false,
                UseShellExecute = true,
            };

            Process.Start(processInfo);
        }

    } // class EnumCodeGenerateEditor
}

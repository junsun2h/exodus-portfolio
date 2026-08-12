using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace PX
{
    public class ClientGachaValidate
    {
        private readonly bool _isDebugMode = false;

        public ClientGachaValidate()
        {
#if UNITY_EDITOR
            // 로컬 에뮬레이터 접속일 때만 검증한다. 실서버는 서버 난수 로그 파일 자체가 없다.
            _isDebugMode = TemplateLocalData.IsEditorDebugMode;
#endif
        }

        // 난수 로그를 역직렬화하기 위한 임시 클래스
        private class RandomLogData
        {
            [JsonProperty("randomValues")]
            public List<string> randomLines { get; set; }
        }

        /// 두 JSON 파일(클라, 서버)을 로드하여 randomValues 배열을 비교.
        /// - 완전히 일치하면 "Match" 로그
        /// - 차이가 있으면 어느 index, 값이 얼마나 다른지 출력
        public void CompareRandomValuesStageReward(EStage stage, int stageLevel, string hash)
        {
            string fileName = stage.ToString() + "_" + stageLevel.ToString();
            CompareRandomValuesFromClientAndServer(fileName, hash);
        }

        public void CompareRandomValuesGacha(EGacha eGacha, string hash)
        {
            string fileName = eGacha.ToString();
            CompareRandomValuesFromClientAndServer(fileName, hash);
        }

        public void CompareRandomValuesFromClientAndServer(string fileName, string hash)
        {
            if (!_isDebugMode) return;

            try
            {
                // 1) 파일 로드
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string folder = Path.Combine(projectRoot, "Temp", "NextRandom");
                string shortHash = hash.Substring(0, 8); // 앞 8글자
                string filePathClient = Path.Combine(folder, $"{shortHash}_client_{fileName}.json");
                string filePathServer = Path.Combine(folder, $"{shortHash}_server_{fileName}.json");

                if (!File.Exists(filePathClient))
                {
                    Debug.LogError($"[CompareRandomValues] Client file not found: {filePathClient}");
                    return;
                }
                if (!File.Exists(filePathServer))
                {
                    Debug.LogError($"[CompareRandomValues] Server file not found: {filePathServer}");
                    return;
                }

                string clientJson = File.ReadAllText(filePathClient);
                string serverJson = File.ReadAllText(filePathServer);

                // 2) JSON 파싱
                var clientData = JsonConvert.DeserializeObject<RandomLogData>(clientJson);
                var serverData = JsonConvert.DeserializeObject<RandomLogData>(serverJson);

                if (clientData == null || serverData == null)
                {
                    Debug.LogError("[CompareRandomValues] Failed to parse JSON log data (null).");
                    return;
                }

                // 3) 배열 길이 비교
                int countClient = clientData.randomLines.Count;
                int countServer = serverData.randomLines.Count;

                if (countClient != countServer)
                {
                    Debug.LogError($"[CompareRandomValues] Mismatch in randomValues length: client={countClient}, server={countServer}");
                    return;
                }

                // 4) 원소별 비교 (부동소수 오차 고려)
                bool allMatch = true;
                float tolerance = 1e-6f; // 정밀도 범위

                for (int i = 0; i < countClient; i++)
                {
                    string lineC = clientData.randomLines[i]; // "0.432457,2,1"
                    string lineS = serverData.randomLines[i];

                    // split
                    var tokensC = lineC.Split(',');
                    var tokensS = lineS.Split(',');

                    // 첫번째 token => randVal
                    float randValC = float.Parse(tokensC[0]);
                    float randValS = float.Parse(tokensS[0]);

                    // 난수 비교
                    float diff = Math.Abs(randValC - randValS);
                    if (diff > tolerance)
                    {
                        Debug.LogError($"[CompareRandomValues] Mismatch at line={i}, randValC={randValC}, randValS={randValS}, diff={diff}");
                        allMatch = false;
                    }

                    // 이어서 토큰 개수별 분기
                    // ex) (2개 토큰) => "RandValue,LargeType" 
                    //    (3개 토큰) => "RandValue,Tier,Grade"
                    if (tokensC.Length == 2 && tokensS.Length == 2)
                    {
                        // 단일 enum 비교
                        int enumC = int.Parse(tokensC[1]);
                        int enumS = int.Parse(tokensS[1]);

                        if (enumC != enumS)
                        {
                            Debug.LogError($"[CompareRandomValues] Enum mismatch at line={i}, clientEnum={enumC}, serverEnum={enumS}");
                            allMatch = false;
                        }
                    }
                    else if (tokensC.Length == 3 && tokensS.Length == 3)
                    {
                        // 2개 enum
                        int enumC1 = int.Parse(tokensC[1]);
                        int enumC2 = int.Parse(tokensC[2]);
                        int enumS1 = int.Parse(tokensS[1]);
                        int enumS2 = int.Parse(tokensS[2]);

                        if (enumC1 != enumS1)
                        {
                            Debug.LogError($"[CompareRandomValues] Enum1 mismatch at line={i}, client={enumC1}, server={enumS1}");
                            allMatch = false;
                        }
                        if (enumC2 != enumS2)
                        {
                            Debug.LogError($"[CompareRandomValues] Enum2 mismatch at line={i}, client={enumC2}, server={enumS2}");
                            allMatch = false;
                        }
                    }
                    else
                    {
                        Debug.LogError($"[CompareRandomValues] Token count mismatch at line={i}, clientTokens={tokensC.Length}, serverTokens={tokensS.Length}");
                        allMatch = false;
                    }
                }

                // 5) 결과 요약
                if (allMatch)
                {
                    Debug.Log($"[CompareRandomValues] Perfect match! " +
                            $"(Count={countClient}, Tolerance={tolerance})");
                }
                else
                {
                    Debug.LogError("[CompareRandomValues] Some values mismatch found above.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CompareRandomValues] CompareRandomValuesFromClientServer Error: {ex}");
            }
        }
    }
}
// BigInt 관련 타입
// 클라이언트-서버 공유 파일 (수동 동기화 필요)
// 서버: FirebaseCLI/functions/src/Data/Shared/Types/BigIntTypes.ts

using SimpleJSON;
using System;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;

namespace PX
{
    [ExcludeCodeGenerateClass]
    public struct PXBigInt
    {
        private BigInteger _value { get; set; }
        public readonly BigInteger Value => _value;
        public readonly string ValueString => _value.ToString();

        /// 서버의 PXBigInt.numberToBigInt()와 동일한 내림(Floor) 규칙:
        /// ① 소수점 이하 4자리에서 Floor
        /// ② 다시 정수에서 Floor
        private static BigInteger RoundDoubleToBigInt(double v)
        {
            // 1) 네 자리 내림
            double rounded4 = Math.Floor(v * 10_000d) / 10_000d;

            // 2) 정수 내림
            long intRounded = (long)Math.Floor(rounded4);

            return new BigInteger(intRounded);
        }

        public static PXBigInt Create(int InValue)
        {
            PXBigInt data = new PXBigInt
            {
                _value = new BigInteger(InValue),
            };

            return data;
        }

        public static PXBigInt Create(double inValue)
        {
            return new PXBigInt { _value = RoundDoubleToBigInt(inValue) };
        }

        public static PXBigInt Create(JSONNode InJson)
        {
            if (InJson == null)
                return Create(0.0);

            PXBigInt value = Create(0.0);
            value.ChangedJsonNode(InJson, ECoreDataChangeType.None);
            return value;
        }

        public static PXBigInt Create(string inValue)
        {
            PXBigInt data = new PXBigInt
            {
                _value = BigInteger.Parse(inValue),
            };

            return data;
        }

        public static PXBigInt Create(BigInteger inValue)
        {
            PXBigInt data = new PXBigInt
            {
                _value = inValue,
            };

            return data;
        }

        public void UpdateValue(string inValue)
        {
            try
            {
                _value = BigInteger.Parse(inValue);
            }
            catch (FormatException)
            {
                Debug.LogError($"Unable to convert the string '{inValue}' to a BigInteger value.");
            }
        }

        public void Add(BigInteger addValue)
        {
            _value += addValue;
        }

        public bool ChangedJsonNode(JSONNode InJson, ECoreDataChangeType InChangeType)
        {
            const string KeyValue = "Value";
            const string KeyValueString = "ValueString";
            try
            {
                if (InJson.HasKey(KeyValueString))
                {
                    JSONNode.Enumerator enumerator = InJson.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        KeyValuePair<string, JSONNode> currentData = enumerator.Current;
                        switch (currentData.Key)
                        {
                            case KeyValue:
                                {
                                    // Value 는 API 내부에서만 사용하는 bigInt다.
                                }
                                break;
                            case KeyValueString:
                                {
                                    UpdateValue(currentData.Value);
                                }
                                break;
                            default:
                                {
                                    Debug.LogError($"ParseJsonNode TypeError, PXBigInt, InDataKey = {currentData.Key}");
                                    return false;
                                }
                        }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(string.Format("FromJson Failed!! error = {0}", e.ToString()));
                return false;
            }
        }
    }
}

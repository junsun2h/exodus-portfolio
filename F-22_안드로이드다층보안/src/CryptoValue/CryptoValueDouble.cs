using SimpleJSON;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PX
{
    [ExcludeCodeGenerateClass]
    public struct CryptoValueDouble
    {
        private long encryptedValue_;  // double을 long 비트로 변환 후 암호화
        private long cryptoKey_;

        public readonly double Value => Decrypt(encryptedValue_, cryptoKey_);
        public readonly EModValueType ValueType => EModValueType.DOUBLE;

        public static CryptoValueDouble Create(double inValue)
        {
            long key = GenerateKey();
            CryptoValueDouble data = new()
            {
                cryptoKey_ = key,
                encryptedValue_ = Encrypt(inValue, key),
            };

            return data;
        }

        // 스레드 세이프 랜덤 생성기 (백그라운드 스레드에서도 사용 가능)
        [ThreadStatic] private static System.Random threadRandom_;

        /// <summary>
        /// 인스턴스별 랜덤 키 생성 (스레드 세이프)
        /// </summary>
        private static long GenerateKey()
        {
            threadRandom_ ??= new System.Random(Environment.TickCount ^ System.Threading.Thread.CurrentThread.ManagedThreadId);
            // 64비트 랜덤 키 생성 (두 개의 32비트 랜덤 값 결합)
            long high = (long)threadRandom_.Next(int.MinValue, int.MaxValue) << 32;
            long low = (long)threadRandom_.Next(int.MinValue, int.MaxValue) & 0xFFFFFFFF;
            long key = high | low;
            return key == 0 ? 0x5A3C7E1F2B4D6A8CL : key;
        }

        /// <summary>
        /// XOR 기반 암호화 (double → long 비트 변환 후 XOR)
        /// </summary>
        private static long Encrypt(double value, long key)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            return bits ^ key;
        }

        /// <summary>
        /// XOR 기반 복호화 (XOR 후 long → double 비트 변환)
        /// </summary>
        private static double Decrypt(long encryptedValue, long key)
        {
            long bits = encryptedValue ^ key;
            return BitConverter.Int64BitsToDouble(bits);
        }

        public override readonly string ToString()
        {
            return Value.ToString();
        }

        public bool ChangedJsonNode(JSONNode InJson, ECoreDataChangeType InChangeType)
        {
            const string KeyValueType = "ValueType";
            const string KeyValue = "Value";
            try
            {
                EModValueType newDatatype = EModValueType.None;
                double newDataValue = 0.0;

                if (InJson.HasKey(KeyValueType))
                {
                    JSONNode.Enumerator enumerator = InJson.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        KeyValuePair<string, JSONNode> currentData = enumerator.Current;
                        switch (currentData.Key)
                        {
                            case KeyValueType:
                                {
                                    newDatatype = (EModValueType)currentData.Value.AsInt;
                                }
                                break;
                            case KeyValue:
                                {
                                    newDataValue = currentData.Value.AsDouble;
                                }
                                break;
                            default:
                                {
                                    Debug.LogError($"ParseJsonNode TypeError, CryptoValueDouble, InDataKey = {currentData.Key}");
                                    return false;
                                }
                        }
                    }
                }
                else
                {
                    if (InJson.HasKey(KeyValue))
                    {
                        newDatatype = EModValueType.DOUBLE;
                        newDataValue = InJson[KeyValue].AsDouble;
                    }
                    else
                    {
                        Debug.LogError($"ParseJsonNode UpdateError, CryptoValueDouble, InJson = {InJson}");
                    }
                }


                if (newDatatype != EModValueType.DOUBLE)
                {
                    Debug.LogError($"ParseJsonNode TypeError, CryptoValueDouble, newDataType = {newDatatype}");
                    return false;
                }

                // 새 랜덤 키로 암호화
                cryptoKey_ = GenerateKey();
                encryptedValue_ = Encrypt(newDataValue, cryptoKey_);

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
using SimpleJSON;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PX
{
    [ExcludeCodeGenerateClass]
    public struct CryptoValueInt
    {
        private int encryptedValue_;
        private int cryptoKey_;

        public readonly int Value => Decrypt(encryptedValue_, cryptoKey_);
        public readonly EModValueType ValueType => EModValueType.INT;

        public static CryptoValueInt Create(JSONNode InJson)
        {
            if (InJson == null)
                return Create(0);

            CryptoValueInt value = Create(0);
            value.ChangedJsonNode(InJson, ECoreDataChangeType.None);
            return value;
        }

        public static CryptoValueInt Create(int inValue)
        {
            int key = GenerateKey();
            CryptoValueInt data = new()
            {
                cryptoKey_ = key,
                encryptedValue_ = Encrypt(inValue, key)
            };

            return data;
        }

        // 스레드 세이프 랜덤 생성기 (백그라운드 스레드에서도 사용 가능)
        [ThreadStatic] private static System.Random threadRandom_;

        /// <summary>
        /// 인스턴스별 랜덤 키 생성 (스레드 세이프)
        /// </summary>
        private static int GenerateKey()
        {
            threadRandom_ ??= new System.Random(Environment.TickCount ^ System.Threading.Thread.CurrentThread.ManagedThreadId);
            int key = threadRandom_.Next(int.MinValue, int.MaxValue);
            // 0은 XOR에서 무의미하므로 방지
            return key == 0 ? 0x5A3C7E1F : key;
        }

        /// <summary>
        /// XOR 기반 암호화
        /// </summary>
        private static int Encrypt(int value, int key) => value ^ key;

        /// <summary>
        /// XOR 기반 복호화
        /// </summary>
        private static int Decrypt(int value, int key) => value ^ key;

        public override readonly string ToString()
        {
            return Value.ToString();
        }

        // Define the < operator for CryptoValueInt
        public static bool operator <(CryptoValueInt a, CryptoValueInt b)
        {
            return a.Value < b.Value;
        }

        // Define the > operator for CryptoValueInt
        public static bool operator >(CryptoValueInt a, CryptoValueInt b)
        {
            return a.Value > b.Value;
        }

        public bool ChangedJsonNode(JSONNode InJson, ECoreDataChangeType InChangeType)
        {
            const string KeyValueType = "ValueType";
            const string KeyValue = "Value";
            try
            {
                EModValueType newDatatype = EModValueType.None;
                int newDataValue = 0;

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
                                    newDataValue = currentData.Value.AsInt;
                                }
                                break;
                            default:
                                {
                                    Debug.LogError($"ParseJsonNode TypeError, CryptoValueInt, InDataKey = {currentData.Key}");
                                    return false;
                                }
                        }
                    }
                }
                else
                {
                    if (InJson.HasKey(KeyValue))
                    {
                        newDatatype = EModValueType.INT;
                        newDataValue = InJson[KeyValue].AsInt;
                    }
                    else
                    {
                        Debug.LogError($"ParseJsonNode UpdateError, CryptoValueInt, InJson = {InJson}");
                        return false;
                    }
                }

                if (newDatatype != EModValueType.INT)
                {
                    Debug.LogError($"ParseJsonNode TypeError, CryptoValueInt, newDatatype = {newDatatype}");
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
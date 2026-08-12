using SimpleJSON;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PX
{
    [ExcludeCodeGenerateClass]
    public struct CryptoValueFloat
    {
        private int encryptedValue_;  // float를 int 비트로 변환 후 암호화
        private int cryptoKey_;

        public readonly float Value => Decrypt(encryptedValue_, cryptoKey_);
        public readonly EModValueType ValueType => EModValueType.FLOAT;

        public static CryptoValueFloat Create(float inValue)
        {
            int key = GenerateKey();
            CryptoValueFloat data = new()
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
            return key == 0 ? 0x5A3C7E1F : key;
        }

        /// <summary>
        /// XOR 기반 암호화 (float → int 비트 변환 후 XOR)
        /// </summary>
        private static int Encrypt(float value, int key)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            return bits ^ key;
        }

        /// <summary>
        /// XOR 기반 복호화 (XOR 후 int → float 비트 변환)
        /// </summary>
        private static float Decrypt(int encryptedValue, int key)
        {
            int bits = encryptedValue ^ key;
            return BitConverter.Int32BitsToSingle(bits);
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
                float newDataValue = 0.0f;

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
                                    newDataValue = currentData.Value.AsFloat;
                                }
                                break;
                            default:
                                {
                                    Debug.LogError($"ParseJsonNode TypeError, CryptoValueFloat, InDataKey = {currentData.Key}");
                                    return false;
                                }
                        }
                    }
                }
                else
                {
                    if (InJson.HasKey(KeyValue))
                    {
                        newDatatype = EModValueType.FLOAT;
                        newDataValue = InJson[KeyValue].AsFloat;
                    }
                    else
                    {
                        Debug.LogError($"ParseJsonNode UpdateError, CryptoValueFloat, InJson = {InJson}");
                        return false;
                    }
                }

                if (newDatatype != EModValueType.FLOAT)
                {
                    Debug.LogError($"ParseJsonNode TypeError, CryptoValueFloat, newDatatype = {newDatatype}");
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
using SimpleJSON;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PX
{
    [ExcludeCodeGenerateClass]
    public struct CryptoValueBool
    {
        private int encryptedValue_;
        private int cryptoKey_;

        public readonly bool Value => Decrypt(encryptedValue_, cryptoKey_);
        public readonly EModValueType ValueType => EModValueType.BOOL;

        public static CryptoValueBool Create(JSONNode InJson)
        {
            if (InJson == null)
                return Create(false);

            CryptoValueBool value = Create(false);
            value.ChangedJsonNode(InJson, ECoreDataChangeType.None);
            return value;
        }

        public static CryptoValueBool Create(bool inValue)
        {
            int key = GenerateKey();
            CryptoValueBool data = new()
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
        /// XOR 기반 암호화 (bool → int 변환 후 XOR)
        /// </summary>
        private static int Encrypt(bool value, int key)
        {
            int intValue = value ? 1 : 0;
            return intValue ^ key;
        }

        /// <summary>
        /// XOR 기반 복호화 (XOR 후 int → bool 변환)
        /// </summary>
        private static bool Decrypt(int encryptedValue, int key)
        {
            int intValue = encryptedValue ^ key;
            return intValue == 1;
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
                bool newDataValue = false;

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
                                    newDataValue = currentData.Value == 0 ? false : true;
                                }
                                break;
                            default:
                                {
                                    Debug.LogError($"ParseJsonNode TypeError, CryptoValueBool, InDataKey = {currentData.Key}");
                                    return false;
                                }
                        }
                    }
                }
                else
                {
                    if (InJson.HasKey(KeyValue))
                    {
                        newDatatype = EModValueType.BOOL;
                        newDataValue = InJson[KeyValue].AsBool;
                    }
                    else
                    {
                        Debug.LogError($"ParseJsonNode UpdateError, CryptoValueBool, InJson = {InJson}");
                        return false;
                    }
                }

                if (newDatatype != EModValueType.BOOL)
                {
                    Debug.LogError($"ParseJsonNode TypeError, CryptoValueBool, newDatatype = {newDatatype}");
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
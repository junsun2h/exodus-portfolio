using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PX
{
    /// <summary>
    /// AES256-CBC 암호화    
    /// 암호화방식 : AES (대칭키 : 256bit (32Bytes), IV : 128bit (16Bytes))
    /// 모드 : CBC
    /// 패딩 : PKCS7
    /// </summary>
    public class CryptoAES256CBC : IPXDisposable
    {
        public string Encrypt(string plainText, string keyString, string ivString)
        {
            try
            {
                RijndaelManaged aes = new RijndaelManaged
                {
                    KeySize = 256,
                    BlockSize = 128,
                    Mode = CipherMode.CBC,
                    Padding = PaddingMode.PKCS7,
                    Key = Convert.FromBase64String(keyString),
                    IV = Convert.FromBase64String(ivString)
                };

                var encrypt = aes.CreateEncryptor(aes.Key, aes.IV);
                byte[] xBuff = null;
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encrypt, CryptoStreamMode.Write))
                    {
                        byte[] xXml = Encoding.UTF8.GetBytes(plainText);
                        cs.Write(xXml, 0, xXml.Length);
                    }

                    xBuff = ms.ToArray();
                }

                return Convert.ToBase64String(xBuff);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public string Decrypt(string combinedString, string keyString, string ivString)
        {
            try
            {
                RijndaelManaged aes = new RijndaelManaged
                {
                    KeySize = 256,
                    BlockSize = 128,
                    Mode = CipherMode.CBC,
                    Padding = PaddingMode.PKCS7,
                    Key = Convert.FromBase64String(keyString),
                    IV = Convert.FromBase64String(ivString)
                };

                var decrypt = aes.CreateDecryptor();
                byte[] xBuff = null;
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, decrypt, CryptoStreamMode.Write))
                    {
                        byte[] xXml = Convert.FromBase64String(combinedString);
                        cs.Write(xXml, 0, xXml.Length);
                    }

                    xBuff = ms.ToArray();
                }

                return Encoding.UTF8.GetString(xBuff);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }

    public class GameCryptoManager : SingletonDependency<GameCryptoManager>
    {
        byte seedValue = 0;

        //16바이트 base64  //https://generate.plus/en/base64
        private string aesKey;
        private string aesIV;
        private CryptoAES256CBC crypto;
        
        // 초기화 상태 추적
        private bool isInitialized = false;

        private const int keySize = 16;
        byte[] keyBytes = new byte[keySize];

        private GameCryptoManager()
        {
            crypto = new CryptoAES256CBC();
        }
        ~GameCryptoManager()
        {
        }

        public override void OnDestroy()
        {
            crypto.Dispose();
        }

        public override void InitData()
        {
            base.InitData();
        }

        public string GetAESKey()
        {
            return aesKey;
        }

        public string GetAESIV()
        {
            return aesIV;
        }
        
        // 초기화 완료 여부 확인
        public bool IsInitialized => isInitialized;

        public void SetKeyAndIV(string key, string iv)
        {
            aesKey = key;
            aesIV = iv;
            isInitialized = true;
            UnityEngine.Debug.Log("[CRYPTO] 암호화 키 초기화 완료");
        }

        public string Encrypt(string text)
        {
            return crypto.Encrypt(text, aesKey, aesIV);
        }

        public string Decrypt(string encryptedText)
        {
            return crypto.Decrypt(encryptedText, aesKey, aesIV);
        }

        public string GenerateBase64Key()
        {
            for (int i = 0; i < keySize; i++)
            {
                keyBytes[i] = GetCachedRandByteByIndex();
            }
            return Convert.ToBase64String(keyBytes);
        }

        private byte GetCachedRandByteByIndex()
        {
            seedValue++;
            if (seedValue >= 255)
            {
                seedValue = 1;
            }
            return seedValue;
        }

        /*
         * 만약 AES 암호화 과정이 빈번해서 퍼포먼스상 무겁다면, 고정값이지만 단순 EncodingBase64 로 변경할수도 있음.
        public static string EncodingBase64(string plainText)
        {
            Byte[] strByte = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(strByte);
        }

        public static string DecodingBase64(string base64PlainText)
        {
            Byte[] strByte = Convert.FromBase64String(base64PlainText);
            return Encoding.UTF8.GetString(strByte);
        }
        */
    }
}

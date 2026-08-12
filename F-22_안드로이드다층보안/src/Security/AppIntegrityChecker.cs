using System;
using System.Text;
using UnityEngine;

namespace PX
{
    /// <summary>
    /// 앱 무결성 검증 유틸리티
    /// APK 서명 검증을 통한 리패키징 탐지
    ///
    /// 보안 정책: 서명 해시는 서버에서 검증 (클라이언트에 저장하지 않음)
    /// </summary>
    public static class AppIntegrityChecker
    {
        // 캐시된 서명 해시 (앱 실행 중 변경되지 않으므로 캐시)
        private static string cachedSignatureHash_ = null;

        // 서버 검증 결과
        private static bool? serverVerificationResult_ = null;

        /// <summary>
        /// 앱 서명 해시 가져오기 (서버 전송용)
        /// 서버에서 이 해시를 예상 해시와 비교하여 검증
        /// </summary>
        /// <returns>현재 앱의 서명 해시 (SHA-256)</returns>
        public static string GetSignatureHashForServerVerification()
        {
            if (cachedSignatureHash_ != null)
                return cachedSignatureHash_;

            cachedSignatureHash_ = GetCurrentAppSignatureHash();
            return cachedSignatureHash_;
        }

        /// <summary>
        /// 서버 검증 결과 설정 (서버 응답 후 호출)
        /// </summary>
        /// <param name="isValid">서버에서 검증한 결과</param>
        public static void SetServerVerificationResult(bool isValid)
        {
            serverVerificationResult_ = isValid;

            if (!isValid)
            {
                SecureDebug.LogWarning("[AppIntegrityChecker] 서버 검증 실패: APK 서명 불일치");
            }
        }

        /// <summary>
        /// 서버 검증이 완료되었는지 확인
        /// </summary>
        public static bool IsServerVerificationCompleted()
        {
            return serverVerificationResult_.HasValue;
        }

        /// <summary>
        /// 서버 검증 결과 확인
        /// </summary>
        /// <returns>검증 성공 여부 (미완료 시 true 반환)</returns>
        public static bool IsVerifiedByServer()
        {
            // 서버 검증이 아직 완료되지 않았으면 일단 허용
            if (!serverVerificationResult_.HasValue)
                return true;

            return serverVerificationResult_.Value;
        }

        /// <summary>
        /// 앱 서명 검증 (하위 호환용 - 서버 검증 결과 반환)
        /// </summary>
        /// <returns>서명이 유효하면 true</returns>
        public static bool VerifyAppSignature()
        {
#if UNITY_EDITOR
            return true;
#else
            // 서버 검증 결과 반환
            return IsVerifiedByServer();
#endif
        }

        /// <summary>
        /// 현재 앱의 서명 해시 가져오기
        /// </summary>
        /// <returns>서명 해시 문자열 (SHA-256)</returns>
        public static string GetCurrentAppSignatureHash()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject packageManager = currentActivity.Call<AndroidJavaObject>("getPackageManager"))
                {
                    string packageName = currentActivity.Call<string>("getPackageName");

                    // Android API 28 이상에서는 GET_SIGNING_CERTIFICATES 사용
                    // 하위 호환을 위해 GET_SIGNATURES도 지원
                    int getSignaturesFlag = 64; // PackageManager.GET_SIGNATURES

                    using (AndroidJavaObject packageInfo = packageManager.Call<AndroidJavaObject>(
                        "getPackageInfo", packageName, getSignaturesFlag))
                    {
                        AndroidJavaObject[] signatures = packageInfo.Get<AndroidJavaObject[]>("signatures");
                        if (signatures != null && signatures.Length > 0)
                        {
                            byte[] certBytes = signatures[0].Call<byte[]>("toByteArray");
                            string hash = ComputeSHA256Hash(certBytes);
                            SecureDebug.Log($"[AppIntegrityChecker] 현재 서명 해시: {hash}");
                            return hash;
                        }
                    }
                }

                return null;
            }
            catch (Exception e)
            {
                SecureDebug.LogWarning($"[AppIntegrityChecker] 서명 해시 조회 중 오류: {e.Message}");
                return null;
            }
#else
            return "EDITOR_OR_NON_ANDROID";
#endif
        }

        /// <summary>
        /// SHA-256 해시 계산
        /// </summary>
        private static string ComputeSHA256Hash(byte[] data)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(data);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("X2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// 캐시 초기화 (테스트용)
        /// </summary>
        public static void ClearCache()
        {
            cachedSignatureHash_ = null;
            serverVerificationResult_ = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using UnityEngine;
using UnityEngine.Networking;

namespace PX
{
    // SSL/TLS 인증서 핀닝 시스템
    // Firebase 서버 인증서 고정을 통한 MITM 공격 방지
    public static class CertificatePinning
    {
        // 핀닝 활성화 여부
        private static bool _isEnabled = true;

        // 허용된 인증서 핑거프린트 목록 (SHA-256)
        // Firebase/Google 서버 인증서 해시
        private static readonly HashSet<string> PinnedCertificateHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Google Trust Services - GTS Root R1
            "D947432ABDE7B7FA90FC2E6B59101B12F8C0A5F3FBBF6FA9E8B9F1C8FDED3D15",
            // Google Trust Services - GTS CA 1C3
            "23EC49B5E1C325C99A0758E26EF4C26E5C96B573EDEBDE74D7BF8FFF89F0B41E",
            // DigiCert Global Root G2 (Firebase 백업)
            "CB3CCBB76031E5E0138F8DD39A23F9DE47FFC35E43C1144CEA27D46A5AB1CB5F",
        };

        // 서버에서 받은 추가 인증서 해시 (동적)
        private static readonly HashSet<string> DynamicCertificateHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 핀닝 활성화/비활성화 설정
        public static void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            SecureDebug.Log($"[CertificatePinning] 핀닝 {(enabled ? "활성화" : "비활성화")}됨");
        }

        // 핀닝 활성화 여부 확인
        public static bool IsEnabled => _isEnabled;

        // 서버에서 받은 인증서 해시 추가
        public static void AddDynamicCertificateHash(string hash)
        {
            if (!string.IsNullOrEmpty(hash))
            {
                DynamicCertificateHashes.Add(hash);
                SecureDebug.Log($"[CertificatePinning] 동적 인증서 해시 추가됨: {hash.Substring(0, Math.Min(16, hash.Length))}...");
            }
        }

        // 서버에서 받은 인증서 해시 목록 설정
        public static void SetDynamicCertificateHashes(string[] hashes)
        {
            DynamicCertificateHashes.Clear();
            if (hashes != null)
            {
                foreach (string hash in hashes)
                {
                    if (!string.IsNullOrEmpty(hash))
                    {
                        DynamicCertificateHashes.Add(hash);
                    }
                }
            }
            SecureDebug.Log($"[CertificatePinning] 동적 인증서 해시 {DynamicCertificateHashes.Count}개 설정됨");
        }

        // 인증서 검증
        public static bool ValidateCertificate(X509Certificate certificate)
        {
            if (!_isEnabled)
                return true;

            if (certificate == null)
            {
                SecureDebug.LogWarning("[CertificatePinning] 인증서가 null입니다");
                return false;
            }

            try
            {
                // 인증서 SHA-256 해시 계산
                string certHash = GetCertificateHash(certificate);

                // 고정된 해시 목록에서 확인
                if (PinnedCertificateHashes.Contains(certHash))
                {
                    return true;
                }

                // 동적 해시 목록에서 확인
                if (DynamicCertificateHashes.Contains(certHash))
                {
                    return true;
                }

                SecureDebug.LogWarning($"[CertificatePinning] 인증서 검증 실패 - 해시: {certHash.Substring(0, Math.Min(16, certHash.Length))}...");
                return false;
            }
            catch (Exception e)
            {
                SecureDebug.LogWarning($"[CertificatePinning] 인증서 검증 중 오류: {e.Message}");
                return false;
            }
        }

        // 인증서의 SHA-256 해시 계산
        private static string GetCertificateHash(X509Certificate certificate)
        {
            byte[] certData = certificate.GetRawCertData();
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(certData);
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

        // UnityWebRequest용 인증서 핸들러 생성
        public static CertificateHandler CreateCertificateHandler()
        {
            return new PinnedCertificateHandler();
        }
    }

    // UnityWebRequest용 커스텀 인증서 핸들러
    public class PinnedCertificateHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            // 핀닝이 비활성화되어 있으면 모든 인증서 허용
            if (!CertificatePinning.IsEnabled)
                return true;

            if (certificateData == null || certificateData.Length == 0)
            {
                SecureDebug.LogWarning("[PinnedCertificateHandler] 인증서 데이터가 없습니다");
                return false;
            }

            try
            {
                // X509Certificate 생성
                X509Certificate cert = new X509Certificate(certificateData);

                // 핀닝 검증
                return CertificatePinning.ValidateCertificate(cert);
            }
            catch (Exception e)
            {
                SecureDebug.LogWarning($"[PinnedCertificateHandler] 인증서 검증 실패: {e.Message}");
                return false;
            }
        }
    }
}

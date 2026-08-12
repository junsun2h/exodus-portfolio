using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using UnityEngine;

namespace PX
{
    // 디바이스 보안 검사 유틸리티
    // 루팅/에뮬레이터/해킹도구/Frida/디버거 탐지
    public static class DeviceSecurityChecker
    {
        // 루팅 관련 경로
        private static readonly string[] RootPaths = new string[]
        {
            "/system/app/Superuser.apk",
            "/system/xbin/su",
            "/system/bin/su",
            "/sbin/su",
            "/data/local/xbin/su",
            "/data/local/bin/su",
            "/data/local/su",
            "/su/bin/su",
            "/system/sd/xbin/su",
            "/system/bin/failsafe/su",
            "/system/bin/.ext/.su",
            "/system/etc/.has_su_daemon",
            "/system/etc/.installed_su_daemon",
            "/dev/com.koushikdutta.superuser.daemon/",
            "/system/app/Superuser/Superuser.apk",
            "/system/etc/init.d/99teleroot",
            "/system/etc/init.d/99SuperSU",
        };

        // 루팅 관련 패키지
        private static readonly string[] RootPackages = new string[]
        {
            "com.topjohnwu.magisk",
            "com.koushikdutta.superuser",
            "com.noshufou.android.su",
            "com.thirdparty.superuser",
            "eu.chainfire.supersu",
            "com.yellowes.su",
            "com.kingroot.kinguser",
            "com.kingo.root",
            "com.smedialink.oneclickroot",
            "com.zhiqupk.root.global",
            "com.alephzain.framaroot",
        };

        // 에뮬레이터 관련 빌드 정보
        private static readonly string[] EmulatorFingerprints = new string[]
        {
            "generic",
            "unknown",
            "google_sdk",
            "Emulator",
            "Android SDK built for x86",
            "Genymotion",
            "vbox86p",
            "goldfish",
            "ranchu",
            "sdk_gphone",
            "sdk_phone",
            "BlueStacks",
            "Nox",
            "LDPlayer",
            "MEmu",
            "Andy",
            "Droid4X",
            "AMIDuOS",
        };

        // 해킹 도구 패키지 (로컬 기본 목록)
        private static readonly string[] LocalHackingToolPackages = new string[]
        {
            "com.chelpus.lackypatch",
            "com.dimonvideo.luckypatcher",
            "com.forpda.lp",
            "com.android.vending.billing.InAppBillingService.LUCK",
            "com.android.vending.billing.InAppBillingService.CRAC",
            "com.android.vending.billing.InAppBillingService.COIN",
            "com.cih.game_cih",
            "com.charles.lpoqasert",
            "catch_.me_.if_.you_.can_",
            "org.sbtools.gamehack",
            "org.cheatengine.cegui",
            "com.xmodgame",
            "com.zune.gamekiller",
            "com.killerapp.gamekiller",
            "cn.maocai.gamekiller",
            "com.github.valter",
            "cc.madkite.freedom",
            "com.devadvance.rootcloak",
            "com.devadvance.rootcloakplus",
            "de.robv.android.xposed.installer",
            "com.saurik.substrate",
        };

        // 서버에서 받은 해킹 도구 패키지 목록 (동적)
        private static string[] _serverHackingToolPackages = Array.Empty<string>();

        // Frida 관련 탐지 항목
        private static readonly string[] FridaPackages = new string[]
        {
            "re.frida.server",
        };

        private static readonly string[] FridaProcessNames = new string[]
        {
            "frida-server",
            "frida-agent",
            "frida-gadget",
            "frida-helper",
        };

        private static readonly string[] FridaFilePaths = new string[]
        {
            "/data/local/tmp/frida-server",
            "/data/local/tmp/re.frida.server",
            "/data/local/tmp/frida",
        };

        private const int FridaDefaultPort = 27042;

        /// <summary>
        /// 루팅된 기기인지 확인
        /// </summary>
        public static bool IsDeviceRooted()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // 1. 루팅 경로 확인
                foreach (string path in RootPaths)
                {
                    if (File.Exists(path))
                    {
                        SecureDebug.Log($"[DeviceSecurityChecker] 루팅 경로 탐지: {path}");
                        return true;
                    }
                }

                // 2. Build.TAGS 확인 (test-keys는 루팅된 기기에서 자주 발견)
                using (AndroidJavaClass buildClass = new AndroidJavaClass("android.os.Build"))
                {
                    string tags = buildClass.GetStatic<string>("TAGS");
                    if (!string.IsNullOrEmpty(tags) && tags.Contains("test-keys"))
                    {
                        SecureDebug.Log("[DeviceSecurityChecker] test-keys 탐지됨");
                        return true;
                    }
                }

                // 3. 루팅 패키지 확인
                foreach (string packageName in RootPackages)
                {
                    if (IsPackageInstalled(packageName))
                    {
                        SecureDebug.Log($"[DeviceSecurityChecker] 루팅 패키지 탐지: {packageName}");
                        return true;
                    }
                }

                // 4. su 명령어 실행 가능 여부 확인
                if (CanExecuteSu())
                {
                    SecureDebug.Log("[DeviceSecurityChecker] su 명령어 실행 가능");
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                SecureDebug.LogWarning($"[DeviceSecurityChecker] 루팅 탐지 중 오류: {e.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// 에뮬레이터인지 확인
        /// </summary>
        public static bool IsEmulator()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass buildClass = new AndroidJavaClass("android.os.Build"))
                {
                    // Build.FINGERPRINT
                    string fingerprint = buildClass.GetStatic<string>("FINGERPRINT");
                    foreach (string emulatorFingerprint in EmulatorFingerprints)
                    {
                        if (!string.IsNullOrEmpty(fingerprint) &&
                            fingerprint.ToLower().Contains(emulatorFingerprint.ToLower()))
                        {
                            SecureDebug.Log($"[DeviceSecurityChecker] 에뮬레이터 fingerprint 탐지: {emulatorFingerprint}");
                            return true;
                        }
                    }

                    // Build.MODEL
                    string model = buildClass.GetStatic<string>("MODEL");
                    foreach (string emulatorFingerprint in EmulatorFingerprints)
                    {
                        if (!string.IsNullOrEmpty(model) &&
                            model.ToLower().Contains(emulatorFingerprint.ToLower()))
                        {
                            SecureDebug.Log($"[DeviceSecurityChecker] 에뮬레이터 model 탐지: {model}");
                            return true;
                        }
                    }

                    // Build.MANUFACTURER
                    string manufacturer = buildClass.GetStatic<string>("MANUFACTURER");
                    foreach (string emulatorFingerprint in EmulatorFingerprints)
                    {
                        if (!string.IsNullOrEmpty(manufacturer) &&
                            manufacturer.ToLower().Contains(emulatorFingerprint.ToLower()))
                        {
                            SecureDebug.Log($"[DeviceSecurityChecker] 에뮬레이터 manufacturer 탐지: {manufacturer}");
                            return true;
                        }
                    }

                    // Build.PRODUCT
                    string product = buildClass.GetStatic<string>("PRODUCT");
                    foreach (string emulatorFingerprint in EmulatorFingerprints)
                    {
                        if (!string.IsNullOrEmpty(product) &&
                            product.ToLower().Contains(emulatorFingerprint.ToLower()))
                        {
                            SecureDebug.Log($"[DeviceSecurityChecker] 에뮬레이터 product 탐지: {product}");
                            return true;
                        }
                    }

                    // Build.HARDWARE
                    string hardware = buildClass.GetStatic<string>("HARDWARE");
                    if (!string.IsNullOrEmpty(hardware))
                    {
                        string hwLower = hardware.ToLower();
                        if (hwLower.Contains("goldfish") || hwLower.Contains("ranchu") || hwLower.Contains("vbox"))
                        {
                            SecureDebug.Log($"[DeviceSecurityChecker] 에뮬레이터 hardware 탐지: {hardware}");
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                SecureDebug.LogWarning($"[DeviceSecurityChecker] 에뮬레이터 탐지 중 오류: {e.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        // 서버에서 받은 해킹 도구 목록 설정 (GameSecurityManager에서 호출)
        public static void SetServerHackingToolPackages(string[] packages)
        {
            _serverHackingToolPackages = packages ?? Array.Empty<string>();
            SecureDebug.Log($"[DeviceSecurityChecker] 서버 해킹 도구 목록 설정됨: {_serverHackingToolPackages.Length}개");
        }

        // 해킹 도구 탐지 (로컬 + 서버 목록 병합)
        // 탐지된 도구 이름 반환, 없으면 null
        public static string DetectHackingTools()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // 로컬 목록과 서버 목록 병합
                var allPackages = new HashSet<string>(LocalHackingToolPackages);
                foreach (string pkg in _serverHackingToolPackages)
                {
                    allPackages.Add(pkg);
                }

                foreach (string packageName in allPackages)
                {
                    if (IsPackageInstalled(packageName))
                    {
                        SecureDebug.Log($"[DeviceSecurityChecker] 해킹 도구 탐지: {packageName}");
                        return packageName;
                    }
                }

                return null;
            }
            catch (Exception e)
            {
                SecureDebug.LogWarning($"[DeviceSecurityChecker] 해킹 도구 탐지 중 오류: {e.Message}");
                return null;
            }
#else
            return null;
#endif
        }

        // Frida 동적 계측 프레임워크 탐지
        public static bool IsFridaDetected()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // 1. Frida 패키지 확인
                foreach (string packageName in FridaPackages)
                {
                    if (IsPackageInstalled(packageName))
                    {
                        SecureDebug.Log($"[DeviceSecurityChecker] Frida 패키지 탐지: {packageName}");
                        return true;
                    }
                }

                // 2. Frida 파일 경로 확인
                foreach (string filePath in FridaFilePaths)
                {
                    if (File.Exists(filePath))
                    {
                        SecureDebug.Log($"[DeviceSecurityChecker] Frida 파일 탐지: {filePath}");
                        return true;
                    }
                }

                // 3. Frida 프로세스 확인
                if (IsFridaProcessRunning())
                {
                    SecureDebug.Log("[DeviceSecurityChecker] Frida 프로세스 탐지됨");
                    return true;
                }

                // 4. Frida 기본 포트(27042) 확인
                if (IsFridaPortOpen())
                {
                    SecureDebug.Log("[DeviceSecurityChecker] Frida 포트(27042) 탐지됨");
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                SecureDebug.LogWarning($"[DeviceSecurityChecker] Frida 탐지 중 오류: {e.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        // 디버거 연결 여부 탐지
        public static bool IsDebuggerAttached()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // 1. TracerPid 확인 (/proc/self/status)
                if (IsTracerPidAttached())
                {
                    SecureDebug.Log("[DeviceSecurityChecker] TracerPid 탐지: 디버거 연결됨");
                    return true;
                }

                // 2. Java 디버거 연결 확인 (android.os.Debug.isDebuggerConnected)
                if (IsJavaDebuggerConnected())
                {
                    SecureDebug.Log("[DeviceSecurityChecker] Java 디버거 연결됨");
                    return true;
                }

                // 3. waitingForDebugger 확인
                if (IsWaitingForDebugger())
                {
                    SecureDebug.Log("[DeviceSecurityChecker] 디버거 대기 상태 탐지됨");
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                SecureDebug.LogWarning($"[DeviceSecurityChecker] 디버거 탐지 중 오류: {e.Message}");
                return false;
            }
#else
            return false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Frida 프로세스가 실행 중인지 확인
        private static bool IsFridaProcessRunning()
        {
            try
            {
                // /proc 디렉토리를 스캔하여 프로세스 확인
                if (!Directory.Exists("/proc"))
                    return false;

                foreach (string dir in Directory.GetDirectories("/proc"))
                {
                    string dirName = Path.GetFileName(dir);
                    // 숫자로 된 디렉토리만 프로세스 ID
                    if (!int.TryParse(dirName, out _))
                        continue;

                    string cmdlinePath = Path.Combine(dir, "cmdline");
                    if (!File.Exists(cmdlinePath))
                        continue;

                    try
                    {
                        string cmdline = File.ReadAllText(cmdlinePath).ToLower();
                        foreach (string processName in FridaProcessNames)
                        {
                            if (cmdline.Contains(processName.ToLower()))
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // 권한 없는 프로세스는 무시
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // Frida 기본 포트(27042)가 열려있는지 확인
        private static bool IsFridaPortOpen()
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect("127.0.0.1", FridaDefaultPort, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100));
                    if (success)
                    {
                        client.EndConnect(result);
                        return true;
                    }
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        // TracerPid 확인 (ptrace 기반 디버거 탐지)
        private static bool IsTracerPidAttached()
        {
            try
            {
                string statusPath = "/proc/self/status";
                if (!File.Exists(statusPath))
                    return false;

                string[] lines = File.ReadAllLines(statusPath);
                foreach (string line in lines)
                {
                    if (line.StartsWith("TracerPid:"))
                    {
                        string value = line.Substring("TracerPid:".Length).Trim();
                        if (int.TryParse(value, out int tracerPid))
                        {
                            // TracerPid가 0이 아니면 디버거가 연결됨
                            return tracerPid != 0;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // Java 디버거 연결 확인
        private static bool IsJavaDebuggerConnected()
        {
            try
            {
                using (var debugClass = new AndroidJavaClass("android.os.Debug"))
                {
                    return debugClass.CallStatic<bool>("isDebuggerConnected");
                }
            }
            catch
            {
                return false;
            }
        }

        // 디버거 대기 상태 확인
        private static bool IsWaitingForDebugger()
        {
            try
            {
                using (var debugClass = new AndroidJavaClass("android.os.Debug"))
                {
                    return debugClass.CallStatic<bool>("waitingForDebugger");
                }
            }
            catch
            {
                return false;
            }
        }

        // 패키지 설치 여부 확인
        private static bool IsPackageInstalled(string packageName)
        {
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject packageManager = currentActivity.Call<AndroidJavaObject>("getPackageManager"))
                {
                    // getPackageInfo 호출 시 패키지가 없으면 예외 발생
                    packageManager.Call<AndroidJavaObject>("getPackageInfo", packageName, 0);
                    return true;
                }
            }
            catch (AndroidJavaException)
            {
                // 패키지가 설치되어 있지 않음
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// su 명령어 실행 가능 여부 확인
        /// </summary>
        private static bool CanExecuteSu()
        {
            try
            {
                using (AndroidJavaClass runtimeClass = new AndroidJavaClass("java.lang.Runtime"))
                using (AndroidJavaObject runtime = runtimeClass.CallStatic<AndroidJavaObject>("getRuntime"))
                {
                    using (AndroidJavaObject process = runtime.Call<AndroidJavaObject>("exec", "which su"))
                    {
                        int exitValue = process.Call<int>("waitFor");
                        return exitValue == 0;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
#endif
    }
}

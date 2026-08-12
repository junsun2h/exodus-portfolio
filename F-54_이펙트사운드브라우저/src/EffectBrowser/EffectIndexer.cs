// 이펙트 브라우저 — 인덱스 빌드 및 캐시 입출력
// 프리팹을 실제로 로드하지 않고 YAML 텍스트의 클래스 ID만 훑어서 이펙트 후보를 빠르게 추린다.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PX.EffectBrowser
{
    /// <summary>
    /// 이펙트 프리팹 인덱스를 만들고 디스크 캐시에 읽고 쓴다.
    /// </summary>
    public static class EffectIndexer
    {
        /// <summary>인덱스 파일 포맷 버전. 구조가 바뀌면 올려서 기존 캐시를 무효화한다.</summary>
        private const int IndexVersion = 1;

        /// <summary>Unity YAML 클래스 ID — 이 중 하나라도 있으면 이펙트 후보로 본다.</summary>
        private static readonly string[] EffectClassMarkers =
        {
            "!u!198 ", // ParticleSystem
            "!u!96 ",  // TrailRenderer
            "!u!120 ", // LineRenderer
        };

        /// <summary>기본 스캔 대상 폴더</summary>
        public static readonly string[] DefaultScanRoots =
        {
            "Assets/StoreAsset/Vfx",
            "Assets/GameAssets/Effect",
            "Assets/GameAssets/Projectile",
            "Assets/Piloto Studio",
        };

        #region 캐시 경로

        /// <summary>캐시 루트 (프로젝트 폴더 바로 아래 — Unity가 임포트하지 않도록 Assets 밖에 둔다)</summary>
        public static string CacheRoot
        {
            get
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                return Path.Combine(projectRoot, "EffectBrowserCache");
            }
        }

        /// <summary>썸네일 PNG 폴더</summary>
        public static string ThumbnailRoot => Path.Combine(CacheRoot, "thumbs");

        /// <summary>인덱스 JSON 경로</summary>
        public static string IndexPath => Path.Combine(CacheRoot, "index.json");

        /// <summary>캐시 폴더가 존재하도록 보장한다.</summary>
        public static void EnsureCacheDirectories()
        {
            Directory.CreateDirectory(CacheRoot);
            Directory.CreateDirectory(ThumbnailRoot);
        }

        #endregion

        #region 인덱스 빌드

        /// <summary>
        /// 지정한 폴더들을 스캔해 이펙트 인덱스를 새로 만든다.
        /// 진행률 표시줄을 띄우며, 사용자가 취소하면 null을 반환한다.
        /// </summary>
        /// <param name="scanRoots">스캔할 에셋 폴더 목록</param>
        /// <param name="includeNonParticle">파티클/트레일/라인이 없는 프리팹도 포함할지 여부</param>
        public static List<EffectEntry> BuildIndex(IEnumerable<string> scanRoots, bool includeNonParticle)
        {
            var validRoots = scanRoots
                .Where(root => !string.IsNullOrWhiteSpace(root) && AssetDatabase.IsValidFolder(root.TrimEnd('/')))
                .Select(root => root.TrimEnd('/'))
                .ToArray();

            if (validRoots.Length == 0)
            {
                Debug.LogError("[EffectBrowser] 유효한 스캔 폴더가 없습니다. 설정에서 경로를 확인하세요.");
                return null;
            }

            var guids = AssetDatabase.FindAssets("t:Prefab", validRoots);
            var entries = new List<EffectEntry>(guids.Length);

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    // 진행률 갱신은 비용이 있어 32건마다만 수행한다
                    if ((i & 31) == 0)
                    {
                        float progress = (float)i / guids.Length;
                        string title = "이펙트 인덱싱";
                        string info = $"{i:N0} / {guids.Length:N0} 검사 · {entries.Count:N0}개 발견";
                        if (EditorUtility.DisplayCancelableProgressBar(title, info, progress))
                        {
                            return null;
                        }
                    }

                    var guid = guids[i];
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath)) continue;

                    if (!includeNonParticle && !HasEffectComponent(assetPath))
                    {
                        continue;
                    }

                    entries.Add(CreateEntry(guid, assetPath, validRoots));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            entries.Sort((a, b) =>
            {
                int packageCompare = string.Compare(a.Package, b.Package, StringComparison.OrdinalIgnoreCase);
                return packageCompare != 0
                    ? packageCompare
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return entries;
        }

        /// <summary>
        /// 프리팹 파일에 파티클/트레일/라인 렌더러가 포함되어 있는지 검사한다.
        /// 텍스트 직렬화(Force Text)면 YAML 클래스 ID로 판정하고, 바이너리면 실제 로드로 폴백한다.
        /// </summary>
        private static bool HasEffectComponent(string assetPath)
        {
            try
            {
                var fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
                if (!File.Exists(fullPath))
                {
                    return false;
                }

                var text = File.ReadAllText(fullPath);
                if (text.StartsWith("%YAML", StringComparison.Ordinal))
                {
                    foreach (var marker in EffectClassMarkers)
                    {
                        if (text.IndexOf(marker, StringComparison.Ordinal) >= 0)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
            catch (IOException)
            {
                // 파일 접근 실패 시 아래 로드 경로로 폴백한다
            }

            // 바이너리 직렬화 프로젝트 대비 폴백 — 느리지만 정확하다
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) return false;

            return prefab.GetComponentInChildren<ParticleSystem>(true) != null
                   || prefab.GetComponentInChildren<TrailRenderer>(true) != null
                   || prefab.GetComponentInChildren<LineRenderer>(true) != null;
        }

        /// <summary>인덱스 엔트리 1건을 만든다.</summary>
        private static EffectEntry CreateEntry(string guid, string assetPath, string[] scanRoots)
        {
            var entry = new EffectEntry
            {
                Guid = guid,
                Path = assetPath,
                Name = Path.GetFileNameWithoutExtension(assetPath),
                Package = ResolvePackage(assetPath, scanRoots),
                IsGameAsset = assetPath.StartsWith("Assets/GameAssets/", StringComparison.OrdinalIgnoreCase),
            };
            entry.Categories = EffectCategoryClassifier.Classify(entry.Name, assetPath);
            entry.RebuildSearchKey();
            return entry;
        }

        /// <summary>
        /// 에셋 경로에서 패키지명을 추론한다 — 스캔 루트 바로 아래 폴더명을 쓴다.
        /// 루트 바로 아래에 놓인 프리팹은 루트 폴더명 자체를 패키지로 삼는다.
        /// </summary>
        private static string ResolvePackage(string assetPath, string[] scanRoots)
        {
            foreach (var root in scanRoots)
            {
                if (!assetPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) continue;

                var relative = assetPath.Substring(root.Length + 1);
                int slash = relative.IndexOf('/');
                return slash > 0
                    ? relative.Substring(0, slash)
                    : Path.GetFileName(root);
            }

            // 스캔 루트에 속하지 않는 예외 경로 — 상위 폴더명으로 대체
            return Path.GetFileName(Path.GetDirectoryName(assetPath) ?? "Unknown");
        }

        #endregion

        #region 사용처 분석

        /// <summary>
        /// 게임 에셋(Assets/GameAssets, Assets/Resources, Assets/Scenes)이 참조하는 이펙트를 표시한다.
        /// 의존성 조회가 무거우므로 사용자가 명시적으로 실행할 때만 호출한다.
        /// </summary>
        /// <returns>참조된 것으로 표시된 엔트리 수. 취소 시 -1</returns>
        public static int AnalyzeReferences(List<EffectEntry> entries)
        {
            if (entries == null || entries.Count == 0) return 0;

            var searchFolders = new[] { "Assets/GameAssets", "Assets/Resources", "Assets/Scenes" }
                .Where(AssetDatabase.IsValidFolder)
                .ToArray();

            if (searchFolders.Length == 0) return 0;

            // 게임 쪽 에셋에서 출발한 전체 의존성 집합을 만든다
            var consumerGuids = AssetDatabase.FindAssets("t:Prefab t:Scene t:ScriptableObject", searchFolders);
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                for (int i = 0; i < consumerGuids.Length; i++)
                {
                    if ((i & 15) == 0)
                    {
                        float progress = (float)i / consumerGuids.Length;
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "사용처 분석",
                                $"{i:N0} / {consumerGuids.Length:N0} 에셋의 의존성 수집 중",
                                progress))
                        {
                            return -1;
                        }
                    }

                    var consumerPath = AssetDatabase.GUIDToAssetPath(consumerGuids[i]);
                    if (string.IsNullOrEmpty(consumerPath)) continue;

                    foreach (var dependency in AssetDatabase.GetDependencies(consumerPath, true))
                    {
                        // GetDependencies는 대상 자신도 포함한다.
                        // 그대로 담으면 GameAssets 하위 이펙트가 전부 "참조됨"으로 잡혀 의미가 없어진다.
                        if (string.Equals(dependency, consumerPath, StringComparison.OrdinalIgnoreCase)) continue;

                        referenced.Add(dependency);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            int count = 0;
            foreach (var entry in entries)
            {
                entry.IsReferenced = referenced.Contains(entry.Path);
                if (entry.IsReferenced) count++;
            }
            return count;
        }

        #endregion

        #region 캐시 입출력

        [Serializable]
        private class IndexFile
        {
            public int Version;
            public string BuiltAt;
            public List<EffectEntry> Entries;
        }

        /// <summary>인덱스를 디스크에 저장한다.</summary>
        public static void SaveIndex(List<EffectEntry> entries)
        {
            EnsureCacheDirectories();

            var file = new IndexFile
            {
                Version = IndexVersion,
                BuiltAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Entries = entries,
            };

            File.WriteAllText(IndexPath, JsonUtility.ToJson(file));
        }

        /// <summary>디스크에서 인덱스를 읽는다. 없거나 버전이 다르면 null.</summary>
        public static List<EffectEntry> LoadIndex()
        {
            if (!File.Exists(IndexPath)) return null;

            try
            {
                var file = JsonUtility.FromJson<IndexFile>(File.ReadAllText(IndexPath));
                if (file == null || file.Version != IndexVersion || file.Entries == null)
                {
                    return null;
                }

                foreach (var entry in file.Entries)
                {
                    entry.RebuildSearchKey();
                }

                // 캡처 도중 도메인 리로드나 중단이 있었으면 JSON의 Captured 플래그가 뒤처진다.
                // 실제로 존재하는 썸네일 파일이 진실이므로 그쪽에 맞춘다.
                SyncCapturedFlags(file.Entries);
                return file.Entries;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EffectBrowser] 인덱스 파일을 읽지 못했습니다: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 디스크에 실제로 있는 썸네일에 맞춰 각 엔트리의 Captured 플래그를 맞춘다.
        /// 파일을 하나씩 File.Exists로 확인하면 수천 건에서 느리므로 목록을 한 번만 읽는다.
        /// </summary>
        public static void SyncCapturedFlags(List<EffectEntry> entries)
        {
            if (entries == null) return;

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(ThumbnailRoot))
            {
                foreach (var file in Directory.GetFiles(ThumbnailRoot, "*.png", SearchOption.TopDirectoryOnly))
                {
                    existing.Add(Path.GetFileNameWithoutExtension(file));
                }
            }

            foreach (var entry in entries)
            {
                entry.Captured = existing.Contains(entry.Guid);
            }
        }

        /// <summary>인덱스가 만들어진 시각 문자열. 없으면 빈 문자열.</summary>
        public static string GetIndexTimestamp()
        {
            if (!File.Exists(IndexPath)) return string.Empty;

            try
            {
                var file = JsonUtility.FromJson<IndexFile>(File.ReadAllText(IndexPath));
                return file?.BuiltAt ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        #endregion
    }
}

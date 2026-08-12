// 사운드 브라우저 — 인덱스 빌드 및 캐시 입출력
// AudioClip 메타(길이/채널/주파수)와 임포터 설정을 모아 목록을 만든다.
// 파형 추출은 비용이 크므로 여기서 하지 않고 SoundAnalysisBatcher가 따로 처리한다.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PX.SoundBrowser
{
    /// <summary>
    /// 오디오 클립 인덱스를 만들고 디스크 캐시에 읽고 쓴다.
    /// </summary>
    public static class SoundIndexer
    {
        /// <summary>인덱스 파일 포맷 버전. 구조가 바뀌면 올려서 기존 캐시를 무효화한다.</summary>
        private const int IndexVersion = 1;

        /// <summary>이 건수마다 로드한 클립을 정리한다. 누적되면 에디터 메모리가 계속 늘어난다.</summary>
        private const int UnloadInterval = 200;

        /// <summary>기본 스캔 대상 폴더</summary>
        public static readonly string[] DefaultScanRoots =
        {
            "Assets",
        };

        #region 캐시 경로

        /// <summary>캐시 루트 (프로젝트 폴더 바로 아래 — Unity가 임포트하지 않도록 Assets 밖에 둔다)</summary>
        public static string CacheRoot
        {
            get
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                return Path.Combine(projectRoot, "SoundBrowserCache");
            }
        }

        /// <summary>인덱스 JSON 경로</summary>
        public static string IndexPath => Path.Combine(CacheRoot, "index.json");

        /// <summary>캐시 폴더가 존재하도록 보장한다.</summary>
        public static void EnsureCacheDirectories()
        {
            Directory.CreateDirectory(CacheRoot);
        }

        #endregion

        #region 인덱스 빌드

        /// <summary>
        /// 지정한 폴더들을 스캔해 사운드 인덱스를 새로 만든다.
        /// 진행률 표시줄을 띄우며, 사용자가 취소하면 null을 반환한다.
        /// </summary>
        /// <param name="scanRoots">스캔할 에셋 폴더 목록</param>
        /// <param name="excludePatterns">경로에 이 문자열이 포함되면 제외 (대소문자 무시)</param>
        public static List<SoundEntry> BuildIndex(IEnumerable<string> scanRoots, IEnumerable<string> excludePatterns)
        {
            var validRoots = scanRoots
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Select(root => root.Trim().TrimEnd('/'))
                .Where(root => string.Equals(root, "Assets", StringComparison.OrdinalIgnoreCase) || AssetDatabase.IsValidFolder(root))
                .ToArray();

            if (validRoots.Length == 0)
            {
                Debug.LogError("[SoundBrowser] 유효한 스캔 폴더가 없습니다. 설정에서 경로를 확인하세요.");
                return null;
            }

            var excludes = excludePatterns?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToArray() ?? Array.Empty<string>();

            var guids = AssetDatabase.FindAssets("t:AudioClip", validRoots);
            var entries = new List<SoundEntry>(guids.Length);
            int sinceUnload = 0;

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    // 진행률 갱신은 비용이 있어 16건마다만 수행한다
                    if ((i & 15) == 0)
                    {
                        float progress = (float)i / guids.Length;
                        string info = $"{i:N0} / {guids.Length:N0} 검사 · {entries.Count:N0}개 수집";
                        if (EditorUtility.DisplayCancelableProgressBar("사운드 인덱싱", info, progress))
                        {
                            return null;
                        }
                    }

                    var guid = guids[i];
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath)) continue;
                    if (IsExcluded(assetPath, excludes)) continue;

                    entries.Add(CreateEntry(guid, assetPath));

                    if (++sinceUnload >= UnloadInterval)
                    {
                        sinceUnload = 0;
                        EditorUtility.UnloadUnusedAssetsImmediate();
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.UnloadUnusedAssetsImmediate();
            }

            entries.Sort((a, b) =>
            {
                int folderCompare = string.Compare(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase);
                return folderCompare != 0
                    ? folderCompare
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return entries;
        }

        private static bool IsExcluded(string assetPath, string[] excludes)
        {
            foreach (var pattern in excludes)
            {
                if (assetPath.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>인덱스 엔트리 1건을 만든다. 클립과 임포터를 읽어 메타를 채운다.</summary>
        private static SoundEntry CreateEntry(string guid, string assetPath)
        {
            var entry = new SoundEntry
            {
                Guid = guid,
                Path = assetPath,
                Name = Path.GetFileNameWithoutExtension(assetPath),
                Folder = GetFolder(assetPath),
                Extension = (Path.GetExtension(assetPath) ?? string.Empty).TrimStart('.').ToLowerInvariant(),
                IsGameAsset = assetPath.StartsWith("Assets/GameAssets/", StringComparison.OrdinalIgnoreCase),
            };

            entry.Categories = SoundCategoryClassifier.Classify(entry.Name, assetPath);
            entry.FileSize = GetFileSize(assetPath);

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            if (clip != null)
            {
                entry.Length = clip.length;
                entry.Channels = clip.channels;
                entry.Frequency = clip.frequency;
                entry.Samples = clip.samples;
            }

            if (AssetImporter.GetAtPath(assetPath) is AudioImporter importer)
            {
                var settings = importer.defaultSampleSettings;
                entry.LoadType = settings.loadType.ToString();
                entry.Compression = settings.compressionFormat.ToString();
                entry.ForceToMono = importer.forceToMono;
            }

            entry.RebuildSearchKey();
            return entry;
        }

        private static string GetFolder(string assetPath)
        {
            int slash = assetPath.LastIndexOf('/');
            return slash > 0 ? assetPath.Substring(0, slash) : assetPath;
        }

        private static long GetFileSize(string assetPath)
        {
            try
            {
                var fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
                var info = new FileInfo(fullPath);
                return info.Exists ? info.Length : 0L;
            }
            catch (Exception)
            {
                return 0L;
            }
        }

        #endregion

        #region 사용처 분석

        /// <summary>
        /// 게임 에셋(Assets/GameAssets, Assets/Resources, Assets/Scenes)이 참조하는 사운드를 표시한다.
        /// 의존성 조회가 무거우므로 사용자가 명시적으로 실행할 때만 호출한다.
        /// </summary>
        /// <returns>참조된 것으로 표시된 엔트리 수. 취소 시 -1</returns>
        public static int AnalyzeReferences(List<SoundEntry> entries)
        {
            if (entries == null || entries.Count == 0) return 0;

            var searchFolders = new[] { "Assets/GameAssets", "Assets/Resources", "Assets/Scenes" }
                .Where(AssetDatabase.IsValidFolder)
                .ToArray();

            if (searchFolders.Length == 0) return 0;

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
                        // GetDependencies는 대상 자신도 포함하므로 걸러낸다
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
            public List<SoundEntry> Entries;
        }

        /// <summary>인덱스를 디스크에 저장한다.</summary>
        public static void SaveIndex(List<SoundEntry> entries)
        {
            EnsureCacheDirectories();

            var file = new IndexFile
            {
                Version = IndexVersion,
                BuiltAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Entries = entries,
            };

            try
            {
                File.WriteAllText(IndexPath, JsonUtility.ToJson(file));
            }
            catch (IOException e)
            {
                Debug.LogError($"[SoundBrowser] 인덱스 저장 실패: {e.Message}");
            }
        }

        /// <summary>디스크에서 인덱스를 읽는다. 없거나 버전이 다르면 null.</summary>
        public static List<SoundEntry> LoadIndex()
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
                return file.Entries;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SoundBrowser] 인덱스 파일을 읽지 못했습니다: {e.Message}");
                return null;
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

        /// <summary>인덱스 파일을 지운다.</summary>
        public static void DeleteIndex()
        {
            try
            {
                if (File.Exists(IndexPath))
                {
                    File.Delete(IndexPath);
                }
            }
            catch (IOException e)
            {
                Debug.LogError($"[SoundBrowser] 인덱스 삭제 실패: {e.Message}");
            }
        }

        #endregion
    }
}

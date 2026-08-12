// 이펙트 브라우저 — 썸네일 캐시
// PNG는 프로젝트 밖(EffectBrowserCache/thumbs)에 두고, 메모리에는 최근 사용분만 LRU로 유지한다.
// 수천 장을 전부 메모리에 올리면 GB 단위가 되므로 상한을 두는 것이 핵심이다.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PX.EffectBrowser
{
    /// <summary>
    /// 썸네일 PNG의 저장/로드와 메모리 캐시를 담당한다.
    /// </summary>
    [InitializeOnLoad]
    public static class EffectThumbnailCache
    {
        /// <summary>메모리에 동시에 유지할 최대 텍스처 수</summary>
        private const int MaxCachedTextures = 512;

        /// <summary>한 프레임에 새로 디스크에서 읽을 최대 장수. 스크롤 중 끊김을 막는다.</summary>
        private const int MaxLoadsPerFrame = 24;

        /// <summary>썸네일 한 변의 픽셀 크기</summary>
        public const int ThumbnailSize = 192;

        private static readonly Dictionary<string, LinkedListNode<CacheItem>> Lookup =
            new Dictionary<string, LinkedListNode<CacheItem>>();

        /// <summary>앞쪽이 최근 사용분</summary>
        private static readonly LinkedList<CacheItem> RecentlyUsed = new LinkedList<CacheItem>();

        /// <summary>로드에 실패한 GUID — 매 프레임 재시도하지 않도록 기억한다</summary>
        private static readonly HashSet<string> FailedGuids = new HashSet<string>();

        private static int _loadsThisFrame;

        private class CacheItem
        {
            public string Guid;
            public Texture2D Texture;
        }

        static EffectThumbnailCache()
        {
            // 도메인 리로드 시 static 참조가 사라지면 텍스처가 누수되므로 미리 정리한다
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
        }

        #region 프레임 예산

        /// <summary>매 OnGUI 시작 시 호출해 프레임당 로드 예산을 초기화한다.</summary>
        public static void BeginFrame()
        {
            _loadsThisFrame = 0;
        }

        /// <summary>이번 프레임에 아직 로드 여력이 남아 있는지</summary>
        public static bool HasLoadBudget => _loadsThisFrame < MaxLoadsPerFrame;

        #endregion

        #region 조회

        /// <summary>
        /// 썸네일을 가져온다. 메모리에 없으면 디스크에서 읽는다.
        /// 프레임 로드 예산을 초과했거나 파일이 없으면 null을 반환한다.
        /// </summary>
        public static Texture2D Get(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;

            if (Lookup.TryGetValue(guid, out var node))
            {
                // 텍스처가 외부에서 파괴되었을 수 있으므로 확인한다
                if (node.Value.Texture != null)
                {
                    RecentlyUsed.Remove(node);
                    RecentlyUsed.AddFirst(node);
                    return node.Value.Texture;
                }

                RecentlyUsed.Remove(node);
                Lookup.Remove(guid);
            }

            if (FailedGuids.Contains(guid)) return null;
            if (_loadsThisFrame >= MaxLoadsPerFrame) return null;

            var texture = LoadFromDisk(guid);
            _loadsThisFrame++;

            if (texture == null)
            {
                FailedGuids.Add(guid);
                return null;
            }

            Insert(guid, texture);
            return texture;
        }

        /// <summary>디스크에 썸네일 파일이 존재하는지 확인한다.</summary>
        public static bool ExistsOnDisk(string guid)
        {
            return !string.IsNullOrEmpty(guid) && File.Exists(GetThumbnailPath(guid));
        }

        /// <summary>GUID에 대응하는 썸네일 PNG 경로</summary>
        public static string GetThumbnailPath(string guid)
        {
            return Path.Combine(EffectIndexer.ThumbnailRoot, guid + ".png");
        }

        private static Texture2D LoadFromDisk(string guid)
        {
            var path = GetThumbnailPath(guid);
            if (!File.Exists(path)) return null;

            try
            {
                var bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };

                if (!texture.LoadImage(bytes))
                {
                    Object.DestroyImmediate(texture);
                    return null;
                }
                return texture;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static void Insert(string guid, Texture2D texture)
        {
            var node = RecentlyUsed.AddFirst(new CacheItem { Guid = guid, Texture = texture });
            Lookup[guid] = node;

            while (RecentlyUsed.Count > MaxCachedTextures)
            {
                var oldest = RecentlyUsed.Last;
                RecentlyUsed.RemoveLast();
                Lookup.Remove(oldest.Value.Guid);

                if (oldest.Value.Texture != null)
                {
                    Object.DestroyImmediate(oldest.Value.Texture);
                }
            }
        }

        #endregion

        #region 저장/무효화

        /// <summary>썸네일을 PNG로 저장한다.</summary>
        public static void Save(string guid, Texture2D texture)
        {
            if (string.IsNullOrEmpty(guid) || texture == null) return;

            EffectIndexer.EnsureCacheDirectories();

            try
            {
                File.WriteAllBytes(GetThumbnailPath(guid), texture.EncodeToPNG());
                FailedGuids.Remove(guid);
                Invalidate(guid);
            }
            catch (IOException e)
            {
                Debug.LogError($"[EffectBrowser] 썸네일 저장 실패 ({guid}): {e.Message}");
            }
        }

        /// <summary>메모리 캐시에서 해당 항목만 제거한다 (다음 조회 시 디스크에서 다시 읽는다).</summary>
        public static void Invalidate(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return;

            FailedGuids.Remove(guid);

            if (!Lookup.TryGetValue(guid, out var node)) return;

            RecentlyUsed.Remove(node);
            Lookup.Remove(guid);

            if (node.Value.Texture != null)
            {
                Object.DestroyImmediate(node.Value.Texture);
            }
        }

        /// <summary>메모리 캐시를 모두 비운다 (디스크 파일은 유지).</summary>
        public static void Clear()
        {
            foreach (var item in RecentlyUsed)
            {
                if (item.Texture != null)
                {
                    Object.DestroyImmediate(item.Texture);
                }
            }
            RecentlyUsed.Clear();
            Lookup.Clear();
            FailedGuids.Clear();
        }

        /// <summary>디스크의 썸네일 파일을 모두 삭제한다.</summary>
        public static void DeleteAllOnDisk()
        {
            Clear();

            if (!Directory.Exists(EffectIndexer.ThumbnailRoot)) return;

            try
            {
                Directory.Delete(EffectIndexer.ThumbnailRoot, true);
                EffectIndexer.EnsureCacheDirectories();
            }
            catch (IOException e)
            {
                Debug.LogError($"[EffectBrowser] 썸네일 폴더 삭제 실패: {e.Message}");
            }
        }

        /// <summary>디스크에 저장된 썸네일 장수</summary>
        public static int CountOnDisk()
        {
            if (!Directory.Exists(EffectIndexer.ThumbnailRoot)) return 0;
            return Directory.GetFiles(EffectIndexer.ThumbnailRoot, "*.png", SearchOption.TopDirectoryOnly).Length;
        }

        #endregion
    }
}

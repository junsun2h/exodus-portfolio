// 사운드 브라우저 — 파형 텍스처 메모리 캐시
// 엔벨로프는 인덱스에 들어 있으므로 디스크 캐시는 필요 없다.
// 다만 매 프레임 텍스처를 새로 구우면 GC가 튀므로 최근 사용분만 LRU로 들고 있는다.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PX.SoundBrowser
{
    /// <summary>
    /// 엔벨로프에서 구운 파형 텍스처를 캐시한다.
    /// </summary>
    [InitializeOnLoad]
    public static class SoundWaveformCache
    {
        /// <summary>메모리에 동시에 유지할 최대 텍스처 수</summary>
        private const int MaxCachedTextures = 400;

        /// <summary>리스트용 파형 텍스처 높이 (행 높이보다 크게 잡아 축소 시 계단이 덜 보이게 한다)</summary>
        private const int ListTextureHeight = 48;

        /// <summary>한 프레임에 새로 구울 최대 텍스처 수. 빠르게 스크롤할 때 프레임이 튀는 것을 막는다.</summary>
        private const int MaxBuildsPerFrame = 24;

        private static int _buildsThisFrame;

        private static readonly Dictionary<string, LinkedListNode<CacheItem>> Lookup =
            new Dictionary<string, LinkedListNode<CacheItem>>();

        /// <summary>앞쪽이 최근 사용분</summary>
        private static readonly LinkedList<CacheItem> RecentlyUsed = new LinkedList<CacheItem>();

        private class CacheItem
        {
            public string Guid;
            public Texture2D Texture;
        }

        static SoundWaveformCache()
        {
            // 도메인 리로드 시 static 참조가 사라지면 텍스처가 누수되므로 미리 정리한다
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
        }

        /// <summary>매 OnGUI 시작 시 호출해 프레임당 생성 예산을 초기화한다.</summary>
        public static void BeginFrame()
        {
            _buildsThisFrame = 0;
        }

        /// <summary>
        /// 리스트에 그릴 파형 텍스처를 가져온다. 엔벨로프가 없으면 null.
        /// 이번 프레임의 생성 예산을 다 썼으면 아직 캐시에 없는 항목은 null을 돌려준다.
        /// </summary>
        public static Texture2D Get(SoundEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Guid)) return null;

            if (Lookup.TryGetValue(entry.Guid, out var node))
            {
                if (node.Value.Texture != null)
                {
                    RecentlyUsed.Remove(node);
                    RecentlyUsed.AddFirst(node);
                    return node.Value.Texture;
                }

                // 텍스처가 외부에서 파괴되었으면 항목을 버리고 다시 굽는다
                RecentlyUsed.Remove(node);
                Lookup.Remove(entry.Guid);
            }

            if (_buildsThisFrame >= MaxBuildsPerFrame) return null;

            var envelope = entry.GetEnvelope();
            if (envelope == null || envelope.Length == 0) return null;

            var texture = SoundWaveform.BuildTexture(envelope, ListTextureHeight);
            if (texture == null) return null;

            _buildsThisFrame++;

            Insert(entry.Guid, texture);
            return texture;
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

        /// <summary>해당 항목만 캐시에서 지운다 (다음 조회 시 다시 굽는다).</summary>
        public static void Invalidate(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return;
            if (!Lookup.TryGetValue(guid, out var node)) return;

            RecentlyUsed.Remove(node);
            Lookup.Remove(guid);

            if (node.Value.Texture != null)
            {
                Object.DestroyImmediate(node.Value.Texture);
            }
        }

        /// <summary>캐시를 모두 비운다.</summary>
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
        }
    }
}

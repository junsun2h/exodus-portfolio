// 사운드 브라우저 — 파형 엔벨로프 추출 및 텍스처 생성
// 파형은 PNG로 굽지 않고 0~255 바이트 엔벨로프로 인덱스에 담는다.
// 리스트 한 행에 수백 개의 사각형을 그리면 스크롤이 멈추므로, 엔벨로프를 알파 마스크 텍스처로 구워 한 번에 그린다.

using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PX.SoundBrowser
{
    /// <summary>
    /// AudioClip에서 파형 엔벨로프를 뽑고, 그것을 그릴 텍스처로 굽는다.
    /// </summary>
    public static class SoundWaveform
    {
        /// <summary>인덱스에 저장할 엔벨로프 해상도 (리스트의 미니 파형용)</summary>
        public const int ListResolution = 192;

        /// <summary>프리뷰 패널의 큰 파형 해상도</summary>
        public const int PreviewResolution = 1024;

        /// <summary>GetData 폴백에서 한 번에 읽을 프레임 수. 긴 BGM을 통째로 올리지 않기 위한 청크 크기</summary>
        private const int ChunkFrames = 1 << 15;

        #region 엔벨로프 추출

        /// <summary>
        /// 클립의 진폭 엔벨로프를 뽑는다.
        /// 에디터가 인스펙터 파형용으로 미리 계산해 둔 데이터를 먼저 쓰고, 없으면 샘플을 직접 읽는다.
        /// </summary>
        /// <param name="clip">대상 클립</param>
        /// <param name="resolution">엔벨로프 칸 수</param>
        /// <param name="peak">최대 진폭 (0~1)</param>
        /// <param name="rms">평균 진폭 (0~1)</param>
        /// <returns>0~255 엔벨로프. 샘플을 읽을 수 없으면 null</returns>
        public static byte[] ExtractEnvelope(AudioClip clip, int resolution, out float peak, out float rms)
        {
            peak = 0f;
            rms = 0f;

            if (clip == null || resolution <= 0) return null;

            var fromEditorData = ExtractFromMinMaxData(clip, resolution, ref peak, ref rms);
            if (fromEditorData != null) return fromEditorData;

            return ExtractFromSamples(clip, resolution, ref peak, ref rms);
        }

        /// <summary>
        /// UnityEditor.AudioUtil.GetMinMaxData가 돌려주는 미리 계산된 파형을 이용한다.
        /// 압축·스트리밍 클립에서도 동작하고 GetData보다 훨씬 빠르다.
        /// </summary>
        private static byte[] ExtractFromMinMaxData(AudioClip clip, int resolution, ref float peak, ref float rms)
        {
            var minMax = SoundEditorAudio.GetMinMaxData(clip);
            if (minMax == null || minMax.Length < 2) return null;

            var envelope = new byte[resolution];
            double sumSquares = 0.0;
            int count = 0;

            // min/max 쌍이 채널 수만큼 인터리브되어 있다.
            // 칸별로 절댓값 최대만 취하므로 채널 배치 순서와 무관하게 같은 결과가 나온다.
            for (int bucket = 0; bucket < resolution; bucket++)
            {
                int start = (int)((long)bucket * minMax.Length / resolution);
                int end = (int)((long)(bucket + 1) * minMax.Length / resolution);
                if (end <= start) end = Mathf.Min(start + 1, minMax.Length);

                float max = 0f;
                for (int i = start; i < end; i++)
                {
                    float value = Mathf.Abs(minMax[i]);
                    if (value > max) max = value;

                    sumSquares += value * (double)value;
                    count++;
                }

                if (max > peak) peak = max;
                envelope[bucket] = ToByte(max);
            }

            // 구간 극값만 가진 데이터라 실제 RMS보다 크게 나온다. 클립끼리 음량을 견주는 용도로만 쓴다.
            rms = count > 0 ? (float)Math.Sqrt(sumSquares / count) : 0f;
            return envelope;
        }

        /// <summary>
        /// 샘플을 직접 읽어 엔벨로프를 만든다.
        /// 로드 방식에 따라 실패할 수 있으므로(스트리밍 등) 실패 시 null을 돌려준다.
        /// </summary>
        private static byte[] ExtractFromSamples(AudioClip clip, int resolution, ref float peak, ref float rms)
        {
            int frames = clip.samples;
            int channels = clip.channels;
            if (frames <= 0 || channels <= 0) return null;

            // 압축/스트리밍 클립은 데이터가 올라와 있어야 GetData가 성공한다
            if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData())
            {
                return null;
            }

            var envelope = new byte[resolution];
            var buffer = new float[Mathf.Min(ChunkFrames, frames) * channels];
            double sumSquares = 0.0;
            long count = 0;
            bool readAny = false;

            for (int offset = 0; offset < frames; offset += ChunkFrames)
            {
                int chunk = Mathf.Min(ChunkFrames, frames - offset);
                if (!clip.GetData(buffer, offset))
                {
                    // 한 번도 못 읽었으면 파형을 만들 수 없다. 중간에 실패했으면 읽은 만큼만 쓴다.
                    if (!readAny) return null;
                    break;
                }
                readAny = true;

                for (int frame = 0; frame < chunk; frame++)
                {
                    int bucket = (int)((long)(offset + frame) * resolution / frames);
                    if (bucket >= resolution) bucket = resolution - 1;

                    float max = 0f;
                    int baseIndex = frame * channels;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        float value = Mathf.Abs(buffer[baseIndex + channel]);
                        if (value > max) max = value;
                    }

                    sumSquares += max * (double)max;
                    count++;

                    if (max > peak) peak = max;

                    byte encoded = ToByte(max);
                    if (encoded > envelope[bucket])
                    {
                        envelope[bucket] = encoded;
                    }
                }
            }

            if (!readAny) return null;

            rms = count > 0 ? (float)Math.Sqrt(sumSquares / count) : 0f;
            return envelope;
        }

        private static byte ToByte(float amplitude)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(amplitude) * 255f), 0, 255);
        }

        #endregion

        #region 텍스처 생성

        /// <summary>
        /// 엔벨로프를 알파 마스크 텍스처로 굽는다.
        /// 색은 굽지 않고 흰색 + 알파로만 남겨, 그릴 때 GUI.color로 자유롭게 물들인다.
        /// </summary>
        /// <param name="envelope">0~255 엔벨로프</param>
        /// <param name="height">텍스처 높이 (픽셀)</param>
        public static Texture2D BuildTexture(byte[] envelope, int height)
        {
            if (envelope == null || envelope.Length == 0 || height <= 0) return null;

            int width = envelope.Length;
            var pixels = new Color32[width * height];

            var filled = new Color32(255, 255, 255, 255);
            var empty = new Color32(255, 255, 255, 0);

            float center = (height - 1) * 0.5f;

            for (int x = 0; x < width; x++)
            {
                // 무음 구간도 가느다란 중심선이 남도록 최소 반높이를 준다
                float halfHeight = Mathf.Max(envelope[x] / 255f * center, 0.5f);
                int top = Mathf.Clamp(Mathf.RoundToInt(center + halfHeight), 0, height - 1);
                int bottom = Mathf.Clamp(Mathf.RoundToInt(center - halfHeight), 0, height - 1);

                for (int y = 0; y < height; y++)
                {
                    pixels[y * width + x] = y >= bottom && y <= top ? filled : empty;
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        #endregion
    }

    /// <summary>
    /// UnityEditor 내부의 AudioUtil을 리플렉션으로 감싼다.
    /// 공개 API가 없어 어쩔 수 없이 내부 클래스에 의존하므로, 시그니처가 바뀌어도
    /// 예외 없이 기능만 빠지도록 모든 호출을 방어적으로 감쌌다.
    /// </summary>
    public static class SoundEditorAudio
    {
        private static readonly Type AudioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

        private static readonly MethodInfo PlayMethod = FindMethod(
            new[] { "PlayPreviewClip", "PlayClip" },
            new[] { typeof(AudioClip), typeof(int), typeof(bool) });

        private static readonly MethodInfo PlaySimpleMethod = FindMethod(
            new[] { "PlayPreviewClip", "PlayClip" },
            new[] { typeof(AudioClip) });

        private static readonly MethodInfo StopMethod = FindMethod(
            new[] { "StopAllPreviewClips", "StopAllClips" },
            Type.EmptyTypes);

        private static readonly MethodInfo PauseMethod = FindMethod(
            new[] { "PausePreviewClip", "PauseClip" },
            Type.EmptyTypes);

        private static readonly MethodInfo ResumeMethod = FindMethod(
            new[] { "ResumePreviewClip", "ResumeClip" },
            Type.EmptyTypes);

        private static readonly MethodInfo LoopMethod = FindMethod(
            new[] { "LoopPreviewClip", "LoopClip" },
            new[] { typeof(bool) });

        private static readonly MethodInfo IsPlayingMethod = FindMethod(
            new[] { "IsPreviewClipPlaying", "IsClipPlaying" },
            Type.EmptyTypes);

        private static readonly MethodInfo GetPositionMethod = FindMethod(
            new[] { "GetPreviewClipPosition", "GetClipPosition" },
            Type.EmptyTypes);

        private static readonly MethodInfo SetSamplePositionMethod = FindMethod(
            new[] { "SetPreviewClipSamplePosition", "SetClipSamplePosition" },
            new[] { typeof(AudioClip), typeof(int) });

        // Unity 6의 GetMinMaxData는 클립이 아니라 임포터를 받는다. 구버전 시그니처도 폴백으로 남겨둔다.
        private static readonly MethodInfo GetMinMaxDataFromImporterMethod = FindMethod(
            new[] { "GetMinMaxData" },
            new[] { typeof(AudioImporter) });

        private static readonly MethodInfo GetMinMaxDataFromClipMethod = FindMethod(
            new[] { "GetMinMaxData" },
            new[] { typeof(AudioClip) });

        /// <summary>에디터 프리뷰 재생 API를 사용할 수 있는지</summary>
        public static bool IsAvailable => PlayMethod != null || PlaySimpleMethod != null;

        private static MethodInfo FindMethod(string[] names, Type[] parameterTypes)
        {
            if (AudioUtilType == null) return null;

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in names)
            {
                var method = AudioUtilType.GetMethod(name, flags, null, parameterTypes, null);
                if (method != null) return method;
            }
            return null;
        }

        private static object Invoke(MethodInfo method, params object[] args)
        {
            if (method == null) return null;

            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException)
            {
                // 클립 데이터가 없는 등의 이유로 내부에서 실패할 수 있다. 재생만 안 될 뿐이므로 삼킨다.
                return null;
            }
        }

        /// <summary>클립을 지정한 샘플 위치부터 재생한다.</summary>
        public static void Play(AudioClip clip, int startSample, bool loop)
        {
            if (clip == null) return;

            if (PlayMethod != null)
            {
                Invoke(PlayMethod, clip, startSample, loop);
                return;
            }

            Invoke(PlaySimpleMethod, clip);
            if (loop)
            {
                SetLoop(true);
            }
        }

        /// <summary>모든 프리뷰 재생을 멈춘다.</summary>
        public static void Stop()
        {
            Invoke(StopMethod);
        }

        /// <summary>재생을 일시정지한다.</summary>
        public static void Pause()
        {
            Invoke(PauseMethod);
        }

        /// <summary>일시정지된 재생을 이어서 한다.</summary>
        public static void Resume()
        {
            Invoke(ResumeMethod);
        }

        /// <summary>반복 재생 여부를 설정한다.</summary>
        public static void SetLoop(bool loop)
        {
            Invoke(LoopMethod, loop);
        }

        /// <summary>프리뷰가 재생 중인지</summary>
        public static bool IsPlaying()
        {
            return Invoke(IsPlayingMethod) is bool playing && playing;
        }

        /// <summary>재생 위치(초). 알 수 없으면 0</summary>
        public static float GetPosition()
        {
            var result = Invoke(GetPositionMethod);
            return result is float position ? position : 0f;
        }

        /// <summary>재생 위치를 샘플 단위로 옮긴다.</summary>
        public static void SetSamplePosition(AudioClip clip, int samplePosition)
        {
            if (clip == null) return;
            Invoke(SetSamplePositionMethod, clip, samplePosition);
        }

        /// <summary>
        /// 에디터가 인스펙터 파형용으로 미리 계산해 둔 min/max 데이터. 없으면 null.
        /// 채널당 일정 구간 수만큼의 (min, max) 쌍이 시간순으로 인터리브되어 들어 있다.
        /// 스트리밍 클립은 GetData가 실패하므로 파형을 얻을 길이 이것뿐이다.
        /// </summary>
        public static float[] GetMinMaxData(AudioClip clip)
        {
            if (clip == null) return null;

            if (GetMinMaxDataFromImporterMethod != null)
            {
                var importer = GetImporter(clip);
                if (importer != null)
                {
                    var data = Invoke(GetMinMaxDataFromImporterMethod, importer) as float[];
                    if (data != null && data.Length > 0) return data;
                }
            }

            return Invoke(GetMinMaxDataFromClipMethod, clip) as float[];
        }

        private static AudioImporter GetImporter(AudioClip clip)
        {
            var path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path)) return null;

            return AssetImporter.GetAtPath(path) as AudioImporter;
        }
    }
}

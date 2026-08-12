// UI 자동화 시스템 - 프리팹 스크린샷 캡처
// RenderTexture + Camera를 사용하여 UI 프리팹을 PNG로 캡처

using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PX.UIAutomation
{
    /// <summary>
    /// UI 프리팹 스크린샷 캡처 유틸리티
    /// Editor 환경에서 프리팹을 렌더링하여 PNG로 저장
    /// </summary>
    public static class UIPrefabScreenshotCapture
    {
        // 캡처 해상도 (고정)
        private const int WIDTH = 1920;
        private const int HEIGHT = 1080;

        /// <summary>
        /// UI 프리팹을 스크린샷으로 캡처
        /// </summary>
        /// <param name="prefabPath">프리팹 에셋 경로</param>
        /// <param name="outputPath">출력 PNG 파일 경로</param>
        /// <returns>성공 여부</returns>
        public static bool Capture(string prefabPath, string outputPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[UIPrefabScreenshotCapture] 프리팹 로드 실패: {prefabPath}");
                return false;
            }

            return CaptureInternal(prefab, outputPath);
        }

        /// <summary>
        /// GameObject를 스크린샷으로 캡처
        /// </summary>
        public static bool Capture(GameObject prefab, string outputPath)
        {
            if (prefab == null)
            {
                Debug.LogError("[UIPrefabScreenshotCapture] 프리팹이 null입니다.");
                return false;
            }

            return CaptureInternal(prefab, outputPath);
        }

        private static bool CaptureInternal(GameObject prefab, string outputPath)
        {
            // 임시 오브젝트들
            GameObject rootGO = null;
            Camera renderCamera = null;
            RenderTexture renderTexture = null;
            RenderTexture previousActive = null;

            try
            {
                // 1. 루트 GameObject 생성
                rootGO = new GameObject("__UIPrefabCapture_Root__");
                rootGO.hideFlags = HideFlags.HideAndDontSave;

                // 2. 카메라 생성 및 설정
                renderCamera = rootGO.AddComponent<Camera>();
                renderCamera.clearFlags = CameraClearFlags.SolidColor;
                renderCamera.backgroundColor = Color.black;
                renderCamera.orthographic = true;
                renderCamera.orthographicSize = HEIGHT / 2f;
                renderCamera.nearClipPlane = 0.1f;
                renderCamera.farClipPlane = 1000f;
                renderCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
                renderCamera.transform.position = new Vector3(WIDTH / 2f, HEIGHT / 2f, -500f);

                // 3. Canvas 생성 및 설정
                GameObject canvasGO = new GameObject("__UIPrefabCapture_Canvas__");
                canvasGO.hideFlags = HideFlags.HideAndDontSave;
                canvasGO.transform.SetParent(rootGO.transform);
                canvasGO.layer = LayerMask.NameToLayer("UI");

                Canvas canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = renderCamera;
                canvas.planeDistance = 100f;

                CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(WIDTH, HEIGHT);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                canvasGO.AddComponent<GraphicRaycaster>();

                // 4. 프리팹 인스턴스화
                GameObject instance = UnityEngine.Object.Instantiate(prefab, canvasGO.transform);
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.name = prefab.name;

                // RectTransform 설정 (전체 화면 채우기)
                RectTransform rectTransform = instance.GetComponent<RectTransform>();
                if (rectTransform != null)
                {
                    rectTransform.anchorMin = Vector2.zero;
                    rectTransform.anchorMax = Vector2.one;
                    rectTransform.offsetMin = Vector2.zero;
                    rectTransform.offsetMax = Vector2.zero;
                    rectTransform.localScale = Vector3.one;
                    rectTransform.localPosition = Vector3.zero;
                }

                // 모든 자식 레이어 설정
                SetLayerRecursively(instance, LayerMask.NameToLayer("UI"));

                // 5. 레이아웃 강제 업데이트 (LayoutGroup이 적용되도록)
                Canvas.ForceUpdateCanvases();

                // 모든 LayoutGroup 강제 재계산
                var layoutGroups = instance.GetComponentsInChildren<LayoutGroup>(true);
                foreach (var lg in layoutGroups)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(lg.GetComponent<RectTransform>());
                }

                // ContentSizeFitter도 업데이트
                var sizeFitters = instance.GetComponentsInChildren<ContentSizeFitter>(true);
                foreach (var sf in sizeFitters)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(sf.GetComponent<RectTransform>());
                }

                // 한번 더 Canvas 업데이트
                Canvas.ForceUpdateCanvases();

                // 6. RenderTexture 생성
                renderTexture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
                renderTexture.antiAliasing = 4;
                renderTexture.Create();

                renderCamera.targetTexture = renderTexture;

                // 7. 렌더링
                renderCamera.Render();

                // 7. Texture2D로 읽기
                previousActive = RenderTexture.active;
                RenderTexture.active = renderTexture;

                Texture2D screenshot = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
                screenshot.Apply();

                RenderTexture.active = previousActive;

                // 8. PNG 저장
                byte[] pngData = screenshot.EncodeToPNG();

                // 출력 디렉터리 확인
                string directory = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(outputPath, pngData);

                // Texture2D 정리
                UnityEngine.Object.DestroyImmediate(screenshot);

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIPrefabScreenshotCapture] 캡처 실패: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
            finally
            {
                // 리소스 정리 (순서 중요!)

                // 1. 카메라의 targetTexture 해제
                if (renderCamera != null)
                {
                    renderCamera.targetTexture = null;
                }

                // 2. RenderTexture.active 복원
                if (previousActive != null)
                {
                    RenderTexture.active = previousActive;
                }
                else
                {
                    RenderTexture.active = null;
                }

                // 3. RenderTexture 해제
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }

                // 4. 임시 GameObject 삭제
                if (rootGO != null)
                {
                    UnityEngine.Object.DestroyImmediate(rootGO);
                }
            }
        }

        /// <summary>
        /// 재귀적으로 레이어 설정
        /// </summary>
        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}

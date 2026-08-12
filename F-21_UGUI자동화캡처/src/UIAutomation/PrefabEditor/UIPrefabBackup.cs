// UI 자동화 시스템 - 프리팹 백업/복원
// Undo/Redo 기능을 포함한 백업 시스템

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PX.UIAutomation
{
    /// <summary>
    /// 프리팹 백업/복원 유틸리티
    /// Undo/Redo 기능 제공
    /// </summary>
    public static class UIPrefabBackup
    {
        // Assets 폴더 외부에 백업 (GUID 충돌 방지)
        private static readonly string BackupFolder = Path.Combine(Application.dataPath, "../Temp/UIAutomation/Backup");
        private const int MaxBackupCount = 10; // 프리팹당 최대 백업 수

        /// <summary>
        /// 프리팹별 Undo/Redo 히스토리 관리
        /// </summary>
        private class UndoRedoHistory
        {
            public List<string> BackupPaths = new List<string>(); // 백업 파일 경로 리스트 (오래된 순)
            public int CurrentIndex = -1; // 현재 위치 (-1: 초기 상태, 0~N: 백업 인덱스)
        }

        // 프리팹별 히스토리 (세션 내 유지)
        private static Dictionary<string, UndoRedoHistory> _histories = new Dictionary<string, UndoRedoHistory>();

        #region Public API

        /// <summary>
        /// 수정 전 준비 (Redo 히스토리 정리, 첫 수정 시 원본 저장)
        /// </summary>
        /// <param name="prefabPath">원본 프리팹 경로</param>
        /// <returns>백업 파일 경로 (실패 시 null)</returns>
        public static string CreateBackup(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath) || !File.Exists(prefabPath))
            {
                Debug.LogError($"[UIPrefabBackup] 프리팹 파일을 찾을 수 없음: {prefabPath}");
                return null;
            }

            try
            {
                EnsureBackupFolderExists();

                var history = GetOrCreateHistory(prefabPath);

                // 현재 인덱스 이후의 Redo 히스토리 제거 (새 분기 시작)
                if (history.CurrentIndex < history.BackupPaths.Count - 1)
                {
                    var toRemove = history.BackupPaths.Skip(history.CurrentIndex + 1).ToList();
                    foreach (var path in toRemove)
                    {
                        TryDeleteFile(path);
                    }
                    history.BackupPaths = history.BackupPaths.Take(history.CurrentIndex + 1).ToList();
                }

                // 첫 수정 시에만 원본 상태 저장
                if (history.BackupPaths.Count == 0)
                {
                    string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    string backupFileName = $"{prefabName}_backup_{timestamp}.prefab";
                    string backupPath = Path.Combine(BackupFolder, backupFileName);

                    File.Copy(prefabPath, backupPath, overwrite: true);

                    history.BackupPaths.Add(backupPath);
                    history.CurrentIndex = 0;

                    Debug.Log($"[UIPrefabBackup] 원본 백업 생성: {backupPath}");
                    return backupPath;
                }

                return "prepared"; // 이미 히스토리가 있으므로 준비만 완료
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIPrefabBackup] 백업 준비 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 수정 후 상태 저장 (수정 적용 성공 후 호출)
        /// </summary>
        public static void SaveAfterModification(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath) || !File.Exists(prefabPath))
                return;

            try
            {
                EnsureBackupFolderExists();

                var history = GetOrCreateHistory(prefabPath);

                string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string backupFileName = $"{prefabName}_backup_{timestamp}.prefab";
                string backupPath = Path.Combine(BackupFolder, backupFileName);

                File.Copy(prefabPath, backupPath, overwrite: true);

                history.BackupPaths.Add(backupPath);
                history.CurrentIndex = history.BackupPaths.Count - 1;

                CleanupOldBackups(history);

                Debug.Log($"[UIPrefabBackup] 수정 후 상태 저장 (히스토리: {history.CurrentIndex + 1}/{history.BackupPaths.Count})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIPrefabBackup] 수정 후 상태 저장 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// Undo - 이전 상태로 복원
        /// </summary>
        public static bool Undo(string prefabPath)
        {
            if (!CanUndo(prefabPath))
            {
                Debug.LogError("[UIPrefabBackup] Undo 불가: 이전 백업이 없습니다.");
                return false;
            }

            var history = GetOrCreateHistory(prefabPath);

            // 이전 인덱스로 이동
            history.CurrentIndex--;
            string backupPath = history.BackupPaths[history.CurrentIndex];

            bool success = RestoreFromBackup(prefabPath, backupPath);

            if (success)
            {
                Debug.Log($"[UIPrefabBackup] Undo 완료 (히스토리: {history.CurrentIndex + 1}/{history.BackupPaths.Count})");
            }
            else
            {
                history.CurrentIndex++; // 실패 시 롤백
            }

            return success;
        }

        /// <summary>
        /// Redo - 다음 상태로 복원
        /// </summary>
        public static bool Redo(string prefabPath)
        {
            if (!CanRedo(prefabPath))
            {
                Debug.LogError("[UIPrefabBackup] Redo 불가: 다음 백업이 없습니다.");
                return false;
            }

            var history = GetOrCreateHistory(prefabPath);

            // 다음 인덱스로 이동
            history.CurrentIndex++;
            string backupPath = history.BackupPaths[history.CurrentIndex];

            bool success = RestoreFromBackup(prefabPath, backupPath);

            if (success)
            {
                Debug.Log($"[UIPrefabBackup] Redo 완료 (히스토리: {history.CurrentIndex + 1}/{history.BackupPaths.Count})");
            }
            else
            {
                history.CurrentIndex--; // 실패 시 롤백
            }

            return success;
        }

        /// <summary>
        /// Undo 가능 여부
        /// </summary>
        public static bool CanUndo(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return false;

            var history = GetOrCreateHistory(prefabPath);
            return history.CurrentIndex > 0; // 0보다 커야 이전으로 갈 수 있음
        }

        /// <summary>
        /// Redo 가능 여부
        /// </summary>
        public static bool CanRedo(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return false;

            var history = GetOrCreateHistory(prefabPath);
            return history.CurrentIndex < history.BackupPaths.Count - 1;
        }

        /// <summary>
        /// Undo/Redo 상태 정보
        /// </summary>
        public static (int current, int total) GetUndoRedoState(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return (0, 0);

            var history = GetOrCreateHistory(prefabPath);
            return (history.CurrentIndex + 1, history.BackupPaths.Count);
        }

        /// <summary>
        /// 백업 가능 여부 확인
        /// </summary>
        public static bool HasBackup(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return false;

            var history = GetOrCreateHistory(prefabPath);
            return history.BackupPaths.Count > 0;
        }

        /// <summary>
        /// 히스토리 초기화
        /// </summary>
        public static void ClearHistory(string prefabPath)
        {
            if (_histories.TryGetValue(prefabPath, out var history))
            {
                foreach (var path in history.BackupPaths)
                {
                    TryDeleteFile(path);
                }
                _histories.Remove(prefabPath);
            }
        }

        /// <summary>
        /// 모든 백업 파일 및 히스토리 초기화
        /// </summary>
        /// <returns>삭제된 파일 수</returns>
        public static int ClearAllBackups()
        {
            int deletedCount = 0;
            int failedCount = 0;

            // 메모리 히스토리 초기화
            _histories.Clear();

            // 백업 폴더의 모든 파일 삭제
            if (Directory.Exists(BackupFolder))
            {
                try
                {
                    var files = Directory.GetFiles(BackupFolder, "*.prefab");
                    foreach (var file in files)
                    {
                        if (TryDeleteFile(file))
                            deletedCount++;
                        else
                            failedCount++;
                    }

                    if (failedCount > 0)
                        Debug.LogError($"[UIPrefabBackup] 백업 폴더 정리: {deletedCount}개 삭제, {failedCount}개 실패");
                    else
                        Debug.Log($"[UIPrefabBackup] 백업 폴더 정리 완료: {deletedCount}개 파일 삭제");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UIPrefabBackup] 백업 폴더 정리 실패: {ex.Message}");
                }
            }

            return deletedCount;
        }

        #endregion

        #region Private Methods

        private static UndoRedoHistory GetOrCreateHistory(string prefabPath)
        {
            if (!_histories.TryGetValue(prefabPath, out var history))
            {
                history = new UndoRedoHistory();

                // 기존 백업 파일들 로드
                string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
                var existingBackups = GetExistingBackupFiles(prefabName);
                history.BackupPaths = existingBackups;
                history.CurrentIndex = existingBackups.Count - 1;

                _histories[prefabPath] = history;
            }
            return history;
        }

        private static List<string> GetExistingBackupFiles(string prefabName)
        {
            if (!Directory.Exists(BackupFolder))
                return new List<string>();

            string pattern = $"{prefabName}_backup_*.prefab";
            return Directory.GetFiles(BackupFolder, pattern)
                .OrderBy(f => File.GetCreationTime(f))
                .ToList();
        }

        private static bool RestoreFromBackup(string prefabPath, string backupPath)
        {
            if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath))
            {
                Debug.LogError($"[UIPrefabBackup] 백업 파일을 찾을 수 없음: {backupPath}");
                return false;
            }

            try
            {
                RemoveReadOnlyAttribute(prefabPath);
                File.Copy(backupPath, prefabPath, overwrite: true);

                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIPrefabBackup] 복원 실패: {ex.Message}");
                return false;
            }
        }

        private static void RemoveReadOnlyAttribute(string filePath)
        {
            if (!File.Exists(filePath)) return;

            FileAttributes attributes = File.GetAttributes(filePath);
            if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
            }
        }

        private static void CleanupOldBackups(UndoRedoHistory history)
        {
            while (history.BackupPaths.Count > MaxBackupCount)
            {
                string oldestBackup = history.BackupPaths[0];
                TryDeleteFile(oldestBackup);
                history.BackupPaths.RemoveAt(0);
                history.CurrentIndex--;
            }

            // 인덱스 보정
            if (history.CurrentIndex < -1)
                history.CurrentIndex = -1;
        }

        private static bool TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    RemoveReadOnlyAttribute(filePath);
                    File.Delete(filePath);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIPrefabBackup] 파일 삭제 실패: {filePath} - {ex.Message}");
                return false;
            }
        }

        private static void EnsureBackupFolderExists()
        {
            if (!Directory.Exists(BackupFolder))
            {
                Directory.CreateDirectory(BackupFolder);
            }
        }

        #endregion
    }
}

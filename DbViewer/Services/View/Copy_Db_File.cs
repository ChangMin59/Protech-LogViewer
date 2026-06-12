using System;
using System.IO;

namespace DbViewer.Services.View
{
    // SQLite DB를 임시 폴더로 복사하는 서비스다.
    // 현재 이력 열기는 원본을 직접 읽지만, 잠금 회피가 필요할 때 쓸 수 있게 남겨둔 구조다.
    public static class DbCopyService
    {
        // 원본 Log.db와 SQLite 보조 파일(-wal, -shm)을 같은 이름 규칙으로 임시 복사한다.
        public static string CopyDbToTemp(string sourceDbPath)
        {
            if (string.IsNullOrWhiteSpace(sourceDbPath))
            {
                // 복사할 원본 경로가 없으면 호출 흐름 자체가 잘못된 것이다.
                throw new ArgumentException("DB 경로가 비어 있습니다.");
            }

            if (!File.Exists(sourceDbPath))
            {
                // 예: C:\Windows\Log.db가 삭제되었거나 권한 문제로 찾지 못한 경우다.
                throw new FileNotFoundException("DB 파일을 찾을 수 없습니다.", sourceDbPath);
            }

            // 임시 폴더에 시각을 붙인 파일명으로 복사해 기존 파일과 충돌하지 않게 한다.
            string tempDir = Path.GetTempPath();
            string sourceFileName = Path.GetFileName(sourceDbPath);
            string uniqueName = $"dbviewer_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{sourceFileName}";
            string tempDbPath = Path.Combine(tempDir, uniqueName);

            // 실제 DB 본체를 먼저 복사한다.
            File.Copy(sourceDbPath, tempDbPath, overwrite: true);

            // WAL 모드 SQLite는 본체 외에 -wal/-shm 파일에 최신 변경분이 있을 수 있다.
            CopySideFileIfExists(sourceDbPath + "-wal", tempDbPath + "-wal");
            CopySideFileIfExists(sourceDbPath + "-shm", tempDbPath + "-shm");

            return tempDbPath;
        }

        // SQLite 보조 파일이 있을 때만 복사한다.
        private static void CopySideFileIfExists(string sourcePath, string targetPath)
        {
            if (!File.Exists(sourcePath))
            {
                // 일반 rollback journal DB는 -wal/-shm 파일이 없을 수 있다.
                return;
            }

            // 예: Log.db-wal -> dbviewer_..._Log.db-wal.
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }
}

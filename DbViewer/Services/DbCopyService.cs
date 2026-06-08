using System;
using System.IO;

namespace DbViewer.Services
{
    public static class DbCopyService
    {
        public static string CopyDbToTemp(string sourceDbPath)
        {
            if (string.IsNullOrWhiteSpace(sourceDbPath))
            {
                throw new ArgumentException("DB 경로가 비어 있습니다.");
            }

            if (!File.Exists(sourceDbPath))
            {
                throw new FileNotFoundException("DB 파일을 찾을 수 없습니다.", sourceDbPath);
            }

            string tempDir = Path.GetTempPath();
            string sourceFileName = Path.GetFileName(sourceDbPath);
            string uniqueName = $"dbviewer_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{sourceFileName}";
            string tempDbPath = Path.Combine(tempDir, uniqueName);

            File.Copy(sourceDbPath, tempDbPath, overwrite: true);

            CopySideFileIfExists(sourceDbPath + "-wal", tempDbPath + "-wal");
            CopySideFileIfExists(sourceDbPath + "-shm", tempDbPath + "-shm");

            return tempDbPath;
        }

        private static void CopySideFileIfExists(string sourcePath, string targetPath)
        {
            if (!File.Exists(sourcePath))
            {
                return;
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }
}
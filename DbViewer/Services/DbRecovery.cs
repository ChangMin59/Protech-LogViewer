using Microsoft.Data.Sqlite;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DbViewer.Services
{
    public sealed class DbRecoveryResult
    {
        public bool Success { get; init; }
        public string SourceDbPath { get; init; } = "";
        public string RecoveredDbPath { get; init; } = "";
        public string RecoverySqlPath { get; init; } = "";
        public string IntegrityResult { get; init; } = "";
        public int OriginalLogCount { get; init; } = -1;
        public int RecoveredLogCount { get; init; } = -1;
        public string Message { get; init; } = "";
    }

    public static class DbRecovery
    {
        public static DbRecoveryResult Recover(string sourceDbPath)
        {
            if (string.IsNullOrWhiteSpace(sourceDbPath))
            {
                return Failure(
                    sourceDbPath,
                    "복구할 DB 경로가 비어 있습니다."
                );
            }

            if (!File.Exists(sourceDbPath))
            {
                return Failure(
                    sourceDbPath,
                    "복구할 DB 파일을 찾을 수 없습니다."
                );
            }

            string sqliteExePath = FindSqliteExePath();

            if (!File.Exists(sqliteExePath))
            {
                return Failure(
                    sourceDbPath,
                    "sqlite3.exe 파일을 찾을 수 없습니다.\n\n" +
                    "프로젝트의 sqlite3 폴더에 sqlite3.exe를 넣고,\n" +
                    "속성에서 [빌드 작업: 내용], [출력 디렉터리에 복사: 새 버전이면 복사]로 설정하세요.\n\n" +
                    $"확인 경로:\n{sqliteExePath}"
                );
            }

            string recoveryDir = Path.Combine(
                Path.GetTempPath(),
                "DbViewer",
                "Recovery",
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
            );

            try
            {
                Directory.CreateDirectory(recoveryDir);

                string sourceFileName = Path.GetFileNameWithoutExtension(sourceDbPath);

                string copiedSourceDbPath = Path.Combine(
                    recoveryDir,
                    $"{sourceFileName}_source.db"
                );

                string recoverySqlPath = Path.Combine(
                    recoveryDir,
                    $"{sourceFileName}_recovered.sql"
                );

                string recoveredDbPath = Path.Combine(
                    recoveryDir,
                    $"{sourceFileName}_recovered.db"
                );

                File.Copy(sourceDbPath, copiedSourceDbPath, true);

                CopySidecarFileIfExists(sourceDbPath, copiedSourceDbPath, "-wal");
                CopySidecarFileIfExists(sourceDbPath, copiedSourceDbPath, "-shm");

                int originalCount = TryCountLogRows(copiedSourceDbPath);

                bool recoverCommandSuccess = RunRecoverCommand(
                    sqliteExePath,
                    copiedSourceDbPath,
                    recoverySqlPath,
                    out string recoverError
                );

                if (!recoverCommandSuccess)
                {
                    return new DbRecoveryResult
                    {
                        Success = false,
                        SourceDbPath = sourceDbPath,
                        RecoveredDbPath = recoveredDbPath,
                        RecoverySqlPath = recoverySqlPath,
                        OriginalLogCount = originalCount,
                        RecoveredLogCount = -1,
                        IntegrityResult = "",
                        Message = recoverError
                    };
                }

                bool createDbSuccess = CreateRecoveredDatabase(
                    recoveredDbPath,
                    recoverySqlPath,
                    out string createDbError
                );

                if (!createDbSuccess)
                {
                    return new DbRecoveryResult
                    {
                        Success = false,
                        SourceDbPath = sourceDbPath,
                        RecoveredDbPath = recoveredDbPath,
                        RecoverySqlPath = recoverySqlPath,
                        OriginalLogCount = originalCount,
                        RecoveredLogCount = -1,
                        IntegrityResult = "",
                        Message = createDbError
                    };
                }

                string integrityResult = RunIntegrityCheck(recoveredDbPath);
                int recoveredCount = TryCountLogRows(recoveredDbPath);

                bool success =
                    File.Exists(recoveredDbPath) &&
                    recoveredCount >= 0 &&
                    integrityResult.Contains("ok", StringComparison.OrdinalIgnoreCase);

                return new DbRecoveryResult
                {
                    Success = success,
                    SourceDbPath = sourceDbPath,
                    RecoveredDbPath = recoveredDbPath,
                    RecoverySqlPath = recoverySqlPath,
                    IntegrityResult = integrityResult,
                    OriginalLogCount = originalCount,
                    RecoveredLogCount = recoveredCount,
                    Message = success
                        ? "DB 복구가 완료되었습니다."
                        : "DB 복구 파일은 생성되었지만 무결성 확인이 필요합니다."
                };
            }
            catch (Exception ex)
            {
                return Failure(
                    sourceDbPath,
                    $"DB 복구 중 오류가 발생했습니다.\n\n{ex.Message}"
                );
            }
        }

        private static DbRecoveryResult Failure(string sourceDbPath, string message)
        {
            return new DbRecoveryResult
            {
                Success = false,
                SourceDbPath = sourceDbPath,
                RecoveredDbPath = "",
                RecoverySqlPath = "",
                IntegrityResult = "",
                OriginalLogCount = -1,
                RecoveredLogCount = -1,
                Message = message
            };
        }

        private static string FindSqliteExePath()
        {
            string baseDir = AppContext.BaseDirectory;

            string sqliteFolderPath = Path.Combine(
                baseDir,
                "sqlite3",
                "sqlite3.exe"
            );

            if (File.Exists(sqliteFolderPath))
            {
                return sqliteFolderPath;
            }

            string rootPath = Path.Combine(
                baseDir,
                "sqlite3.exe"
            );

            if (File.Exists(rootPath))
            {
                return rootPath;
            }

            return sqliteFolderPath;
        }

        private static void CopySidecarFileIfExists(
            string originalDbPath,
            string copiedDbPath,
            string suffix)
        {
            string originalSidecarPath = originalDbPath + suffix;
            string copiedSidecarPath = copiedDbPath + suffix;

            if (File.Exists(originalSidecarPath))
            {
                File.Copy(originalSidecarPath, copiedSidecarPath, true);
            }
        }

        private static bool RunRecoverCommand(
            string sqliteExePath,
            string sourceDbPath,
            string recoverySqlPath,
            out string errorMessage)
        {
            errorMessage = "";

            try
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = sqliteExePath,
                    Arguments = $"\"{sourceDbPath}\" \".recover\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using Process process = new()
                {
                    StartInfo = startInfo
                };

                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                process.WaitForExit();

                if (string.IsNullOrWhiteSpace(output))
                {
                    errorMessage =
                        "sqlite3 .recover 결과가 비어 있습니다.\n\n" +
                        $"오류 내용:\n{error}";

                    return false;
                }

                File.WriteAllText(recoverySqlPath, output, Encoding.UTF8);

                if (process.ExitCode != 0 && !File.Exists(recoverySqlPath))
                {
                    errorMessage =
                        "sqlite3 .recover 실행에 실패했습니다.\n\n" +
                        $"ExitCode: {process.ExitCode}\n" +
                        $"오류 내용:\n{error}";

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage =
                    "sqlite3 .recover 실행 중 오류가 발생했습니다.\n\n" +
                    ex.Message;

                return false;
            }
        }

        private static bool CreateRecoveredDatabase(
            string recoveredDbPath,
            string recoverySqlPath,
            out string errorMessage)
        {
            errorMessage = "";

            try
            {
                if (File.Exists(recoveredDbPath))
                {
                    File.Delete(recoveredDbPath);
                }

                string sql = File.ReadAllText(recoverySqlPath, Encoding.UTF8);

                using SqliteConnection connection = new($"Data Source={recoveredDbPath}");
                connection.Open();

                using SqliteTransaction transaction = connection.BeginTransaction();

                try
                {
                    using SqliteCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = sql;
                    command.ExecuteNonQuery();

                    transaction.Commit();

                    return true;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();

                    errorMessage =
                        "복구 SQL을 DB로 변환하는 중 오류가 발생했습니다.\n\n" +
                        ex.Message;

                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage =
                    "복구 DB 생성 준비 중 오류가 발생했습니다.\n\n" +
                    ex.Message;

                return false;
            }
        }

        private static string RunIntegrityCheck(string dbPath)
        {
            try
            {
                using SqliteConnection connection = new($"Data Source={dbPath}");
                connection.Open();

                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check;";

                object? result = command.ExecuteScalar();

                return result?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                return $"integrity_check 실패: {ex.Message}";
            }
        }

        private static int TryCountLogRows(string dbPath)
        {
            try
            {
                using SqliteConnection connection = new($"Data Source={dbPath}");
                connection.Open();

                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Log;";

                object? result = command.ExecuteScalar();

                if (result == null)
                {
                    return 0;
                }

                return Convert.ToInt32(result);
            }
            catch
            {
                return -1;
            }
        }
    }
}
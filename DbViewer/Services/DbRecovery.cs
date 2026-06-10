using Microsoft.Data.Sqlite;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

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
        private const int ProcessTimeoutMilliseconds = 300_000;

        public static DbRecoveryResult Recover(string sourceDbPath)
        {
            if (string.IsNullOrWhiteSpace(sourceDbPath))
            {
                return Failure(sourceDbPath, "복구할 DB 경로가 비어 있습니다.");
            }

            if (!File.Exists(sourceDbPath))
            {
                return Failure(sourceDbPath, "복구할 DB 파일을 찾을 수 없습니다.");
            }

            string sqliteExePath = FindSqliteExePath();

            if (!File.Exists(sqliteExePath))
            {
                return Failure(
                    sourceDbPath,
                    "sqlite3.exe 파일을 찾을 수 없습니다.\n\n" +
                    "프로젝트의 sqlite3 또는 sqllite3 폴더에 sqlite3.exe를 넣고,\n" +
                    "속성에서 [빌드 작업: 내용], [출력 디렉터리에 복사: 새 버전이면 복사]로 설정하세요.\n\n" +
                    $"확인 경로:\n{sqliteExePath}"
                );
            }

            try
            {
                string sourceDir = Path.GetDirectoryName(sourceDbPath) ?? "";
                string recoveryRootDir = Path.Combine(sourceDir, "sqlliteDB");
                string recoveryDir = CreateTimestampRecoveryDirectory(recoveryRootDir);

                Directory.CreateDirectory(recoveryDir);

                string backupDbPath = Path.Combine(recoveryDir, "Log.db");
                string recoverySqlPath = Path.Combine(recoveryDir, "recovery.sql");
                string recoveredDbPath = Path.Combine(recoveryDir, "Log_recovered.db");
                /*
                 * 1. 기존 C:\Windows\Log.db를 백업 폴더로 먼저 복사한다.
                 * 최종 백업 위치 예:
                 * C:\Windows\sqlliteDB\20260610_113045\Log.db
                 */
                File.Copy(sourceDbPath, backupDbPath, overwrite: false);

                /*
                 * WAL/SHM 파일이 있으면 같이 백업한다.
                 * SQLite가 WAL 모드로 동작 중이면 Log.db-wal 안에 최신 데이터가 남아 있을 수 있다.
                 */
                CopySidecarFileIfExists(sourceDbPath, backupDbPath, "-wal");
                CopySidecarFileIfExists(sourceDbPath, backupDbPath, "-shm");

                int originalCount = TryCountLogRows(backupDbPath);

                /*
                 * 2. 파이썬 검증 코드와 동일하게 sqlite3.exe .recover로 SQL을 추출한다.
                 *
                 * sqlite3.exe 백업DB .recover > recovery.sql
                 */
                bool recoverCommandSuccess = RunRecoverCommand(
                    sqliteExePath,
                    backupDbPath,
                    recoverySqlPath,
                    out string recoverError
                );

                if (!recoverCommandSuccess)
                {
                    DeleteFileIfExists(recoveredDbPath);

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

                /*
                 * 3. 파이썬 검증 코드와 동일하게 sqlite3.exe에 SQL 파일을 입력해서 새 DB를 만든다.
                 *
                 * sqlite3.exe Log_recovered.db < recovery.sql
                 *
                 * 기존 문제:
                 * Microsoft.Data.Sqlite의 ExecuteNonQuery()로 .recover SQL 전체를 실행하려고 해서 실패 가능성이 컸다.
                 */
                bool createDbSuccess = CreateRecoveredDatabaseBySqliteCli(
                    sqliteExePath,
                    recoveredDbPath,
                    recoverySqlPath,
                    out string createDbError
                );

                if (!createDbSuccess)
                {
                    DeleteFileIfExists(recoveredDbPath);

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

                /*
                 * 4. 복구 DB 무결성 검사.
                 */
                string integrityResult = RunIntegrityCheck(recoveredDbPath);
                int recoveredCount = TryCountLogRows(recoveredDbPath);

                bool success =
                    File.Exists(recoveredDbPath) &&
                    recoveredCount >= 0 &&
                    string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase);

                if (!success)
                {
                    return new DbRecoveryResult
                    {
                        Success = false,
                        SourceDbPath = sourceDbPath,
                        RecoveredDbPath = recoveredDbPath,
                        RecoverySqlPath = recoverySqlPath,
                        IntegrityResult = integrityResult,
                        OriginalLogCount = originalCount,
                        RecoveredLogCount = recoveredCount,
                        Message =
                            "복구 DB 파일은 생성되었지만 무결성 확인에 실패했습니다.\n\n" +
                            $"무결성 검사 결과: {integrityResult}"
                    };
                }

                /*
                 * 5. 검증된 복구 DB를 원래 위치 C:\Windows\Log.db로 교체한다.
                 *
                 * 기존 DB는 이미 recoveryDir\Log.db로 백업되어 있다.
                 */
                if (!ReplaceSourceDatabase(sourceDbPath, recoveredDbPath, recoveryDir, out string replaceError))
                {
                    return new DbRecoveryResult
                    {
                        Success = false,
                        SourceDbPath = sourceDbPath,
                        RecoveredDbPath = recoveredDbPath,
                        RecoverySqlPath = recoverySqlPath,
                        IntegrityResult = integrityResult,
                        OriginalLogCount = originalCount,
                        RecoveredLogCount = recoveredCount,
                        Message = replaceError
                    };
                }

                /*
                 * 6. 원본 위치의 WAL/SHM은 복구 DB 기준에서는 필요 없으므로 삭제한다.
                 */
                DeleteSidecarFileIfExists(sourceDbPath, "-wal");
                DeleteSidecarFileIfExists(sourceDbPath, "-shm");

                return new DbRecoveryResult
                {
                    Success = true,
                    SourceDbPath = sourceDbPath,
                    RecoveredDbPath = sourceDbPath,
                    RecoverySqlPath = recoverySqlPath,
                    IntegrityResult = integrityResult,
                    OriginalLogCount = originalCount,
                    RecoveredLogCount = recoveredCount,
                    Message =
                        "DB 복구가 완료되었습니다.\n\n" +
                        $"기존 DB 백업 위치:\n{backupDbPath}\n\n" +
                        $"복구된 DB 적용 위치:\n{sourceDbPath}"
                };
            }
            catch (UnauthorizedAccessException)
            {
                return Failure(
                    sourceDbPath,
                    "프로그램을 관리자 권한으로 실행하세요."
                );
            }
            catch (Exception ex)
            {
                return Failure(
                    sourceDbPath,
                    "DB 복구 중 오류가 발생했습니다.\n\n" +
                    ex.Message
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

        private static string CreateTimestampRecoveryDirectory(string recoveryRootDir)
        {
            Directory.CreateDirectory(recoveryRootDir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string recoveryDir = Path.Combine(recoveryRootDir, timestamp);

            if (!Directory.Exists(recoveryDir))
            {
                return recoveryDir;
            }

            return Path.Combine(
                recoveryRootDir,
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
            );
        }

        private static string FindSqliteExePath()
        {
            string baseDir = AppContext.BaseDirectory;

            string[] candidatePaths =
            {
                Path.Combine(baseDir, "sqlite3", "sqlite3.exe"),
                Path.Combine(baseDir, "sqllite3", "sqlite3.exe"),
                Path.Combine(baseDir, "sqllit3", "sqlite3.exe"),
                Path.Combine(baseDir, "sqlite3.exe"),

                Path.Combine(baseDir, "sqlite3", "sqllit3.exe"),
                Path.Combine(baseDir, "sqllite3", "sqllit3.exe"),
                Path.Combine(baseDir, "sqllit3", "sqllit3.exe"),
                Path.Combine(baseDir, "sqllit3.exe"),

                Path.Combine(baseDir, "..", "..", "..", "sqlite3", "sqlite3.exe"),
                Path.Combine(baseDir, "..", "..", "..", "sqllite3", "sqlite3.exe"),
                Path.Combine(baseDir, "..", "..", "..", "sqllit3", "sqlite3.exe"),

                Path.Combine(baseDir, "..", "..", "..", "sqlite3", "sqllit3.exe"),
                Path.Combine(baseDir, "..", "..", "..", "sqllite3", "sqllit3.exe"),
                Path.Combine(baseDir, "..", "..", "..", "sqllit3", "sqllit3.exe")
            };

            foreach (string candidatePath in candidatePaths)
            {
                string fullPath = Path.GetFullPath(candidatePath);

                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return Path.GetFullPath(candidatePaths[0]);
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
                File.Copy(originalSidecarPath, copiedSidecarPath, overwrite: true);
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
                DeleteFileIfExists(recoverySqlPath);

                ProcessStartInfo startInfo = new()
                {
                    FileName = sqliteExePath,
                    Arguments = $"\"{sourceDbPath}\" \".recover\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using Process process = new()
                {
                    StartInfo = startInfo
                };

                process.Start();
                process.StandardInput.Close();

                Task copyOutputTask;

                using (FileStream sqlStream = new(
                           recoverySqlPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.Read))
                {
                    copyOutputTask = process.StandardOutput.BaseStream.CopyToAsync(sqlStream);

                    string error = process.StandardError.ReadToEnd();

                    bool exited = process.WaitForExit(ProcessTimeoutMilliseconds);

                    copyOutputTask.GetAwaiter().GetResult();

                    if (!exited)
                    {
                        TryKillProcess(process);

                        errorMessage =
                            "sqlite3 .recover 실행 시간이 초과되었습니다.";

                        return false;
                    }

                    if (process.ExitCode != 0)
                    {
                        errorMessage =
                            "sqlite3 .recover 실행에 실패했습니다.\n\n" +
                            $"ExitCode: {process.ExitCode}\n" +
                            $"오류 내용:\n{error}";

                        return false;
                    }
                }

                if (!File.Exists(recoverySqlPath) || new FileInfo(recoverySqlPath).Length == 0)
                {
                    errorMessage =
                        "sqlite3 .recover 결과 SQL이 비어 있습니다.";

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

        private static bool CreateRecoveredDatabaseBySqliteCli(
            string sqliteExePath,
            string recoveredDbPath,
            string recoverySqlPath,
            out string errorMessage)
        {
            errorMessage = "";

            try
            {
                if (!File.Exists(recoverySqlPath))
                {
                    errorMessage =
                        "복구 SQL 파일을 찾을 수 없습니다.";

                    return false;
                }

                if (new FileInfo(recoverySqlPath).Length == 0)
                {
                    errorMessage =
                        "복구 SQL 파일이 비어 있습니다.";

                    return false;
                }

                DeleteFileIfExists(recoveredDbPath);
                DeleteFileIfExists(recoveredDbPath + "-wal");
                DeleteFileIfExists(recoveredDbPath + "-shm");

                ProcessStartInfo startInfo = new()
                {
                    FileName = sqliteExePath,
                    Arguments = $"\"{recoveredDbPath}\"",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardInputEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using Process process = new()
                {
                    StartInfo = startInfo
                };

                process.Start();

                using (FileStream sqlStream = new(
                           recoverySqlPath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read))
                {
                    sqlStream.CopyTo(process.StandardInput.BaseStream);
                }

                process.StandardInput.Close();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                bool exited = process.WaitForExit(ProcessTimeoutMilliseconds);

                if (!exited)
                {
                    TryKillProcess(process);

                    errorMessage =
                        "복구 SQL을 새 DB로 변환하는 시간이 초과되었습니다.";

                    return false;
                }

                if (process.ExitCode != 0)
                {
                    errorMessage =
                        "복구 SQL을 DB로 변환하는 데 실패했습니다.\n\n" +
                        $"ExitCode: {process.ExitCode}\n" +
                        $"출력 내용:\n{output}\n\n" +
                        $"오류 내용:\n{error}";

                    return false;
                }

                if (!File.Exists(recoveredDbPath))
                {
                    errorMessage =
                        "복구 DB 파일이 생성되지 않았습니다.";

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage =
                    "복구 DB 생성 중 오류가 발생했습니다.\n\n" +
                    ex.Message;

                return false;
            }
        }

        private static bool ReplaceSourceDatabase(
            string sourceDbPath,
            string recoveredDbPath,
            string recoveryDir,
            out string errorMessage)
        {
            errorMessage = "";

            try
            {
                if (!File.Exists(recoveredDbPath))
                {
                    errorMessage =
                        "복구된 DB 파일을 찾을 수 없습니다.";

                    return false;
                }

                string replacementDbPath = Path.Combine(recoveryDir, "Log_replacement.db");

                DeleteFileIfExists(replacementDbPath);

                File.Copy(recoveredDbPath, replacementDbPath, overwrite: false);

                /*
                 * File.Replace는 원본 파일을 교체하는 Windows API 방식이다.
                 * 실패할 경우를 대비해 File.Copy overwrite로 한 번 더 시도한다.
                 */
                try
                {
                    File.Replace(
                        replacementDbPath,
                        sourceDbPath,
                        destinationBackupFileName: null
                    );
                }
                catch
                {
                    File.Copy(recoveredDbPath, sourceDbPath, overwrite: true);
                    DeleteFileIfExists(replacementDbPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage =
                    "복구 DB를 최종 위치로 복사하는 중 오류가 발생했습니다.\n\n" +
                    ex.Message + "\n\n" +
                    "C:\\Windows\\Log.db를 교체하려면 프로그램을 관리자 권한으로 실행해야 할 수 있습니다.";

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

        private static void DeleteSidecarFileIfExists(string dbPath, string suffix)
        {
            DeleteFileIfExists(dbPath + suffix);
        }

        private static void DeleteFileIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 삭제 실패는 복구 전체 실패로 보지 않는다.
            }
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // 프로세스 종료 실패는 별도 처리하지 않는다.
            }
        }
    }
}
using Microsoft.Data.Sqlite;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DbViewer.Services.Recovery
{
    public sealed class DbRecoveryResult
    {
        public bool Success { get; init; }
        public string SourceDbPath { get; init; } = "";
        public string RecoveredDbPath { get; init; } = "";
        public string RecoverySqlPath { get; init; } = "";
        public string IntegrityResult { get; init; } = "";
        public bool SourceLogReadable { get; init; }
        public int OriginalLogCount { get; init; } = -1;
        public int RecoveredLogCount { get; init; } = -1;
        public List<string> RecoveredTables { get; init; } = new();
        public int? EstimatedLoss =>
            OriginalLogCount < 0 || RecoveredLogCount < 0
                ? null
                : Math.Max(OriginalLogCount - RecoveredLogCount, 0);
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

            string tempRecoveryDir = "";

            try
            {
                string sourceDir = Path.GetDirectoryName(sourceDbPath) ?? "";
                string backupDir = CreateTimestampBackupDirectory(sourceDir);
                tempRecoveryDir = PrepareTempRecoveryDirectory(sourceDir);

                string backupDbPath = Path.Combine(backupDir, "Log.db");
                string recoverySqlPath = Path.Combine(tempRecoveryDir, "recovery.sql");
                string recoveredDbPath = Path.Combine(tempRecoveryDir, "recovery.db");

                /*
                 * 원본 C:\Windows\Log.db는 먼저 sqllite_DB\날짜시간\Log.db로 복사한다.
                 * 복구와 검증은 임시 db복구 폴더에서 진행하고, 검증 성공 후에만
                 * recovery.db를 C:\Windows\Log.db로 덮어쓴다.
                 */
                File.Copy(sourceDbPath, backupDbPath, overwrite: false);

                int originalCount = TryCountLogRows(backupDbPath);
                bool sourceLogReadable = originalCount >= 0;

                /*
                 * 2. 백업본 DB를 대상으로 sqlite3.exe .recover로 SQL을 추출한다.
                 *
                 * sqlite3.exe 백업DB .recover > sqllite_DB\db복구\recovery.sql
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
                        SourceLogReadable = sourceLogReadable,
                        OriginalLogCount = originalCount,
                        RecoveredLogCount = -1,
                        IntegrityResult = "",
                        Message = recoverError
                    };
                }

                /*
                 * 3. sqlite3.exe에 SQL 파일을 입력해서 새 복구 DB를 만든다.
                 *
                 * sqlite3.exe recovery.db < recovery.sql
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
                        SourceLogReadable = sourceLogReadable,
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
                List<string> recoveredTables = InspectTables(recoveredDbPath);

                bool success =
                    File.Exists(recoveredDbPath) &&
                    recoveredCount >= 0 &&
                    string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase);

                if (!success)
                {
                    DeleteFileIfExists(recoveredDbPath);

                    return new DbRecoveryResult
                    {
                        Success = false,
                        SourceDbPath = sourceDbPath,
                        RecoveredDbPath = recoveredDbPath,
                        RecoverySqlPath = recoverySqlPath,
                        IntegrityResult = integrityResult,
                        SourceLogReadable = sourceLogReadable,
                        OriginalLogCount = originalCount,
                        RecoveredLogCount = recoveredCount,
                        RecoveredTables = recoveredTables,
                        Message =
                            "복구 DB 파일은 생성되었지만 무결성 확인에 실패했습니다.\n\n" +
                            $"무결성 검사 결과: {integrityResult}"
                    };
                }

                File.Copy(recoveredDbPath, sourceDbPath, overwrite: true);

                return new DbRecoveryResult
                {
                    Success = true,
                    SourceDbPath = sourceDbPath,
                    RecoveredDbPath = sourceDbPath,
                    RecoverySqlPath = recoverySqlPath,
                    IntegrityResult = integrityResult,
                    SourceLogReadable = sourceLogReadable,
                    OriginalLogCount = originalCount,
                    RecoveredLogCount = recoveredCount,
                    RecoveredTables = recoveredTables,
                    Message =
                        "DB 복구가 완료되었습니다.\n\n" +
                        $"원본 백업 위치:\n{backupDbPath}\n\n" +
                        $"복구 DB 위치:\n{sourceDbPath}"
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
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempRecoveryDir))
                {
                    DeleteDirectoryIfExists(tempRecoveryDir);
                }
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
                SourceLogReadable = false,
                OriginalLogCount = -1,
                RecoveredLogCount = -1,
                RecoveredTables = new List<string>(),
                Message = message
            };
        }

        private static string CreateTimestampBackupDirectory(string sourceDir)
        {
            string recoveryRootDir = Path.Combine(sourceDir, "sqllite_DB");
            Directory.CreateDirectory(recoveryRootDir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            for (int index = 0; index < 1000; index++)
            {
                string suffix = index == 0
                    ? ""
                    : $"_{index}";

                string recoveryDir = Path.Combine(recoveryRootDir, $"{timestamp}{suffix}");

                if (Directory.Exists(recoveryDir))
                {
                    continue;
                }

                Directory.CreateDirectory(recoveryDir);
                return recoveryDir;
            }

            throw new InvalidOperationException("복구 폴더를 만들 수 없습니다.");
        }

        private static string PrepareTempRecoveryDirectory(string sourceDir)
        {
            string tempRecoveryDir = Path.Combine(sourceDir, "sqllite_DB", "db복구");

            DeleteDirectoryIfExists(tempRecoveryDir);
            Directory.CreateDirectory(tempRecoveryDir);

            return tempRecoveryDir;
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
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();

                    bool exited = process.WaitForExit(ProcessTimeoutMilliseconds);

                    if (!exited)
                    {
                        TryKillProcess(process);
                        TryWaitForExit(process);

                        errorMessage =
                            "sqlite3 .recover 실행 시간이 초과되었습니다.";

                        return false;
                    }

                    copyOutputTask.GetAwaiter().GetResult();
                    string error = errorTask.GetAwaiter().GetResult();

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
                    Arguments = $"-cmd \".dbconfig defensive off\" \"{recoveredDbPath}\"",
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

                WriteRecoverySqlToStandardInput(recoverySqlPath, process.StandardInput);
                process.StandardInput.Close();

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                bool exited = process.WaitForExit(ProcessTimeoutMilliseconds);

                if (!exited)
                {
                    TryKillProcess(process);
                    TryWaitForExit(process);

                    errorMessage =
                        "복구 SQL을 새 DB로 변환하는 시간이 초과되었습니다.";

                    return false;
                }

                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();

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

        private static void WriteRecoverySqlToStandardInput(
            string recoverySqlPath,
            StreamWriter standardInput)
        {
            using StreamReader reader = new(
                recoverySqlPath,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true
            );

            while (reader.ReadLine() is string line)
            {
                if (line.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                standardInput.WriteLine(line);
            }
        }

        private static string RunIntegrityCheck(string dbPath)
        {
            try
            {
                using SqliteConnection connection = CreateReadOnlyConnection(dbPath);
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
                using SqliteConnection connection = CreateReadOnlyConnection(dbPath);
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

        private static List<string> InspectTables(string dbPath)
        {
            try
            {
                using SqliteConnection connection = CreateReadOnlyConnection(dbPath);
                connection.Open();

                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT name
                    FROM sqlite_master
                    WHERE type = 'table'
                      AND name NOT LIKE 'sqlite_%'
                    ORDER BY name;
                """;

                List<string> tables = new();

                using SqliteDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    string name = reader.IsDBNull(0)
                        ? ""
                        : reader.GetString(0);

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        tables.Add(name);
                    }
                }

                return tables;
            }
            catch
            {
                return new List<string>();
            }
        }

        private static SqliteConnection CreateReadOnlyConnection(string dbPath)
        {
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly
            };

            return new SqliteConnection(builder.ToString());
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

        private static void DeleteDirectoryIfExists(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();

            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    NormalizeDirectoryAttributes(path);
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }
        }

        private static void NormalizeDirectoryAttributes(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            DirectoryInfo directory = new(path);

            foreach (FileInfo file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    file.Attributes = FileAttributes.Normal;
                }
                catch
                {
                }
            }

            foreach (DirectoryInfo childDirectory in directory.EnumerateDirectories("*", SearchOption.AllDirectories))
            {
                try
                {
                    childDirectory.Attributes = FileAttributes.Normal;
                }
                catch
                {
                }
            }

            try
            {
                directory.Attributes = FileAttributes.Normal;
            }
            catch
            {
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

        private static void TryWaitForExit(Process process)
        {
            try
            {
                process.WaitForExit(5000);
            }
            catch
            {
                // 프로세스 종료 대기 실패는 별도 처리하지 않는다.
            }
        }
    }
}

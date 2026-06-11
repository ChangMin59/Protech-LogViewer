using Microsoft.Data.Sqlite;
using System.IO;

namespace DbViewer.Services.View
{
    public sealed class HistoryDatabaseValidationResult
    {
        public bool Success { get; init; }
        public int TotalCount { get; init; }
        public string StartDate { get; init; } = "";
        public string EndDate { get; init; } = "";
        public string ErrorMessage { get; init; } = "";
    }

    public static class HistoryDatabaseValidator
    {
        public static HistoryDatabaseValidationResult Check(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                return new HistoryDatabaseValidationResult
                {
                    Success = false,
                    ErrorMessage = "DB 파일 경로가 비어 있습니다."
                };
            }

            if (!File.Exists(dbPath))
            {
                return new HistoryDatabaseValidationResult
                {
                    Success = false,
                    ErrorMessage = "DB 파일을 찾을 수 없습니다."
                };
            }

            try
            {
                using SqliteConnection connection = new($"Data Source={dbPath};Mode=ReadOnly");
                connection.Open();

                string integrityResult = ExecuteScalarText(
                    connection,
                    "PRAGMA integrity_check;"
                );

                if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return new HistoryDatabaseValidationResult
                    {
                        Success = false,
                        ErrorMessage = $"DB 무결성 검사 실패: {integrityResult}"
                    };
                }

                bool hasLogTable = string.Equals(
                    ExecuteScalarText(
                        connection,
                        "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'Log' LIMIT 1;"
                    ),
                    "Log",
                    StringComparison.OrdinalIgnoreCase
                );

                if (!hasLogTable)
                {
                    return new HistoryDatabaseValidationResult
                    {
                        Success = false,
                        ErrorMessage = "Log 테이블이 없습니다."
                    };
                }

                List<string> columns = GetTableColumns(connection, "Log");

                string[] requiredColumns =
                {
                    "ID",
                    "GRP",
                    "DTIME",
                    "Type",
                    "Action",
                    "Section",
                    "Contents",
                    "Packet"
                };

                List<string> missingColumns = requiredColumns
                    .Where(required => !columns.Contains(required, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (missingColumns.Count > 0)
                {
                    return new HistoryDatabaseValidationResult
                    {
                        Success = false,
                        ErrorMessage = $"Log 테이블 컬럼이 맞지 않습니다. 누락 컬럼: {string.Join(", ", missingColumns)}"
                    };
                }

                int totalCount = ExecuteScalarInt(
                    connection,
                    "SELECT COUNT(*) FROM Log;"
                );

                string startDate = ExecuteScalarText(
                    connection,
                    "SELECT SUBSTR(DTIME, 1, 10) FROM Log WHERE DTIME IS NOT NULL AND LENGTH(DTIME) >= 10 ORDER BY DTIME ASC, ID ASC LIMIT 1;"
                );

                string endDate = ExecuteScalarText(
                    connection,
                    "SELECT SUBSTR(DTIME, 1, 10) FROM Log WHERE DTIME IS NOT NULL AND LENGTH(DTIME) >= 10 ORDER BY DTIME DESC, ID DESC LIMIT 1;"
                );

                return new HistoryDatabaseValidationResult
                {
                    Success = true,
                    TotalCount = totalCount,
                    StartDate = startDate,
                    EndDate = endDate
                };
            }
            catch (SqliteException ex)
            {
                return new HistoryDatabaseValidationResult
                {
                    Success = false,
                    ErrorMessage = $"SQLite 파일을 읽을 수 없습니다. {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                return new HistoryDatabaseValidationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static string ExecuteScalarText(SqliteConnection connection, string commandText)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = commandText;

            object? result = command.ExecuteScalar();

            return result?.ToString()?.Trim() ?? "";
        }

        private static int ExecuteScalarInt(SqliteConnection connection, string commandText)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = commandText;

            object? result = command.ExecuteScalar();

            if (result == null)
            {
                return 0;
            }

            return Convert.ToInt32(result);
        }

        private static List<string> GetTableColumns(SqliteConnection connection, string tableName)
        {
            List<string> columns = new();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                string columnName = reader["name"]?.ToString() ?? "";

                if (!string.IsNullOrWhiteSpace(columnName))
                {
                    columns.Add(columnName);
                }
            }

            return columns;
        }
    }
}

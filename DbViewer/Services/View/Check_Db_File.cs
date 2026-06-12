using Microsoft.Data.Sqlite;
using System.IO;

namespace DbViewer.Services.View
{
    // 실제 SQLite 이력 DB를 검사한 결과다.
    // 성공 시 화면의 전체 건수/기간 버튼에 쓸 TotalCount, StartDate, EndDate를 같이 넘긴다.
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
        // 선택된 .db 파일이 이력 보기용 Log.db 형식인지 빠르게 확인한다.
        // 필수 조건: 파일 존재, SQLite quick_check 통과, Log 테이블 존재, 필수 컬럼 존재.
        public static HistoryDatabaseValidationResult Check(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                // 파일 선택이 취소되었거나 경로가 비정상인 경우다.
                return new HistoryDatabaseValidationResult
                {
                    Success = false,
                    ErrorMessage = "DB 파일 경로가 비어 있습니다."
                };
            }

            if (!File.Exists(dbPath))
            {
                // 예: C:\Windows\Log.db가 삭제되었거나 사용자가 존재하지 않는 파일을 지정한 경우다.
                return new HistoryDatabaseValidationResult
                {
                    Success = false,
                    ErrorMessage = "DB 파일을 찾을 수 없습니다."
                };
            }

            try
            {
                // 원본 이력 DB는 수정하지 않으므로 읽기 전용으로 연다.
                using SqliteConnection connection = new($"Data Source={dbPath};Mode=ReadOnly");
                connection.Open();

                // SQLite 파일 자체가 깨졌는지 먼저 짧게 검사한다.
                string integrityResult = ExecuteScalarText(
                    connection,
                    "PRAGMA quick_check;"
                );

                if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    // 깨진 DB는 전체 조회를 시도하지 않고 즉시 실패로 돌려 팝업이 늦게 뜨지 않게 한다.
                    return new HistoryDatabaseValidationResult
                    {
                        Success = false,
                        ErrorMessage = $"DB 빠른 검사 실패: {integrityResult}"
                    };
                }

                // 실제 이력은 Log 테이블에 저장되므로 테이블이 없으면 다른 DB로 본다.
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
                    // 예: SQLite 파일이지만 이력 프로그램 DB가 아닌 경우다.
                    return new HistoryDatabaseValidationResult
                    {
                        Success = false,
                        ErrorMessage = "Log 테이블이 없습니다."
                    };
                }

                // Log 테이블 컬럼 목록을 읽어 실제 이력 컬럼과 맞는지 검사한다.
                List<string> columns = GetTableColumns(connection, "Log");

                // 화면/검색/저장/인쇄가 직접 사용하는 컬럼이다.
                // 예: DTIME=2026-06-01 17:04:17, Type=중계기고장, Action=발생, Section=02#..., Contents=..., Packet=...
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

                // 하나라도 없으면 이후 ReadRow에서 컬럼 index가 깨지므로 여기서 차단한다.
                List<string> missingColumns = requiredColumns
                    .Where(required => !columns.Contains(required, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (missingColumns.Count > 0)
                {
                    // 예: Packet 컬럼이 없는 오래된/다른 형식 DB는 잘못된 이력 파일로 처리한다.
                    return new HistoryDatabaseValidationResult
                    {
                        Success = false,
                        ErrorMessage = $"Log 테이블 컬럼이 맞지 않습니다. 누락 컬럼: {string.Join(", ", missingColumns)}"
                    };
                }

                // 전체로그 카드 숫자와 전체 페이지 계산에 바로 쓸 총 건수다.
                int totalCount = ExecuteScalarInt(
                    connection,
                    "SELECT COUNT(*) FROM Log;"
                );

                // 가장 오래된 로그 날짜를 yyyy-MM-dd로 잘라 시작일로 쓴다.
                string startDate = ExecuteScalarText(
                    connection,
                    "SELECT SUBSTR(DTIME, 1, 10) FROM Log WHERE DTIME IS NOT NULL AND LENGTH(DTIME) >= 10 ORDER BY DTIME ASC, ID ASC LIMIT 1;"
                );

                // 가장 최신 로그 날짜를 yyyy-MM-dd로 잘라 종료일로 쓴다.
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
                // SQLite로 열 수 없는 파일이면 손상 DB 또는 다른 파일 형식으로 본다.
                return new HistoryDatabaseValidationResult
                {
                    Success = false,
                    ErrorMessage = $"SQLite 파일을 읽을 수 없습니다. {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                // 권한/경로/기타 예외 메시지를 화면 안내에 그대로 넘긴다.
                return new HistoryDatabaseValidationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        // SELECT 한 칸짜리 결과를 문자열로 읽는다.
        // 예: PRAGMA quick_check 결과 "ok", Log 테이블명 "Log", 날짜 "2026-06-01".
        private static string ExecuteScalarText(SqliteConnection connection, string commandText)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = commandText;

            object? result = command.ExecuteScalar();

            // null이나 공백은 빈 문자열로 통일해 비교 흐름을 단순하게 만든다.
            return result?.ToString()?.Trim() ?? "";
        }

        // SELECT COUNT(*) 같은 숫자 결과를 int로 읽는다.
        private static int ExecuteScalarInt(SqliteConnection connection, string commandText)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = commandText;

            object? result = command.ExecuteScalar();

            if (result == null)
            {
                // Log가 비어 있거나 결과가 없으면 0건으로 본다.
                return 0;
            }

            return Convert.ToInt32(result);
        }

        // PRAGMA table_info(Log)에서 실제 컬럼명을 모은다.
        // 이 값으로 ID/GRP/DTIME/Type/Action/Section/Contents/Packet 존재 여부를 검사한다.
        private static List<string> GetTableColumns(SqliteConnection connection, string tableName)
        {
            List<string> columns = new();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                // PRAGMA table_info 결과의 name 컬럼이 실제 컬럼명이다.
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

using DbViewer.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DbViewer.Services.View
{
    // SQLite 이력 DB의 Log 테이블을 읽는 저장소다.
    // 실제 컬럼: ID, GRP, DTIME, Type, Action, Section, Contents, Packet.
    public class LogRepository
    {
        private readonly string _dbPath;

        // 화면에서 선택한 Log.db 또는 TXT 변환 임시 DB 경로를 보관한다.
        public LogRepository(string dbPath)
        {
            _dbPath = dbPath;
        }

        // 선택된 SQLite DB가 이력 보기용 DB인지 검사한다.
        // 실제 Log.db가 깨졌거나 Log 테이블이 없으면 여기서 예외를 던진다.
        public void Validate()
        {
            // 원본 DB는 수정하지 않으므로 읽기 전용 연결만 사용한다.
            using SqliteConnection connection = CreateConnection();
            connection.Open();

            // SQLite 파일 자체가 정상인지 빠르게 확인한다.
            using SqliteCommand integrityCommand = connection.CreateCommand();
            integrityCommand.CommandText = "PRAGMA quick_check;";

            object? integrityResult = integrityCommand.ExecuteScalar();

            if (integrityResult?.ToString() != "ok")
            {
                // 예: 손상 DB는 "DB 빠른 검사 실패"로 처리한다.
                throw new Exception($"DB 빠른 검사 실패: {integrityResult}");
            }

            // 실제 이력은 Log 테이블에 들어 있으므로 테이블 존재 여부를 확인한다.
            using SqliteCommand tableCommand = connection.CreateCommand();
            tableCommand.CommandText = """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = 'Log';
            """;

            object? tableResult = tableCommand.ExecuteScalar();

            if (tableResult == null)
            {
                // SQLite 파일이지만 이력 DB가 아닌 경우다.
                throw new Exception("Log 테이블이 없습니다.");
            }
        }

        // Log 테이블 전체 건수를 조회한다.
        // 예: 전체로그 300000건이면 카운트 카드와 전체 페이지 계산에 이 값이 쓰인다.
        public int CountAllLogs()
        {
            using SqliteConnection connection = CreateConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Log;";

            object? result = command.ExecuteScalar();

            // 조회 결과가 null이면 0건으로 처리한다.
            return Convert.ToInt32(result ?? 0);
        }

        // 현재 페이지에 필요한 로그만 조회한다.
        // 예: page=2, pageSize=25이면 최신순 26~50번째 로그만 읽는다.
        public List<LogRow> LoadLogsPage(int page, int pageSize)
        {
            // 잘못된 page/pageSize가 들어와도 최소 1로 보정한다.
            int safePage = Math.Max(1, page);
            int safePageSize = Math.Max(1, pageSize);
            int offset = (safePage - 1) * safePageSize;

            List<LogRow> rows = new();

            using SqliteConnection connection = CreateConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    ID,
                    GRP,
                    DTIME,
                    Type,
                    Action,
                    Section,
                    Contents,
                    Packet
                FROM Log
                ORDER BY DTIME DESC, ID DESC
                LIMIT $limit OFFSET $offset;
            """;

            // LIMIT/OFFSET은 문자열 조합이 아니라 파라미터로 넣어 안전하게 실행한다.
            command.Parameters.AddWithValue("$limit", safePageSize);
            command.Parameters.AddWithValue("$offset", offset);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                // DB 한 행을 화면 표시용 LogRow로 변환한다.
                rows.Add(ReadRow(reader));
            }

            return rows;
        }

        // 전체 로그를 최신순으로 한 줄씩 순차적으로 읽는다.
        // 카테고리 캐시/저장/인쇄처럼 전체를 훑어야 하지만 한 번에 UI에 올리면 안 되는 작업에서 사용한다.
        public IEnumerable<LogRow> StreamAllLogs()
        {
            using SqliteConnection connection = CreateConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    ID,
                    GRP,
                    DTIME,
                    Type,
                    Action,
                    Section,
                    Contents,
                    Packet
                FROM Log
                ORDER BY DTIME DESC, ID DESC;
            """;

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                // yield return이라 호출하는 쪽이 필요할 때마다 한 줄씩 소비한다.
                yield return ReadRow(reader);
            }
        }

        // 검색어가 Type/Action/Section/Contents/Packet 중 하나에 포함된 로그 건수를 조회한다.
        // 예: keyword="중계기"이면 Type=중계기고장, Section=005중계기, Contents=중계기 통신고장이 모두 걸린다.
        public int CountSearchLogs(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                // 빈 검색어는 검색 결과가 아니라 전체 조회 버튼 흐름에서 처리한다.
                return 0;
            }

            // SQLite LIKE 검색용으로 앞뒤에 %를 붙인다.
            string like = $"%{keyword}%";

            using SqliteConnection connection = CreateConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM Log
                WHERE Type LIKE $keyword
                   OR Action LIKE $keyword
                   OR Section LIKE $keyword
                   OR Contents LIKE $keyword
                   OR Packet LIKE $keyword;
            """;

            // 검색어는 파라미터로 넘겨 따옴표나 특수문자가 있어도 쿼리가 깨지지 않게 한다.
            command.Parameters.AddWithValue("$keyword", like);

            object? result = command.ExecuteScalar();

            return Convert.ToInt32(result ?? 0);
        }

        // 검색 결과를 페이지 단위로 조회한다.
        // 예: "MCC" 검색 후 pageSize=25이면 MCC가 포함된 로그 25개만 최신순으로 가져온다.
        public List<LogRow> SearchLogsPage(string keyword, int page, int pageSize)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                // 빈 검색어는 결과 없음으로 반환한다.
                return new List<LogRow>();
            }

            // 페이지 계산은 전체 조회와 같은 방식으로 맞춘다.
            int safePage = Math.Max(1, page);
            int safePageSize = Math.Max(1, pageSize);
            int offset = (safePage - 1) * safePageSize;
            string like = $"%{keyword}%";

            List<LogRow> rows = new();

            using SqliteConnection connection = CreateConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    ID,
                    GRP,
                    DTIME,
                    Type,
                    Action,
                    Section,
                    Contents,
                    Packet
                FROM Log
                WHERE Type LIKE $keyword
                   OR Action LIKE $keyword
                   OR Section LIKE $keyword
                   OR Contents LIKE $keyword
                   OR Packet LIKE $keyword
                ORDER BY DTIME DESC, ID DESC
                LIMIT $limit OFFSET $offset;
            """;

            command.Parameters.AddWithValue("$keyword", like);
            command.Parameters.AddWithValue("$limit", safePageSize);
            command.Parameters.AddWithValue("$offset", offset);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                // 검색 결과도 일반 로그와 같은 LogRow 구조로 반환한다.
                rows.Add(ReadRow(reader));
            }

            return rows;
        }

        // 기간 조회 결과 전체 건수를 조회한다.
        // 예: startDate=2026-06-01, endDate=2026-06-07이면 해당 7일의 전체 로그 수를 계산한다.
        public int CountDateLogs(string startDate, string endDate)
        {
            // DB의 DTIME은 "yyyy-MM-dd HH:mm:ss"라 시작/끝 시간을 붙여 하루 전체를 포함한다.
            string start = $"{startDate} 00:00:00";
            string end = $"{endDate} 23:59:59";

            using SqliteConnection connection = CreateConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM Log
                WHERE DTIME BETWEEN $start AND $end;
            """;

            command.Parameters.AddWithValue("$start", start);
            command.Parameters.AddWithValue("$end", end);

            object? result = command.ExecuteScalar();

            return Convert.ToInt32(result ?? 0);
        }

        // 기간 조회 결과를 페이지 단위로 조회한다.
        // 예: 30일 버튼 적용 후 최신순 25건만 먼저 화면에 보여준다.
        public List<LogRow> LoadLogsByDatePage(
            string startDate,
            string endDate,
            int page,
            int pageSize)
        {
            // 기간 조회도 전체 조회와 같은 페이지 보정 규칙을 쓴다.
            int safePage = Math.Max(1, page);
            int safePageSize = Math.Max(1, pageSize);
            int offset = (safePage - 1) * safePageSize;

            // 선택한 날짜의 00:00:00부터 23:59:59까지 포함한다.
            string start = $"{startDate} 00:00:00";
            string end = $"{endDate} 23:59:59";

            List<LogRow> rows = new();

            using SqliteConnection connection = CreateConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    ID,
                    GRP,
                    DTIME,
                    Type,
                    Action,
                    Section,
                    Contents,
                    Packet
                FROM Log
                WHERE DTIME BETWEEN $start AND $end
                ORDER BY DTIME DESC, ID DESC
                LIMIT $limit OFFSET $offset;
            """;

            command.Parameters.AddWithValue("$start", start);
            command.Parameters.AddWithValue("$end", end);
            command.Parameters.AddWithValue("$limit", safePageSize);
            command.Parameters.AddWithValue("$offset", offset);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                // 기간 필터에 걸린 실제 Log 행만 화면 모델로 변환한다.
                rows.Add(ReadRow(reader));
            }

            return rows;
        }

        // DB 안의 실제 로그 날짜 범위를 조회한다.
        // 예: 가장 오래된 DTIME=2023-01-24, 최신 DTIME=2024-10-12이면 전체 기간은 2023-01-24~2024-10-12다.
        public (string StartDate, string EndDate) GetLogDateRange()
        {
            using SqliteConnection connection = CreateConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    MIN(substr(DTIME, 1, 10)),
                    MAX(substr(DTIME, 1, 10))
                FROM Log
                WHERE DTIME IS NOT NULL
                  AND length(DTIME) >= 10;
            """;

            using SqliteDataReader reader = command.ExecuteReader();

            if (!reader.Read())
            {
                // Log가 비어 있으면 기간을 표시할 수 없다.
                return ("", "");
            }

            // SQLite 결과가 null이면 ReadString에서 빈 문자열로 바뀐다.
            string startDate = ReadString(reader, 0);
            string endDate = ReadString(reader, 1);

            return (startDate, endDate);
        }

        // SQLite 연결을 생성한다.
        // Mode=ReadOnly라서 C:\Windows\Log.db 같은 실제 이력 파일을 수정하지 않고 조회만 한다.
        private SqliteConnection CreateConnection()
        {
            return new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        }

        // SQLite reader의 현재 행을 LogRow 객체로 변환한다.
        // SELECT 컬럼 순서와 아래 index가 반드시 맞아야 한다.
        private static LogRow ReadRow(SqliteDataReader reader)
        {
            return new LogRow
            {
                // 0: ID. 같은 시간 로그가 여러 개일 때 최신순 보조 정렬에 쓰인다.
                Id = ReadLong(reader, 0),
                // 1: GRP. 원본 DB 그룹값이며 화면에는 직접 표시하지 않아도 보존한다.
                Group = ReadString(reader, 1),
                // 2: DTIME. 예: 2026-06-01 17:04:17.
                DTime = ReadString(reader, 2),
                // 3: Type. 예: 중계기고장, AN고장, MCC, KEY.
                Type = ReadString(reader, 3),
                // 4: Action. 예: 발생, 복구, ON, OFF, 기동, 정지.
                Action = ReadString(reader, 4),
                // 5: Section. 예: 02# 01계통 005중계기.
                Section = ReadString(reader, 5),
                // 6: Contents. 예: 중계기 통신고장.
                Contents = ReadString(reader, 6),
                // 7: Packet. 예: NU..., CAU..., 비어 있으면 텍스트 기준으로 분류한다.
                Packet = ReadString(reader, 7)
            };
        }

        // DB 문자열 컬럼을 안전하게 읽는다.
        private static string ReadString(SqliteDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                // null 값은 화면/검색/분류에서 빈 문자열로 취급한다.
                return "";
            }

            return reader.GetValue(index)?.ToString() ?? "";
        }

        // DB 숫자 컬럼을 안전하게 읽는다.
        private static long ReadLong(SqliteDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                // ID가 null이면 정렬 보조값으로 쓸 수 없으므로 0으로 둔다.
                return 0;
            }

            return Convert.ToInt64(reader.GetValue(index));
        }
    }
}

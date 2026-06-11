using DbViewer.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DbViewer.Services.View
{
    public class LogRepository
    {
        private readonly string _dbPath;

        public LogRepository(string dbPath)
        {
            _dbPath = dbPath;
        }

        /// <summary>
        /// 선택된 SQLite DB가 이력 보기용 DB인지 검사한다.
        /// 
        /// 검사 내용:
        /// 1. SQLite 무결성 검사
        /// 2. Log 테이블 존재 여부 확인
        /// </summary>
        public void Validate()
        {
            using SqliteConnection connection = CreateConnection();
            connection.Open();

            using SqliteCommand integrityCommand = connection.CreateCommand();
            integrityCommand.CommandText = "PRAGMA integrity_check;";

            object? integrityResult = integrityCommand.ExecuteScalar();

            if (integrityResult?.ToString() != "ok")
            {
                throw new Exception($"DB 무결성 검사 실패: {integrityResult}");
            }

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
                throw new Exception("Log 테이블이 없습니다.");
            }
        }

        /// <summary>
        /// Log 테이블 전체 건수를 조회한다.
        /// 
        /// 화면에서 전체 로그 카드 숫자와 전체 페이지 수 계산에 사용한다.
        /// </summary>
        public int CountAllLogs()
        {
            using SqliteConnection connection = CreateConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Log;";

            object? result = command.ExecuteScalar();

            return Convert.ToInt32(result ?? 0);
        }

        /// <summary>
        /// 현재 페이지에 필요한 로그만 조회한다.
        /// 
        /// 전체 DB를 한 번에 메모리에 올리지 않고,
        /// LIMIT / OFFSET으로 필요한 구간만 읽는다.
        /// </summary>
        public List<LogRow> LoadLogsPage(int page, int pageSize)
        {
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

            command.Parameters.AddWithValue("$limit", safePageSize);
            command.Parameters.AddWithValue("$offset", offset);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(ReadRow(reader));
            }

            return rows;
        }

        /// <summary>
        /// 전체 로그를 한 줄씩 순차적으로 읽는다.
        /// 
        /// 카테고리별 건수 계산처럼 전체 DB를 훑어야 하지만,
        /// List에 전부 담을 필요가 없는 작업에서 사용한다.
        /// </summary>
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
                yield return ReadRow(reader);
            }
        }

        /// <summary>
        /// 검색 결과 전체 건수를 조회한다.
        /// </summary>
        public int CountSearchLogs(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return 0;
            }

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

            command.Parameters.AddWithValue("$keyword", like);

            object? result = command.ExecuteScalar();

            return Convert.ToInt32(result ?? 0);
        }

        /// <summary>
        /// 검색 결과를 페이지 단위로 조회한다.
        /// </summary>
        public List<LogRow> SearchLogsPage(string keyword, int page, int pageSize)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return new List<LogRow>();
            }

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
                rows.Add(ReadRow(reader));
            }

            return rows;
        }

        /// <summary>
        /// 기간 조회 결과 전체 건수를 조회한다.
        /// 
        /// startDate, endDate는 yyyy-MM-dd 형식 기준이다.
        /// </summary>
        public int CountDateLogs(string startDate, string endDate)
        {
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

        /// <summary>
        /// 기간 조회 결과를 페이지 단위로 조회한다.
        /// </summary>
        public List<LogRow> LoadLogsByDatePage(
            string startDate,
            string endDate,
            int page,
            int pageSize)
        {
            int safePage = Math.Max(1, page);
            int safePageSize = Math.Max(1, pageSize);
            int offset = (safePage - 1) * safePageSize;

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
                rows.Add(ReadRow(reader));
            }

            return rows;
        }

        /// <summary>
        /// DB 안의 실제 로그 날짜 범위를 조회한다.
        /// 
        /// 시작일/종료일 버튼에 DB 기준 날짜를 표시할 때 사용한다.
        /// </summary>
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
                return ("", "");
            }

            string startDate = ReadString(reader, 0);
            string endDate = ReadString(reader, 1);

            return (startDate, endDate);
        }

        /// <summary>
        /// SQLite 연결을 생성한다.
        /// 
        /// Mode=ReadOnly:
        /// - 조회 전용으로 연다.
        /// - 원본이 아니라 임시 복사본 DB를 읽는 구조에 적합하다.
        /// </summary>
        private SqliteConnection CreateConnection()
        {
            return new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        }

        /// <summary>
        /// SQLite reader의 현재 행을 LogRow 객체로 변환한다.
        /// </summary>
        private static LogRow ReadRow(SqliteDataReader reader)
        {
            return new LogRow
            {
                Id = ReadLong(reader, 0),
                Group = ReadString(reader, 1),
                DTime = ReadString(reader, 2),
                Type = ReadString(reader, 3),
                Action = ReadString(reader, 4),
                Section = ReadString(reader, 5),
                Contents = ReadString(reader, 6),
                Packet = ReadString(reader, 7)
            };
        }

        private static string ReadString(SqliteDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return "";
            }

            return reader.GetValue(index)?.ToString() ?? "";
        }

        private static long ReadLong(SqliteDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return 0;
            }

            return Convert.ToInt64(reader.GetValue(index));
        }
    }
}

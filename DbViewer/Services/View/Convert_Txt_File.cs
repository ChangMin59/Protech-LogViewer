using DbViewer.Models;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DbViewer.Services.View
{
    // TXT 이력 파일을 임시 SQLite DB로 변환한 결과다.
    // 성공하면 DbPath는 Log 테이블이 생성된 임시 DB 경로다.
    public sealed class TxtHistoryConvertResult
    {
        public bool Success { get; init; }
        public string DbPath { get; init; } = "";
        public string ErrorMessage { get; init; } = "";
    }

    public static class TxtHistoryConverter
    {
        // TXT 저장 파일을 읽어 화면이 기존 DB와 똑같이 처리할 수 있는 임시 Log DB로 바꾼다.
        // 입력 예: 2026-06-01 17:04:17     중계기고장     발생     02# 01계통 005중계기     중계기 통신고장
        public static TxtHistoryConvertResult ConvertToTempDb(string txtPath)
        {
            if (string.IsNullOrWhiteSpace(txtPath))
            {
                // 파일 선택 결과가 없으면 변환할 대상이 없다.
                return new TxtHistoryConvertResult
                {
                    Success = false,
                    ErrorMessage = "텍스트 파일 경로가 비어 있습니다."
                };
            }

            if (!File.Exists(txtPath))
            {
                // 사용자가 선택한 TXT가 삭제되었거나 경로가 잘못된 경우다.
                return new TxtHistoryConvertResult
                {
                    Success = false,
                    ErrorMessage = "텍스트 파일을 찾을 수 없습니다."
                };
            }

            List<LogRow> rows;

            try
            {
                // TXT 줄을 LogRow 목록으로 먼저 파싱한다.
                rows = ReadRows(txtPath);
            }
            catch (Exception ex)
            {
                return new TxtHistoryConvertResult
                {
                    Success = false,
                    ErrorMessage = $"텍스트 파일을 읽는 중 오류가 발생했습니다. {ex.Message}"
                };
            }

            if (rows.Count == 0)
            {
                // 날짜 형식으로 시작하는 실제 이력 줄을 하나도 찾지 못한 경우다.
                return new TxtHistoryConvertResult
                {
                    Success = false,
                    ErrorMessage = "텍스트 파일에서 이력 데이터를 찾을 수 없습니다."
                };
            }

            try
            {
                // TXT 원본은 그대로 두고, 프로그램 임시 폴더에 SQLite DB를 새로 만든다.
                string tempDir = Path.Combine(
                    Path.GetTempPath(),
                    "DbViewer",
                    "TxtHistory"
                );

                Directory.CreateDirectory(tempDir);

                // 같은 이름 충돌을 피하려고 밀리초까지 붙인다.
                string tempDbPath = Path.Combine(
                    tempDir,
                    $"TxtHistory_{DateTime.Now:yyyyMMdd_HHmmss_fff}.db"
                );

                if (File.Exists(tempDbPath))
                {
                    // 극히 드문 중복 생성 상황을 대비해 기존 임시 파일을 지운다.
                    File.Delete(tempDbPath);
                }

                // 변환 DB는 새로 만드는 파일이라 읽기/쓰기 기본 모드로 연다.
                using SqliteConnection connection = new($"Data Source={tempDbPath}");
                connection.Open();

                // 실제 DB와 같은 Log 테이블 구조로 만든다.
                // 이후 LogRepository는 DB/TXT 출처를 구분하지 않고 같은 쿼리를 사용한다.
                using SqliteCommand createCommand = connection.CreateCommand();
                createCommand.CommandText =
                    "CREATE TABLE Log (" +
                    "ID INTEGER PRIMARY KEY, " +
                    "GRP TEXT, " +
                    "DTIME TEXT, " +
                    "Type TEXT, " +
                    "Action TEXT, " +
                    "Section TEXT, " +
                    "Contents TEXT, " +
                    "Packet TEXT" +
                    ");";
                createCommand.ExecuteNonQuery();

                // 수십만 줄 TXT도 빠르게 넣기 위해 한 트랜잭션으로 묶는다.
                using SqliteTransaction transaction = connection.BeginTransaction();

                // 같은 INSERT 문을 재사용하고 값만 바꿔 넣는다.
                using SqliteCommand insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText =
                    "INSERT INTO Log " +
                    "(ID, GRP, DTIME, Type, Action, Section, Contents, Packet) " +
                    "VALUES " +
                    "($id, $grp, $dtime, $type, $action, $section, $contents, $packet);";

                SqliteParameter idParam = insertCommand.Parameters.Add("$id", SqliteType.Integer);
                SqliteParameter grpParam = insertCommand.Parameters.Add("$grp", SqliteType.Text);
                SqliteParameter dtimeParam = insertCommand.Parameters.Add("$dtime", SqliteType.Text);
                SqliteParameter typeParam = insertCommand.Parameters.Add("$type", SqliteType.Text);
                SqliteParameter actionParam = insertCommand.Parameters.Add("$action", SqliteType.Text);
                SqliteParameter sectionParam = insertCommand.Parameters.Add("$section", SqliteType.Text);
                SqliteParameter contentsParam = insertCommand.Parameters.Add("$contents", SqliteType.Text);
                SqliteParameter packetParam = insertCommand.Parameters.Add("$packet", SqliteType.Text);

                // TXT에는 원본 ID가 없으므로 최신순 정렬을 유지하도록 위에서부터 큰 ID를 부여한다.
                int total = rows.Count;

                for (int i = 0; i < rows.Count; i++)
                {
                    LogRow row = rows[i];

                    // 예: 첫 줄이 최신 로그이면 가장 큰 ID를 받아 ORDER BY DTIME DESC, ID DESC에서 먼저 나온다.
                    idParam.Value = total - i;
                    grpParam.Value = row.Group;
                    dtimeParam.Value = row.DTime;
                    typeParam.Value = row.Type;
                    actionParam.Value = row.Action;
                    sectionParam.Value = row.Section;
                    contentsParam.Value = row.Contents;
                    packetParam.Value = row.Packet;

                    insertCommand.ExecuteNonQuery();
                }

                // 모든 줄이 들어간 뒤 한 번에 확정한다.
                transaction.Commit();

                return new TxtHistoryConvertResult
                {
                    Success = true,
                    DbPath = tempDbPath
                };
            }
            catch (Exception ex)
            {
                return new TxtHistoryConvertResult
                {
                    Success = false,
                    ErrorMessage = $"텍스트 이력을 DB로 변환하는 중 오류가 발생했습니다. {ex.Message}"
                };
            }
        }

        // TXT 전체를 읽고 날짜로 시작하는 실제 이력 줄만 LogRow로 변환한다.
        private static List<LogRow> ReadRows(string txtPath)
        {
            string[] lines = ReadLines(txtPath);

            List<LogRow> rows = new();

            foreach (string line in lines)
            {
                // 빈 줄, 제목 줄, 날짜 형식이 아닌 줄은 null로 넘어온다.
                LogRow? row = ParseLine(line);

                if (row == null)
                {
                    continue;
                }

                rows.Add(row);
            }

            return rows;
        }

        // TXT 파일 인코딩을 읽는다.
        // UTF-8 BOM, UTF-8, CP949 순서로 시도해서 현장 PC의 한글 TXT를 최대한 그대로 읽는다.
        private static string[] ReadLines(string txtPath)
        {
            byte[] bytes = File.ReadAllBytes(txtPath);

            string text;

            if (bytes.Length >= 3 &&
                bytes[0] == 0xEF &&
                bytes[1] == 0xBB &&
                bytes[2] == 0xBF)
            {
                // BOM이 있으면 UTF-8 파일로 확정한다.
                text = Encoding.UTF8.GetString(bytes);
            }
            else
            {
                try
                {
                    // BOM이 없어도 정상 UTF-8이면 먼저 UTF-8로 읽는다.
                    UTF8Encoding strictUtf8 = new(
                        encoderShouldEmitUTF8Identifier: false,
                        throwOnInvalidBytes: true
                    );

                    text = strictUtf8.GetString(bytes);
                }
                catch
                {
                    // UTF-8이 아니면 국내 Windows TXT에서 흔한 CP949로 읽는다.
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    text = Encoding.GetEncoding(949).GetString(bytes);
                }
            }

            // 줄바꿈 형식을 \n으로 통일해 한 줄씩 파싱한다.
            return text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n');
        }

        // TXT 한 줄을 실제 LogRow 컬럼으로 분해한다.
        // 예: 날짜 / 구분 / 상태 / 위치 / 내용 순서이며, 컬럼 사이가 여러 칸 공백으로 벌어진 형식이다.
        private static LogRow? ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                // 빈 줄은 이력 데이터가 아니다.
                return null;
            }

            string trimmedLine = line.Trim();

            // 실제 이력 줄은 "yyyy-MM-dd HH:mm:ss"로 시작한다.
            if (!Regex.IsMatch(
                    trimmedLine,
                    @"^\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}"
                ))
            {
                return null;
            }

            // TXT 저장 형식은 컬럼 간격이 여러 칸이므로 2칸 이상 공백으로 나눈다.
            // 예: "2026-06-01 17:04:17     MCC     02# MCC 스위치 014번 기동     지하주차장..."
            string[] parts = Regex
                .Split(trimmedLine, @"\s{2,}")
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Trim())
                .ToArray();

            if (parts.Length < 3)
            {
                // 날짜/구분/나머지 값이 최소로도 없으면 이력 줄로 볼 수 없다.
                return null;
            }

            string dtime = parts[0];
            string type = parts[1];
            string action = "";
            string section = "";
            string contents = "";

            if (parts.Length >= 5)
            {
                // 표준 형식: 시간, 구분, 상태, 위치, 내용.
                // 예: 중계기고장 / 발생 / 02# 01계통 005중계기 / 중계기 통신고장.
                action = parts[2];
                section = parts[3];
                contents = string.Join(" ", parts.Skip(4)).Trim();
            }
            else if (parts.Length == 4)
            {
                if (IsKnownAction(parts[2]) && !LooksLikeSection(parts[2]))
                {
                    // 예: 시간 / 구분 / ON / 02# 수신기 스위치.
                    action = parts[2];
                    section = parts[3];
                    contents = "";
                }
                else
                {
                    // 예: MCC처럼 상태가 비고 위치/내용만 있는 줄이다.
                    action = "";
                    section = parts[2];
                    contents = parts[3];
                }
            }
            else if (parts.Length == 3)
            {
                if (IsKnownAction(parts[2]) && !LooksLikeSection(parts[2]))
                {
                    // 예: 시간 / 구분 / 복구 처럼 상태만 있는 짧은 줄이다.
                    action = parts[2];
                    section = "";
                    contents = "";
                }
                else
                {
                    // 예: 시간 / 구분 / 02# 수신기 같은 위치만 있는 줄이다.
                    action = "";
                    section = parts[2];
                    contents = "";
                }
            }

            // TXT에는 Packet이 없으므로 빈 값으로 두고 LogClassifier가 텍스트 기준으로 보정한다.
            return new LogRow
            {
                Id = 0,
                Group = "",
                DTime = dtime,
                Type = NormalizeType(type),
                Action = NormalizeAction(action),
                Section = section,
                Contents = contents,
                Packet = ""
            };
        }

        // TXT에서 MCC스위치로 온 구분을 화면/필터에서 쓰는 MCC 기준으로 맞춘다.
        private static string NormalizeType(string value)
        {
            string type = value.Trim();

            if (type == "MCC스위치")
            {
                // 예: Type=MCC스위치 -> Type=MCC.
                return "MCC";
            }

            return type;
        }

        // 상태값 앞뒤 공백만 정리한다.
        // 예: " 발생 " -> "발생", " ON " -> "ON".
        private static string NormalizeAction(string value)
        {
            return value.Trim();
        }

        // parts[2]가 상태인지 위치인지 구분하기 위한 실제 상태값 목록이다.
        private static bool IsKnownAction(string value)
        {
            string action = value.Trim();

            return action is
                "발생" or
                "소거" or
                "복구" or
                "해제" or
                "ON" or
                "OFF" or
                "기동" or
                "정지" or
                "자동" or
                "수동";
        }

        // parts[2]가 상태가 아니라 위치/장비명처럼 보이는지 확인한다.
        private static bool LooksLikeSection(string value)
        {
            string section = value.Trim();

            // 예: 02# 01계통 005중계기, 02# MCC 스위치, 02# 수신기.
            return Regex.IsMatch(section, @"^\d{2}#") ||
                   section.Contains("수신기") ||
                   section.Contains("중계반") ||
                   section.Contains("계통") ||
                   section.Contains("중계기") ||
                   section.Contains("MCC") ||
                   section.Contains("AN");
        }
    }
}

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
        private const int DateTimeWidth = 24;
        private const int TypeWidth = 16;
        private const int ActionWidth = 16;
        private const int NoPacketSectionStartWidth = 56;
        private const int NoPacketContentsStartWidth = 106;
        private const int WithPacketContentsStartWidth = 108;
        private const int WithPacketPacketStartWidth = 173;

        private enum TxtHistoryFormat
        {
            NoPacket,
            WithPacket
        }

        // TXT 저장 파일을 읽어 화면이 기존 DB와 똑같이 처리할 수 있는 임시 Log DB로 바꾼다.
        // 입력 예: 2026-06-01 17:04:17     중계기고장     발생     02# 01계통 005중계기     중계기 통신고장
        public static TxtHistoryConvertResult ConvertToTempDb(string txtPath)
        {
            return ConvertTxtHistoryToTempDb(txtPath);
        }

        // TXT 이력 파일을 고정폭 display width 기준으로 읽어 임시 SQLite DB로 변환한다.
        public static TxtHistoryConvertResult ConvertTxtHistoryToTempDb(string txtPath)
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
                    "ID INTEGER PRIMARY KEY AUTOINCREMENT, " +
                    "GRP INTEGER DEFAULT 0, " +
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
                    "(GRP, DTIME, Type, Action, Section, Contents, Packet) " +
                    "VALUES " +
                    "($grp, $dtime, $type, $action, $section, $contents, $packet);";

                SqliteParameter grpParam = insertCommand.Parameters.Add("$grp", SqliteType.Integer);
                SqliteParameter dtimeParam = insertCommand.Parameters.Add("$dtime", SqliteType.Text);
                SqliteParameter typeParam = insertCommand.Parameters.Add("$type", SqliteType.Text);
                SqliteParameter actionParam = insertCommand.Parameters.Add("$action", SqliteType.Text);
                SqliteParameter sectionParam = insertCommand.Parameters.Add("$section", SqliteType.Text);
                SqliteParameter contentsParam = insertCommand.Parameters.Add("$contents", SqliteType.Text);
                SqliteParameter packetParam = insertCommand.Parameters.Add("$packet", SqliteType.Text);

                for (int i = 0; i < rows.Count; i++)
                {
                    LogRow row = rows[i];

                    grpParam.Value = 0;
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
            TxtHistoryFormat format = DetectTxtHistoryFormat(lines);

            List<LogRow> rows = new();

            foreach (string line in lines)
            {
                // 빈 줄, 제목 줄, 날짜 형식이 아닌 줄은 null로 넘어온다.
                LogRow? row = ParseTxtHistoryLine(line, format);

                if (row == null)
                {
                    continue;
                }

                rows.Add(row);
            }

            return rows;
        }

        // 날짜/시간으로 시작하는 실제 로그 줄을 샘플링해 TXT 고정폭 형식을 판별한다.
        private static TxtHistoryFormat DetectTxtHistoryFormat(string[] lines)
        {
            int sampleCount = 0;
            int packetCandidateCount = 0;

            foreach (string line in lines)
            {
                if (!IsLogLine(line))
                {
                    continue;
                }

                sampleCount++;

                string packetCandidate = SliceByDisplayWidthToEnd(line, WithPacketPacketStartWidth).Trim();

                if (LooksLikePacket(packetCandidate))
                {
                    packetCandidateCount++;
                }

                if (sampleCount >= 200)
                {
                    break;
                }
            }

            if (sampleCount == 0)
            {
                return TxtHistoryFormat.NoPacket;
            }

            return packetCandidateCount * 2 >= sampleCount
                ? TxtHistoryFormat.WithPacket
                : TxtHistoryFormat.NoPacket;
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

        // TXT 한 줄을 판별된 고정폭 형식에 맞춰 실제 LogRow 컬럼으로 분해한다.
        private static LogRow? ParseTxtHistoryLine(string line, TxtHistoryFormat format)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                // 빈 줄은 이력 데이터가 아니다.
                return null;
            }

            if (!IsLogLine(line))
            {
                return null;
            }

            string dtime = SliceByDisplayWidth(line, 0, DateTimeWidth).Trim();
            string type = SliceByDisplayWidth(line, DateTimeWidth, DateTimeWidth + TypeWidth).Trim();
            string action = SliceByDisplayWidth(
                line,
                DateTimeWidth + TypeWidth,
                DateTimeWidth + TypeWidth + ActionWidth).Trim();

            int sectionEndWidth = format == TxtHistoryFormat.WithPacket
                ? WithPacketContentsStartWidth
                : NoPacketContentsStartWidth;
            int contentsStartWidth = sectionEndWidth;

            string section = SliceByDisplayWidth(
                line,
                NoPacketSectionStartWidth,
                sectionEndWidth).Trim();
            string contents = format == TxtHistoryFormat.WithPacket
                ? SliceByDisplayWidth(line, contentsStartWidth, WithPacketPacketStartWidth).Trim()
                : SliceByDisplayWidthToEnd(line, contentsStartWidth).Trim();
            string packet = format == TxtHistoryFormat.WithPacket
                ? SliceByDisplayWidthToEnd(line, WithPacketPacketStartWidth).Trim()
                : "";

            return new LogRow
            {
                Id = 0,
                Group = "",
                DTime = dtime,
                Type = NormalizeType(type),
                Action = NormalizeAction(action),
                Section = section,
                Contents = contents,
                Packet = packet
            };
        }

        // display width 구간으로 문자열을 자른다.
        private static string SliceByDisplayWidth(string text, int startWidth, int endWidth)
        {
            if (endWidth <= startWidth)
            {
                return "";
            }

            int startIndex = -1;
            int endIndex = text.Length;
            int displayWidth = 0;

            for (int i = 0; i < text.Length; i++)
            {
                int charWidth = IsWideCharacter(text[i]) ? 2 : 1;
                int nextDisplayWidth = displayWidth + charWidth;

                if (startIndex < 0 && displayWidth >= startWidth)
                {
                    startIndex = i;
                }

                if (nextDisplayWidth > endWidth)
                {
                    endIndex = i;
                    break;
                }

                displayWidth = nextDisplayWidth;
            }

            if (startIndex < 0)
            {
                return "";
            }

            return text[startIndex..endIndex];
        }

        // display width 시작 위치부터 끝까지 문자열을 자른다.
        private static string SliceByDisplayWidthToEnd(string text, int startWidth)
        {
            int startIndex = -1;
            int displayWidth = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (displayWidth >= startWidth)
                {
                    startIndex = i;
                    break;
                }

                displayWidth += IsWideCharacter(text[i]) ? 2 : 1;
            }

            return startIndex < 0
                ? ""
                : text[startIndex..];
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

        private static bool IsLogLine(string line)
        {
            return Regex.IsMatch(
                line,
                @"^\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}"
            );
        }

        // TXT 마지막 컬럼이 원본 코드(Packet)인지 확인한다.
        // 일반 한글 내용이 2칸 이상 공백으로 쪼개졌을 때 코드로 오인하지 않기 위한 방어다.
        private static bool LooksLikePacket(string value)
        {
            string packet = value.Trim();

            if (packet.Length < 2)
            {
                return false;
            }

            if (packet.Any(char.IsWhiteSpace))
            {
                return false;
            }

            return packet.Any(IsAsciiLetter) &&
                   packet.Any(char.IsDigit) &&
                   packet.All(ch => IsAsciiLetter(ch) || char.IsDigit(ch) || ch is '_' or '-' or '#');
        }

        private static bool IsAsciiLetter(char ch)
        {
            return ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
        }

        private static bool IsWideCharacter(char ch)
        {
            return ch >= 0x1100 &&
                   (ch <= 0x115F ||
                    ch == 0x2329 ||
                    ch == 0x232A ||
                    (ch >= 0x2E80 && ch <= 0xA4CF) ||
                    (ch >= 0xAC00 && ch <= 0xD7A3) ||
                    (ch >= 0xF900 && ch <= 0xFAFF) ||
                    (ch >= 0xFE10 && ch <= 0xFE19) ||
                    (ch >= 0xFE30 && ch <= 0xFE6F) ||
                    (ch >= 0xFF00 && ch <= 0xFF60) ||
                    (ch >= 0xFFE0 && ch <= 0xFFE6));
        }
    }
}

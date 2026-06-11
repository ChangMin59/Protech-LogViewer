using DbViewer.Models;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DbViewer.Services.View
{
    public sealed class TxtHistoryConvertResult
    {
        public bool Success { get; init; }
        public string DbPath { get; init; } = "";
        public string ErrorMessage { get; init; } = "";
    }

    public static class TxtHistoryConverter
    {
        public static TxtHistoryConvertResult ConvertToTempDb(string txtPath)
        {
            if (string.IsNullOrWhiteSpace(txtPath))
            {
                return new TxtHistoryConvertResult
                {
                    Success = false,
                    ErrorMessage = "텍스트 파일 경로가 비어 있습니다."
                };
            }

            if (!File.Exists(txtPath))
            {
                return new TxtHistoryConvertResult
                {
                    Success = false,
                    ErrorMessage = "텍스트 파일을 찾을 수 없습니다."
                };
            }

            List<LogRow> rows;

            try
            {
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
                return new TxtHistoryConvertResult
                {
                    Success = false,
                    ErrorMessage = "텍스트 파일에서 이력 데이터를 찾을 수 없습니다."
                };
            }

            try
            {
                string tempDir = Path.Combine(
                    Path.GetTempPath(),
                    "DbViewer",
                    "TxtHistory"
                );

                Directory.CreateDirectory(tempDir);

                string tempDbPath = Path.Combine(
                    tempDir,
                    $"TxtHistory_{DateTime.Now:yyyyMMdd_HHmmss_fff}.db"
                );

                if (File.Exists(tempDbPath))
                {
                    File.Delete(tempDbPath);
                }

                using SqliteConnection connection = new($"Data Source={tempDbPath}");
                connection.Open();

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

                using SqliteTransaction transaction = connection.BeginTransaction();

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

                int total = rows.Count;

                for (int i = 0; i < rows.Count; i++)
                {
                    LogRow row = rows[i];

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

        private static List<LogRow> ReadRows(string txtPath)
        {
            string[] lines = ReadLines(txtPath);

            List<LogRow> rows = new();

            foreach (string line in lines)
            {
                LogRow? row = ParseLine(line);

                if (row == null)
                {
                    continue;
                }

                rows.Add(row);
            }

            return rows;
        }

        private static string[] ReadLines(string txtPath)
        {
            byte[] bytes = File.ReadAllBytes(txtPath);

            string text;

            if (bytes.Length >= 3 &&
                bytes[0] == 0xEF &&
                bytes[1] == 0xBB &&
                bytes[2] == 0xBF)
            {
                text = Encoding.UTF8.GetString(bytes);
            }
            else
            {
                try
                {
                    UTF8Encoding strictUtf8 = new(
                        encoderShouldEmitUTF8Identifier: false,
                        throwOnInvalidBytes: true
                    );

                    text = strictUtf8.GetString(bytes);
                }
                catch
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    text = Encoding.GetEncoding(949).GetString(bytes);
                }
            }

            return text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n');
        }

        private static LogRow? ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            string trimmedLine = line.Trim();

            if (!Regex.IsMatch(
                    trimmedLine,
                    @"^\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}"
                ))
            {
                return null;
            }

            string[] parts = Regex
                .Split(trimmedLine, @"\s{2,}")
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Trim())
                .ToArray();

            if (parts.Length < 3)
            {
                return null;
            }

            string dtime = parts[0];
            string type = parts[1];
            string action = "";
            string section = "";
            string contents = "";

            if (parts.Length >= 5)
            {
                action = parts[2];
                section = parts[3];
                contents = string.Join(" ", parts.Skip(4)).Trim();
            }
            else if (parts.Length == 4)
            {
                if (IsKnownAction(parts[2]) && !LooksLikeSection(parts[2]))
                {
                    action = parts[2];
                    section = parts[3];
                    contents = "";
                }
                else
                {
                    action = "";
                    section = parts[2];
                    contents = parts[3];
                }
            }
            else if (parts.Length == 3)
            {
                if (IsKnownAction(parts[2]) && !LooksLikeSection(parts[2]))
                {
                    action = parts[2];
                    section = "";
                    contents = "";
                }
                else
                {
                    action = "";
                    section = parts[2];
                    contents = "";
                }
            }

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

        private static string NormalizeType(string value)
        {
            string type = value.Trim();

            if (type == "MCC스위치")
            {
                return "MCC";
            }

            return type;
        }

        private static string NormalizeAction(string value)
        {
            return value.Trim();
        }

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

        private static bool LooksLikeSection(string value)
        {
            string section = value.Trim();

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

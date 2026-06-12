using DbViewer.Models;
using System.IO;
using System.Text;

namespace DbViewer.Services.Export
{
    // 현재 화면 조건의 LogRow 목록을 사용자가 지정한 TXT 파일로 저장한다.
    // 출력 예: 2026-06-01 17:04:17     중계기고장     발생     02# 01계통 005중계기     중계기 통신고장
    public static class TextFileExporter
    {
        // 한글은 화면상 2칸 폭으로 보이므로 각 컬럼 폭을 고정해 TXT 정렬을 맞춘다.
        private const int DateTimeWidth = 24;
        private const int TypeWidth = 16;
        private const int ActionWidth = 16;
        private const int SectionWidth = 52;
        private const int ContentsWidthBeforePacket = 60;

        // sourceRowsFactory에서 넘어오는 로그를 한 줄씩 TXT 형식으로 쓴다.
        public static string Export(
            Func<IEnumerable<LogRow>> sourceRowsFactory,
            string outputPath)
        {
            // 저장 경로의 폴더가 없으면 먼저 만든다.
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            // Excel/메모장에서 한글이 깨지지 않게 UTF-8 BOM으로 저장한다.
            UTF8Encoding encoding = new(
                encoderShouldEmitUTF8Identifier: true,
                throwOnInvalidBytes: false
            );

            using StreamWriter writer = new(outputPath, append: false, encoding);

            foreach (LogRow row in sourceRowsFactory())
            {
                // 실제 로그 한 줄을 고정 폭 문자열로 쓰고, 보기 좋게 빈 줄 하나를 추가한다.
                writer.WriteLine(FormatRow(row));
                writer.WriteLine();
            }

            return outputPath;
        }

        // LogRow를 TXT 한 줄로 포맷한다.
        // Packet이 있으면 내용 뒤에 일정 간격을 두고 Packet까지 붙인다.
        private static string FormatRow(LogRow row)
        {
            // 기본 컬럼: 시간 / 구분 / 상태 / 위치.
            string line =
                PadDisplay(row.DTime, DateTimeWidth) +
                PadDisplay(row.Type, TypeWidth) +
                PadDisplay(row.Action, ActionWidth) +
                PadDisplay(row.Section, SectionWidth);

            string packet = row.Packet.Trim();

            if (string.IsNullOrWhiteSpace(packet))
            {
                // 예: Packet 없는 TXT/일반 로그는 "내용"까지만 저장한다.
                return (line + row.Contents).TrimEnd();
            }

            // 예: Packet=NU... 같은 원본 신호가 있으면 내용 뒤에 Packet을 추가한다.
            return (
                line +
                PadDisplay(row.Contents, ContentsWidthBeforePacket) +
                "     " +
                packet
            ).TrimEnd();
        }

        // 한글/영문 표시 폭을 고려해 지정 폭까지 공백을 채운다.
        private static string PadDisplay(string? value, int width)
        {
            string text = value ?? "";
            int displayWidth = GetDisplayWidth(text);

            if (displayWidth >= width)
            {
                // 컬럼 폭보다 긴 값은 자르지 않고 뒤에 한 칸만 띄워 데이터 손실을 막는다.
                return text + " ";
            }

            return text + new string(' ', width - displayWidth);
        }

        // 문자열이 TXT에서 차지하는 표시 폭을 계산한다.
        // 예: "AN"은 2칸, "중계기"는 6칸으로 본다.
        private static int GetDisplayWidth(string text)
        {
            int width = 0;

            foreach (char ch in text)
            {
                // 한글/전각 문자는 2칸, 영문/숫자는 1칸으로 계산한다.
                width += IsWideCharacter(ch)
                    ? 2
                    : 1;
            }

            return width;
        }

        // 한글, CJK, 전각 문자처럼 화면상 넓게 보이는 문자인지 확인한다.
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

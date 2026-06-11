using System.Windows.Media;

namespace DbViewer.Services.Common
{
    public sealed class BadgeStyle
    {
        public string Label { get; init; } = "";
        public Brush Fill { get; init; } = Brushes.Transparent;
        public Brush Border { get; init; } = Brushes.Transparent;
        public Brush Text { get; init; } = Brushes.Black;
    }

    public static class LogStyleMapper
    {
        /*
         * 밝은 WPF UI 전용 색상표다.
         *
         * 기준:
         * - 발생 로그: 아주 연한 배경색으로 강조
         * - 소거/복구 로그: 흰 배경 유지 + 글자색만 강조
         * - 배지 로그: 흰 배경 유지 + 내용 앞에 배지 표시
         * - 전체 UI가 흰색/글래스모피즘이라 너무 진한 색은 피한다.
         */

        private static readonly Dictionary<string, string> RowFills = new()
        {
            // 기본 행
            ["even_row"] = "#FFFFFFFF",
            ["odd_row"] = "#FFF7FAFC",

            // 화재
            ["fire_row"] = "#FFFFE8E8",          // 화재 발생: 연한 빨강 배경
            ["fire_text"] = "#FFFFFFFF",         // 화재 소거: 흰 배경 + 빨간 글자
            ["fire_strong_text"] = "#FFFFF4F4",  // 예보: 아주 연한 빨강 배경 + 진한 빨간 글자
            ["fire_soft_row"] = "#FFFFF1F1",     // 축적: 연한 빨강 배경

            // 제경보
            ["alarm_row"] = "#FFEAF3FF",         // 제경보 발생: 연한 파랑 배경
            ["alarm_text"] = "#FFFFFFFF",        // 제경보 소거: 흰 배경 + 파란 글자

            // 단선
            ["line_fault_row"] = "#FFFFF7D6",    // 단선 발생: 연한 노랑 배경
            ["line_fault_text"] = "#FFFFFFFF",   // 단선 복구: 흰 배경 + 노랑/주황 글자

            // AN 고장
            ["fault_row"] = "#FFFFF0DC",         // AN고장 발생: 연한 주황 배경
            ["fault_text"] = "#FFFFFFFF",        // AN고장 복구: 흰 배경 + 주황 글자

            // 수신기/중계반/중계기 고장
            ["receiver_fault_row"] = "#FFF3F0FF",
            ["receiver_fault_text"] = "#FFFFFFFF",

            ["panel_fault_row"] = "#FFE8FAFA",
            ["panel_fault_text"] = "#FFFFFFFF",

            ["relay_fault_row"] = "#FFF3E8FF",
            ["relay_fault_text"] = "#FFFFFFFF",

            // 배지형 로그는 행 배경을 진하게 하지 않는다.
            ["mcc_badge"] = "#FFFFFFFF",
            ["on_badge"] = "#FFFFFFFF",
            ["off_badge"] = "#FFFFFFFF",
            ["output_on_badge"] = "#FFFFFFFF",
            ["output_off_badge"] = "#FFFFFFFF",
            ["key_badge"] = "#FFFFFFFF",

            // 기타 고장/복구
            ["fault_soft_row"] = "#FFFFF6E8",
            ["fault_soft_text"] = "#FFFFFFFF",

            // 시스템 복구
            ["recover_row"] = "#FFE9FBEF"
        };

        private static readonly Dictionary<string, string> RowTexts = new()
        {
            // 기본 행
            ["even_row"] = "#FF102044",
            ["odd_row"] = "#FF102044",

            // 화재
            ["fire_row"] = "#FFB91C1C",
            ["fire_text"] = "#FFDC2626",
            ["fire_strong_text"] = "#FFB91C1C",
            ["fire_soft_row"] = "#FFB91C1C",

            // 제경보
            ["alarm_row"] = "#FF1467F2",
            ["alarm_text"] = "#FF1467F2",

            // 단선
            ["line_fault_row"] = "#FFB45309",
            ["line_fault_text"] = "#FFD97706",

            // AN 고장
            ["fault_row"] = "#FFC2410C",
            ["fault_text"] = "#FFF05A00",

            // 수신기/중계반/중계기 고장
            ["receiver_fault_row"] = "#FF6D28D9",
            ["receiver_fault_text"] = "#FF7C3FE8",

            ["panel_fault_row"] = "#FF008C86",
            ["panel_fault_text"] = "#FF008C86",

            ["relay_fault_row"] = "#FF7C3FE8",
            ["relay_fault_text"] = "#FF7C3FE8",

            // 배지형 로그
            ["mcc_badge"] = "#FF102044",
            ["on_badge"] = "#FF102044",
            ["off_badge"] = "#FF102044",
            ["output_on_badge"] = "#FF102044",
            ["output_off_badge"] = "#FF102044",
            ["key_badge"] = "#FF102044",

            // 기타
            ["fault_soft_row"] = "#FFF05A00",
            ["fault_soft_text"] = "#FFF05A00",

            // 시스템 복구
            ["recover_row"] = "#FF087A3A"
        };

        private static readonly Dictionary<string, BadgeStyle> Badges = new()
        {
            ["mcc_badge"] = new BadgeStyle
            {
                Label = "MCC",
                Fill = Hex("#FFF1E9FF"),
                Border = Hex("#FFB794F4"),
                Text = Hex("#FF6D28D9")
            },
            ["on_badge"] = new BadgeStyle
            {
                Label = "ON",
                Fill = Hex("#FFE8FBEF"),
                Border = Hex("#FF86EFAC"),
                Text = Hex("#FF15803D")
            },
            ["off_badge"] = new BadgeStyle
            {
                Label = "OFF",
                Fill = Hex("#FFF1F5F9"),
                Border = Hex("#FFCBD5E1"),
                Text = Hex("#FF475569")
            },
            ["output_on_badge"] = new BadgeStyle
            {
                Label = "출력 ON",
                Fill = Hex("#FFE5FAF7"),
                Border = Hex("#FF99F6E4"),
                Text = Hex("#FF008C86")
            },
            ["output_off_badge"] = new BadgeStyle
            {
                Label = "출력 OFF",
                Fill = Hex("#FFF1F5F9"),
                Border = Hex("#FFCBD5E1"),
                Text = Hex("#FF475569")
            },
            ["key_badge"] = new BadgeStyle
            {
                Label = "KEY",
                Fill = Hex("#FFEAF3FF"),
                Border = Hex("#FF93C5FD"),
                Text = Hex("#FF1467F2")
            }
        };

        public static Brush GetRowFill(string rowTag)
        {
            return Hex(RowFills.TryGetValue(rowTag, out string? color)
                ? color
                : "#FFFFFFFF");
        }

        public static Brush GetRowText(string rowTag)
        {
            return Hex(RowTexts.TryGetValue(rowTag, out string? color)
                ? color
                : "#FF102044");
        }

        public static BadgeStyle? GetBadge(string badgeKey)
        {
            return Badges.TryGetValue(badgeKey, out BadgeStyle? badge)
                ? badge
                : null;
        }

        public static Brush GridLineBrush()
        {
            return Hex("#FFE2E8F0");
        }

        private static SolidColorBrush Hex(string hex)
        {
            SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}

using System.Windows.Media;

namespace DbViewer.Services.Common
{
    // 내용 컬럼 앞에 붙는 작은 상태 배지의 색상 정보다.
    // 예: MCC, ON, OFF, 출력 ON, KEY.
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
         * 실제 데이터 기준:
         * - Type=화재/중계기고장/AN고장 + Action=발생: 연한 배경색으로 강조
         * - Action=소거/복구/해제: 흰 배경 유지 + 글자색만 강조
         * - Type=MCC/KEY/출력, Action=ON/OFF/기동/정지: 흰 배경 유지 + 내용 앞에 배지 표시
         * - 전체 UI가 흰색/글래스모피즘이라 너무 진한 색은 피한다.
         */

        // LogClassifier가 만든 rowTag별 행 배경색이다.
        // 예: relay_fault_row -> 중계기고장 발생 행 배경.
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

        // LogClassifier가 만든 rowTag별 글자색이다.
        // 예: fire_text -> 화재 복구/소거 로그 글자색.
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

        // 내용 앞에 표시되는 배지 색상표다.
        // 예: Type=MCC + 기동상태 -> MCC 배지와 ON 배지가 같이 붙을 수 있다.
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

        // rowTag에 맞는 행 배경 Brush를 반환한다.
        public static Brush GetRowFill(string rowTag)
        {
            // 등록되지 않은 rowTag는 일반 흰 배경으로 보여준다.
            return Hex(RowFills.TryGetValue(rowTag, out string? color)
                ? color
                : "#FFFFFFFF");
        }

        // rowTag에 맞는 글자색 Brush를 반환한다.
        public static Brush GetRowText(string rowTag)
        {
            // 등록되지 않은 rowTag는 기본 진한 남색 텍스트로 보여준다.
            return Hex(RowTexts.TryGetValue(rowTag, out string? color)
                ? color
                : "#FF102044");
        }

        // badgeKey에 맞는 배지 스타일을 반환한다.
        public static BadgeStyle? GetBadge(string badgeKey)
        {
            // 예: "on_badge" -> Label=ON, 초록 배지.
            return Badges.TryGetValue(badgeKey, out BadgeStyle? badge)
                ? badge
                : null;
        }

        // 로그 표의 셀 구분선 색상이다.
        public static Brush GridLineBrush()
        {
            return Hex("#FFE2E8F0");
        }

        // HEX 문자열을 WPF Brush로 만들고 Freeze해서 반복 렌더링 비용을 줄인다.
        private static SolidColorBrush Hex(string hex)
        {
            SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}

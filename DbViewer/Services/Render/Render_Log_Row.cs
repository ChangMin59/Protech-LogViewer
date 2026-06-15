using DbViewer.Models;
using DbViewer.Services.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DbViewer.Services.Render
{
    // LogRow 한 줄을 WPF 화면용 행 UI로 만든다.
    // 왼쪽 고정 영역은 시간, 오른쪽 스크롤 영역은 구분/상태/위치/내용/패킷이다.
    public static class Render_Log_Row
    {
        // 로그가 없거나 파일을 열지 않았을 때 표 안에 안내 행을 만든다.
        public static UIElement CreateEmptyRow(string message, FrameworkElement? widthSource = null)
        {
            // 실제 로그 행 높이와 맞춰 빈 상태에서도 표 레이아웃이 흔들리지 않게 한다.
            Border border = new()
            {
                Height = 58,
                MinWidth = 930,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(221, 232, 242)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            BindWidthToSource(border, widthSource);

            // 예: "이력 파일을 열어주세요.", "표시할 로그가 없습니다.".
            TextBlock text = new()
            {
                Text = message,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 94, 120)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = text;
            return border;
        }

        // 왼쪽 고정 시간 영역에 맞춰 빈 셀을 만든다.
        public static UIElement CreateFixedEmptyCell()
        {
            Border border = new()
            {
                Height = 58,
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 1, 1)
            };

            return border;
        }

        // 실제 로그의 DTIME을 왼쪽 고정 시간 셀로 만든다.
        // 예: row.DTime=2026-06-01 17:04:17.
        public static UIElement CreateFixedTimeCell(LogRow row, int index)
        {
            // 시간 셀도 같은 rowTag 색상을 써서 오른쪽 로그 내용과 배경이 이어져 보이게 한다.
            string rowTag = LogClassifier.ClassifyRowTag(row, index);

            Border border = new()
            {
                Height = 50,
                Background = LogStyleMapper.GetRowFill(rowTag),
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(0),
                SnapsToDevicePixels = true
            };

            // 한 줄 안에서 TextBlock이 가운데 정렬되도록 Grid를 둔다.
            Grid rootGrid = new()
            {
                SnapsToDevicePixels = true
            };

            // 시간이 길거나 비정상 값이면 말줄임 처리하고 전체 값은 Tooltip로 확인한다.
            TextBlock textBlock = new()
            {
                Text = row.DTime,
                ToolTip = row.DTime,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = LogStyleMapper.GetRowText(rowTag),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6, 6, 6, 6)
            };

            rootGrid.Children.Add(textBlock);
            border.Child = rootGrid;

            return border;
        }

        // 실제 로그의 구분/상태/위치/내용/패킷을 오른쪽 스크롤 행으로 만든다.
        // 예: 중계기고장 / 발생 / 02# 01계통 005중계기 / 중계기 통신고장 / Packet.
        public static UIElement CreateScrollableLogRow(LogRow row, int index, FrameworkElement? widthSource = null)
        {
            // rowTag는 행 배경색/글자색과 배지 계산의 기준이다.
            string rowTag = LogClassifier.ClassifyRowTag(row, index);
            // 예: Type=MCC, Contents=기동상태면 [mcc_badge, on_badge]가 들어올 수 있다.
            List<string> badgeKeys = LogClassifier.GetBadgeKeys(row, rowTag);

            Border border = new()
            {
                Height = 50,
                MinWidth = 930,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = LogStyleMapper.GetRowFill(rowTag),
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0),
                SnapsToDevicePixels = true
            };

            BindWidthToSource(border, widthSource);

            // 테두리/하이라이트 라인을 추가하기 쉽도록 최상위 Grid를 둔다.
            Grid rootGrid = new()
            {
                MinWidth = 930,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SnapsToDevicePixels = true
            };

            BindWidthToSource(rootGrid, widthSource);

            // 실제 로그 컬럼을 고정 폭+가변 폭으로 배치하는 Grid다.
            Grid contentGrid = new()
            {
                MinWidth = 930,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SnapsToDevicePixels = true
            };

            BindWidthToSource(contentGrid, widthSource);

            // Type: 예 "중계기고장", "MCC".
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            // Action: 예 "발생", "ON", "정지".
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            // Section: 예 "02# 01계통 005중계기".
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            // Contents: 긴 설명이 들어가므로 남은 폭을 사용한다.
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            // Packet: 원본 신호는 오른쪽 끝 고정 폭에 표시한다.
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            Brush textBrush = LogStyleMapper.GetRowText(rowTag);

            // 실제 표시 순서: 구분 / 상태 / 위치 / 내용 / 패킷.
            AddFixedWidthCell(contentGrid, row.Type, 0, true, textBrush);
            AddFixedWidthCell(contentGrid, row.Action, 1, true, textBrush);
            AddFixedWidthCell(contentGrid, row.Section, 2, false, textBrush);
            AddScrollableContentCell(contentGrid, row.Contents, 3, textBrush, badgeKeys);
            AddFixedWidthCell(contentGrid, row.Packet, 4, false, textBrush, HorizontalAlignment.Left);

            rootGrid.Children.Add(contentGrid);
            border.Child = rootGrid;

            return border;
        }

        // 헤더/목록이 쓰는 스크롤 영역 ActualWidth와 실제 행 폭을 같은 기준으로 묶는다.
        private static void BindWidthToSource(FrameworkElement target, FrameworkElement? widthSource)
        {
            if (widthSource == null)
            {
                return;
            }

            BindingOperations.SetBinding(
                target,
                FrameworkElement.WidthProperty,
                new Binding(nameof(FrameworkElement.ActualWidth))
                {
                    Source = widthSource
                });
        }

        // 고정 폭 텍스트 셀을 추가한다.
        // 예: Type, Action, Section, Packet 컬럼.
        private static void AddFixedWidthCell(
            Grid grid,
            string text,
            int column,
            bool center,
            Brush textBrush,
            HorizontalAlignment? forceAlignment = null)
        {
            // 각 셀 오른쪽에 구분선을 넣어 표 형태를 유지한다.
            Border cellBorder = new()
            {
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(5, 6, 5, 6)
            };

            // 긴 위치/패킷 값은 셀 안에서 말줄임 처리하고 Tooltip로 원문을 보여준다.
            TextBlock textBlock = new()
            {
                Text = text,
                ToolTip = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            // Type/Action은 가운데, Section/Packet은 왼쪽 정렬이 기본이다.
            HorizontalAlignment finalAlignment = forceAlignment ??
                                                 (center ? HorizontalAlignment.Center : HorizontalAlignment.Left);

            textBlock.HorizontalAlignment = finalAlignment;

            textBlock.TextAlignment = finalAlignment switch
            {
                // 강제 오른쪽 정렬이 필요해질 때도 TextAlignment를 같이 맞춘다.
                HorizontalAlignment.Right => TextAlignment.Right,
                HorizontalAlignment.Center => TextAlignment.Center,
                _ => TextAlignment.Left
            };

            cellBorder.Child = textBlock;

            Grid.SetColumn(cellBorder, column);
            grid.Children.Add(cellBorder);
        }

        // Contents 셀을 추가한다.
        // 배지형 로그는 Contents 앞에 MCC/ON/OFF 같은 배지를 먼저 붙인다.
        private static void AddScrollableContentCell(
            Grid grid,
            string text,
            int column,
            Brush textBrush,
            List<string> badgeKeys)
        {
            // Contents도 오른쪽 구분선을 유지한다.
            Border cellBorder = new()
            {
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(5, 6, 5, 6)
            };

            // 배지와 내용 텍스트를 한 줄로 나란히 배치한다.
            StackPanel panel = new()
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            foreach (string badgeKey in badgeKeys)
            {
                // LogClassifier가 만든 badgeKey를 실제 색상/라벨로 바꾼다.
                BadgeStyle? badge = LogStyleMapper.GetBadge(badgeKey);

                if (badge == null)
                {
                    // 등록되지 않은 배지는 표시하지 않는다.
                    continue;
                }

                // 예: "MCC", "ON", "OFF", "출력 ON" 같은 작은 라벨 박스다.
                Border badgeBorder = new()
                {
                    MinWidth = 38,
                    Height = 22,
                    CornerRadius = new CornerRadius(6),
                    Background = badge.Fill,
                    BorderBrush = badge.Border,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(7, 2, 7, 2),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                // 배지 내부 텍스트를 가운데 정렬한다.
                TextBlock badgeText = new()
                {
                    Text = badge.Label,
                    Foreground = badge.Text,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                badgeBorder.Child = badgeText;
                panel.Children.Add(badgeBorder);
            }

            // 실제 Contents 문구다. 예: "중계기 통신고장", "지하주차장 환기휀-지하5층 기동상태".
            TextBlock contentText = new()
            {
                Text = text,
                ToolTip = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            panel.Children.Add(contentText);
            cellBorder.Child = panel;

            Grid.SetColumn(cellBorder, column);
            grid.Children.Add(cellBorder);
        }
    }
}

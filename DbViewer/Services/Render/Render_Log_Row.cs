using DbViewer.Models;
using DbViewer.Services.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DbViewer.Services.Render
{
    public static class Render_Log_Row
    {
        public static UIElement CreateEmptyRow(string message)
        {
            Border border = new()
            {
                Height = 58,
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(221, 232, 242)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

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

        public static UIElement CreateFixedTimeCell(LogRow row, int index)
        {
            string rowTag = LogClassifier.ClassifyRowTag(row, index);

            Border border = new()
            {
                MinHeight = 50,
                Background = LogStyleMapper.GetRowFill(rowTag),
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(0),
                SnapsToDevicePixels = true
            };

            Grid rootGrid = new()
            {
                SnapsToDevicePixels = true
            };

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

        public static UIElement CreateScrollableLogRow(LogRow row, int index)
        {
            string rowTag = LogClassifier.ClassifyRowTag(row, index);
            List<string> badgeKeys = LogClassifier.GetBadgeKeys(row, rowTag);

            Border border = new()
            {
                MinHeight = 50,
                Width = 930,
                Background = LogStyleMapper.GetRowFill(rowTag),
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0),
                SnapsToDevicePixels = true
            };

            Grid rootGrid = new()
            {
                SnapsToDevicePixels = true
            };

            Grid contentGrid = new()
            {
                Width = 930,
                SnapsToDevicePixels = true
            };

            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            Brush textBrush = LogStyleMapper.GetRowText(rowTag);

            AddFixedWidthCell(contentGrid, row.Type, 0, true, textBrush);
            AddFixedWidthCell(contentGrid, row.Action, 1, true, textBrush);
            AddFixedWidthCell(contentGrid, row.Section, 2, false, textBrush);
            AddScrollableContentCell(contentGrid, row.Contents, 3, textBrush, badgeKeys);
            AddFixedWidthCell(contentGrid, row.Packet, 4, false, textBrush, HorizontalAlignment.Left);

            rootGrid.Children.Add(contentGrid);
            border.Child = rootGrid;

            return border;
        }

        private static void AddFixedWidthCell(
            Grid grid,
            string text,
            int column,
            bool center,
            Brush textBrush,
            HorizontalAlignment? forceAlignment = null)
        {
            Border cellBorder = new()
            {
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(5, 6, 5, 6)
            };

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

            HorizontalAlignment finalAlignment = forceAlignment ??
                                                 (center ? HorizontalAlignment.Center : HorizontalAlignment.Left);

            textBlock.HorizontalAlignment = finalAlignment;

            textBlock.TextAlignment = finalAlignment switch
            {
                HorizontalAlignment.Right => TextAlignment.Right,
                HorizontalAlignment.Center => TextAlignment.Center,
                _ => TextAlignment.Left
            };

            cellBorder.Child = textBlock;

            Grid.SetColumn(cellBorder, column);
            grid.Children.Add(cellBorder);
        }

        private static void AddScrollableContentCell(
            Grid grid,
            string text,
            int column,
            Brush textBrush,
            List<string> badgeKeys)
        {
            Border cellBorder = new()
            {
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(5, 6, 5, 6)
            };

            StackPanel panel = new()
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            foreach (string badgeKey in badgeKeys)
            {
                BadgeStyle? badge = LogStyleMapper.GetBadge(badgeKey);

                if (badge == null)
                {
                    continue;
                }

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

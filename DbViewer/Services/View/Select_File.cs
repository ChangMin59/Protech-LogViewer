using Microsoft.Win32;

namespace DbViewer.Services.View
{
    public static class Select_File
    {
        public static string? SelectHistoryFilePath()
        {
            OpenFileDialog dialog = new()
            {
                Title = "이력 파일 선택",
                Filter = "이력 파일 (*.db;*.txt)|*.db;*.txt|SQLite DB (*.db)|*.db|텍스트 이력 (*.txt)|*.txt|모든 파일 (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            return dialog.FileName;
        }
    }
}

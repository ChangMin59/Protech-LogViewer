using System;
using System.IO;

namespace DbViewer.Services.View
{
    public static class Auto_File_Find
    {
        public static bool TryFindHistoryFile(out string historyFilePath)
        {
            historyFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Log.db"
            );

            return File.Exists(historyFilePath);
        }
    }
}

using System;
using System.IO;
using System.Threading.Tasks;

namespace DbViewer.Services.Recovery
{
    public sealed class StartRecoveryResult
    {
        public bool SourceFound { get; init; }
        public DbRecoveryResult? RecoveryResult { get; init; }
    }

    public static class Start_Recovery
    {
        public static async Task<StartRecoveryResult> RunAsync()
        {
            string sourceDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Log.db"
            );

            if (!File.Exists(sourceDbPath))
            {
                return new StartRecoveryResult
                {
                    SourceFound = false
                };
            }

            DbRecoveryResult recoveryResult = await Task.Run(() =>
                DbRecovery.Recover(sourceDbPath)
            );

            return new StartRecoveryResult
            {
                SourceFound = true,
                RecoveryResult = recoveryResult
            };
        }
    }
}

using System;

namespace AlphaBleedFixer
{
    internal sealed class ProcessOptions
    {
        public int Padding { get; set; } = 32;
        public bool Recursive { get; set; } = true;
        public bool CreateBackup { get; set; } = true;
    }

    internal enum ProcessStatus
    {
        Changed,
        Skipped,
        Failed
    }

    internal sealed class ProcessResult
    {
        public string FilePath { get; set; }
        public ProcessStatus Status { get; set; }
        public int ChangedPixels { get; set; }
        public string BackupPath { get; set; }
        public string Message { get; set; }

        public string ToLogLine()
        {
            var label = Status == ProcessStatus.Changed ? "変更" : Status == ProcessStatus.Skipped ? "スキップ" : "失敗";
            var detail = Status == ProcessStatus.Changed ? ChangedPixels + " px" : Message;
            return string.Format("[{0}] {1}  {2}", label, FilePath, detail);
        }
    }
}

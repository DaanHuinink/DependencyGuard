namespace Abstractions
{
    public interface IFileService
    {
        string ReadAllText(string path);
        void WriteAllText(string path, string content);
        bool Exists(string path);
    }
}

namespace FileOperations
{
    using System.IO;    // Allowed: FileOperations → System.IO overrides the deny.
    using Abstractions; // Allowed: every namespace may use Abstractions.

    public sealed class LocalFileService : IFileService
    {
        public string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public void WriteAllText(string path, string content)
        {
            File.WriteAllText(path, content);
        }

        public bool Exists(string path)
        {
            return File.Exists(path);
        }
    }
}

namespace Services
{
    using Abstractions; // Allowed: every namespace may use Abstractions.

    public sealed class DocumentService(IFileService fileService)
    {
        public string LoadDocument(string path)
        {
            return fileService.ReadAllText(path);
        }

        public void SaveDocument(string path, string content)
        {
            fileService.WriteAllText(path, content);
        }
    }
}

namespace Services
{
    using System.IO; // DG0001: System.IO is denied outside FileOperations.

    public sealed class ReportService
    {
        public string GenerateReport(string outputPath)
        {
            string report = $"Report generated at {DateTime.UtcNow}";
            File.WriteAllText(outputPath, report);
            return report;
        }
    }
}

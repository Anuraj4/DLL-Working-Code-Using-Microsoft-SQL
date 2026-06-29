using System;
using System.IO;
using Serilog;

namespace Edi.Generator835.Services
{
    public class ExcelTemplateService
    {
        private readonly string _templatePath;
        private readonly string _baseOutputPath;

        public ExcelTemplateService(string templatePath, string baseOutputPath)
        {
            _templatePath = templatePath;
            _baseOutputPath = baseOutputPath;
        }

        public (string RootFolder, string MappedFolder, string EdiFolder, string LogsFolder) CreateOutputFolders(int csvCount)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string rootFolderName = $"{timestamp}_{csvCount}_csv_processed";
            string rootPath = Path.Combine(_baseOutputPath, rootFolderName);

            string mappedPath = Path.Combine(rootPath, "Mapped_Excel");
            string ediPath = Path.Combine(rootPath, "Generated_EDI_835");
            string logsPath = Path.Combine(rootPath, "Logs");

            if (!Directory.Exists(rootPath)) Directory.CreateDirectory(rootPath);
            if (!Directory.Exists(mappedPath)) Directory.CreateDirectory(mappedPath);
            if (!Directory.Exists(ediPath)) Directory.CreateDirectory(ediPath);
            if (!Directory.Exists(logsPath)) Directory.CreateDirectory(logsPath);

            Log.Information("Created batch output directory structure at: {Path}", rootPath);

            return (rootPath, mappedPath, ediPath, logsPath);
        }

        public string PrepareOutputPath(string outputFolder, string csvFileName)
        {
            string baseName = Path.GetFileNameWithoutExtension(csvFileName);
            string outputFileName = $"{baseName}_Mapped.xlsx";
            string destPath = Path.Combine(outputFolder, outputFileName);

            if (File.Exists(_templatePath))
            {
                File.Copy(_templatePath, destPath, true);
                Log.Debug("Copied template to: {Path}", destPath);
            }
            else
            {
                Log.Warning("Template file not found at {TemplatePath}. A new workbook will be created instead.", _templatePath);
            }

            return destPath;
        }
    }
}

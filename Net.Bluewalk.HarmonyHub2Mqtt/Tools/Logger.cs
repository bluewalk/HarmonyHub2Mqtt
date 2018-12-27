using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Net.Bluewalk.LogTools
{
    public static class Logger
    {
        private static readonly string _logFileTemplate = Path.Combine(Path.GetTempPath(), Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? System.Reflection.Assembly.GetExecutingAssembly()?.Location))
            + "{0}.log";

        private static readonly string _currentLogFile = string.Format(_logFileTemplate, string.Empty);
        
        public static void LogException(Exception e)
        {
            var sb = new StringBuilder();
            CreateExceptionString(sb, e, string.Empty);

            LogMessage(sb.ToString());
        }

        private static void CreateExceptionString(StringBuilder sb, Exception e, string indent)
        {
            if (e == null) return;

            if (indent == null)
            {
                indent = string.Empty;
            }
            else if (indent.Length > 0)
            {
                sb.AppendFormat("{0}Inner " + Environment.NewLine, indent);
            }

            sb.AppendFormat("Exception Found: {0} - Type: {1}" + Environment.NewLine, indent, e.GetType().FullName);
            sb.AppendFormat(" - {0}Message: {1}" + Environment.NewLine, indent, e.Message);
            sb.AppendFormat(" - {0}Source: {1}" + Environment.NewLine, indent, e.Source);
            sb.AppendFormat(" - {0}Stacktrace: {1}" + Environment.NewLine, indent, e.StackTrace);

            if (e.InnerException == null) return;

            for (var eCurrent = e.InnerException; eCurrent != null; eCurrent = eCurrent.InnerException)
            {
                sb.Append(Environment.NewLine);
                CreateExceptionString(sb, eCurrent.InnerException, indent + "  ");
            }
        }

        public static void LogMessage(string message)
        {
            message = $"{DateTime.Now:G}: {message}";
            LogMessageToFile(message);

#if DEBUG
            Debugger.Log(0, "DEBUG", message + Environment.NewLine);
#endif
        }

        public static void LogMessage(string message, params object[] parameters)
        {
            LogMessage(string.Format(message, parameters));
        }

        public static void Initialize()
        {
            if (File.Exists(_currentLogFile) && File.GetCreationTime(_currentLogFile).Date <= DateTime.Now.Date.AddDays(-1))
                LogRotate();
        }

        private static void LogMessageToFile(string message)
        {
            try
            {
                var streamWriter = File.AppendText(_currentLogFile);
                try
                {
                    streamWriter.WriteLine(message);
                }
                finally
                {
                    streamWriter.Close();
                }
            }
            catch { }
        }

        public static void LogRotate()
        {
            var archived = string.Format(_logFileTemplate, "-" + DateTime.Now.Date.AddDays(-1).ToString("yyyyMMdd"));
            var rotateMaxDays = 7;

            LogMessage("LogRotation: Rotating today's log");
            if (File.Exists(_currentLogFile) && !File.Exists(archived))
                File.Move(_currentLogFile, archived);
            
            File.WriteAllText(_currentLogFile, string.Empty);
            File.SetCreationTime(_currentLogFile, DateTime.Now);

            LogMessage("LogRotation: Cleaning up logs older than {0}", DateTime.Now.Date.AddDays(-rotateMaxDays));
            var info = new DirectoryInfo(Path.GetDirectoryName(_currentLogFile));
            var files = info
                .GetFiles(Path.GetFileName(string.Format(_logFileTemplate, "-*")))
                .Where(p => p.LastWriteTime < DateTime.Now.Date.AddDays(-rotateMaxDays))
                .ToArray();

            foreach (var file in files)
            {
                LogMessage("LogRotation: Deleting {0}", file.Name);
                file.Delete();
            }
        }
    }
}

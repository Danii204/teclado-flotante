using System;
using System.IO;
using System.Text;

namespace TecladoFlotante
{
    /// <summary>
    /// Registro local de errores (%APPDATA%\TecladoFlotante\log.txt, máx. ~256 KB).
    /// Nunca se envía a ningún sitio: solo sirve para diagnosticar fallos.
    /// </summary>
    public static class Log
    {
        const long MaxBytes = 256 * 1024;
        static readonly object gate = new object();

        public static string FilePath { get { return Path.Combine(Settings.Folder, "log.txt"); } }

        public static void Info(string message) { Write("INFO ", message); }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", ex == null ? message : message + Environment.NewLine + ex);
        }

        static void Write(string level, string message)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Settings.Folder);
                    FileInfo fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = Path.Combine(Settings.Folder, "log.old.txt");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " [" + BuildInfo.Version + "] " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch { /* el registro nunca debe tumbar la aplicación */ }
        }
    }
}

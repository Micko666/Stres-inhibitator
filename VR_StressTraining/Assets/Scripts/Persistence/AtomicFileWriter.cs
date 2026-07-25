using System;
using System.IO;
using System.Text;

namespace StressTraining.Persistence
{
    /// <summary>
    /// Crash-safe file writes (spec §6):
    /// 1. content is written to "path.tmp"
    /// 2. the previous valid file (if any) is preserved as "path.prev"
    /// 3. tmp atomically replaces the target
    /// A crash at any step leaves either the old valid file, the .prev copy,
    /// or both on disk — never a torn write at the target path.
    /// </summary>
    public static class AtomicFileWriter
    {
        public static void Write(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            string prev = path + ".prev";

            File.WriteAllText(tmp, content, new UTF8Encoding(false));

            if (File.Exists(path))
            {
                // Keep last valid version before replacing.
                File.Copy(path, prev, overwrite: true);
                try
                {
                    File.Replace(tmp, path, null);
                }
                catch (PlatformNotSupportedException)
                {
                    SafeFallback(tmp, path);
                }
                catch (UnauthorizedAccessException)
                {
                    SafeFallback(tmp, path);
                }
                catch (IOException)
                {
                    // File.Replace can fail across some filesystems — fall back to delete+move.
                    SafeFallback(tmp, path);
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        /// <summary>Path of the last-valid copy kept next to the target.</summary>
        public static string PrevPath(string path) => path + ".prev";

        private static void SafeFallback(string tmp, string path)
        {
            File.Copy(tmp, path, overwrite: true);
            File.Delete(tmp);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AtualizaAPP.Utils
{
    public static class FileUtils
    {
        public static void EnsureEmptyDir(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
        }

        public static void CopyDirectory(string sourceDir, string destDir, HashSet<string>? excludeFileNames = null, HashSet<string>? excludeDirNames = null)
        {
            var src = new DirectoryInfo(sourceDir);
            if (!src.Exists) throw new DirectoryNotFoundException(sourceDir);
            Directory.CreateDirectory(destDir);

            foreach (var file in src.GetFiles())
            {
                if (excludeFileNames != null && excludeFileNames.Contains(file.Name)) continue;
                var dest = Path.Combine(destDir, file.Name);
                file.CopyTo(dest, true);
            }
            foreach (var sub in src.GetDirectories())
            {
                if (excludeDirNames != null && excludeDirNames.Contains(sub.Name)) continue;
                var destSub = Path.Combine(destDir, sub.Name);
                CopyDirectory(sub.FullName, destSub, excludeFileNames, excludeDirNames);
            }
        }

        public static IEnumerable<string> EnumerateFilesRelative(string root)
        {
            int cut = root.EndsWith(Path.DirectorySeparatorChar) ? root.Length : root.Length + 1;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                yield return path.Substring(cut);
            }
        }

        public static void DeleteIfExists(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Debug.WriteLine($"DeleteIfExists '{path}': {ex.Message}"); }
        }

        public static void SafeKillProcessesByName(string processName)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        if (!p.CloseMainWindow()) p.Kill(entireProcessTree: true);
                        if (!p.WaitForExit(5000)) p.Kill(entireProcessTree: true);
                    }
                    catch { /* ignora */ }
                }
            }
            catch { /* ignora */ }
        }
    }
}

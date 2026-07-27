using System;
using System.IO;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Shared crash-safe text replacement used by local snapshots and the
    /// Authority command outbox. It never decides what data is durable.
    /// </summary>
    public static class OntologyAtomicFileStore
    {
        public static void WriteAllText(
            string path,
            string contents,
            bool keepBackup)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "A destination path is required.",
                    nameof(path));
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = path + ".tmp";
            var backupPath = path + ".bak";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            try
            {
                File.WriteAllText(tempPath, contents ?? string.Empty);
                if (File.Exists(path))
                {
                    if (keepBackup)
                    {
                        if (File.Exists(backupPath))
                        {
                            File.Delete(backupPath);
                        }

                        File.Move(path, backupPath);
                    }
                    else
                    {
                        File.Delete(path);
                    }
                }

                File.Move(tempPath, path);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                throw;
            }
        }
    }
}

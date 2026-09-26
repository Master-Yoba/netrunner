namespace Netrunner.Match;

/// <summary>Flush a same-directory temporary file before an atomic rename. One writer per destination.</summary>
public static class AtomicFile
{
    public static void Write(string path, byte[] bytes, Action<string>? validate = null, string? backupPath = null)
    {
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            validate?.Invoke(temporary);
            // Stage and flush the previous valid generation before publishing.
            // A failure in either write leaves the primary untouched.
            if (backupPath is not null && File.Exists(path))
                Write(backupPath, File.ReadAllBytes(path), validate);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

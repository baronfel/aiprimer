namespace Primer.Utils;

/// <summary>
/// File system utilities for directory and file operations.
/// </summary>
public static class FileUtils
{
    /// <summary>
    /// Ensures a directory exists, creating it and parent directories if needed.
    /// </summary>
    /// <param name="dirPath">The directory path to create.</param>
    public static void EnsureDir(string dirPath)
    {
        ArgumentNullException.ThrowIfNull(dirPath);
        Directory.CreateDirectory(dirPath);
    }

    /// <summary>
    /// Writes content to a file, optionally skipping if the file already exists.
    /// </summary>
    /// <param name="filePath">The file path to write to.</param>
    /// <param name="content">The content to write.</param>
    /// <param name="force">If true, overwrites existing files; otherwise skips existing files.</param>
    /// <returns>A message describing the action taken.</returns>
    public static string SafeWriteFile(string filePath, string content, bool force)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(content);

        var exists = File.Exists(filePath);
        if (exists && !force)
        {
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath);
            return $"Skipped {relativePath} (exists)";
        }

        File.WriteAllText(filePath, content);
        var relativeWritePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath);
        return $"Wrote {relativeWritePath}";
    }

    /// <summary>
    /// Asynchronously writes content to a file, optionally skipping if the file already exists.
    /// </summary>
    /// <param name="filePath">The file path to write to.</param>
    /// <param name="content">The content to write.</param>
    /// <param name="force">If true, overwrites existing files; otherwise skips existing files.</param>
    /// <returns>A message describing the action taken.</returns>
    public static async Task<string> SafeWriteFileAsync(string filePath, string content, bool force)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(content);

        var exists = File.Exists(filePath);
        if (exists && !force)
        {
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath);
            return $"Skipped {relativePath} (exists)";
        }

        await File.WriteAllTextAsync(filePath, content);
        var relativeWritePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath);
        return $"Wrote {relativeWritePath}";
    }
}

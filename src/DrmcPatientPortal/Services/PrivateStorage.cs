namespace DrmcPatientPortal.Services;

// All caller-supplied components are leaf names. Check every existing ancestor as well as
// canonical containment: GetFullPath alone does not resolve junctions or symbolic links.
public sealed class PrivateStorageRoot
{
    public string Path { get; }
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public PrivateStorageRoot(IWebHostEnvironment environment, string configuredPath)
    {
        Path = ValidatePath(environment, configuredPath);
        var web = System.IO.Path.GetFullPath(environment.WebRootPath ?? System.IO.Path.Combine(environment.ContentRootPath, "wwwroot"));
        CheckLinks(web);
        CheckLinks(Path);
    }

    // Configuration validation must not depend on an SMB share currently being online.
    public static string ValidatePath(IWebHostEnvironment environment, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) throw new InvalidOperationException("Private storage requires a directory.");
        var content = System.IO.Path.GetFullPath(environment.ContentRootPath);
        var path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(configuredPath, content));
        var web = System.IO.Path.GetFullPath(environment.WebRootPath ?? System.IO.Path.Combine(content, "wwwroot"));
        if ((!System.IO.Path.IsPathRooted(configuredPath) && !Contains(content, path)) ||
            Contains(web, path) || Contains(path, web))
            throw new InvalidOperationException("Private storage must be outside wwwroot and relative paths must remain inside the content root.");
        return path;
    }

    public static bool IsLeaf(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 255 &&
        value is not "." and not ".." && value[0] != '.' && value[^1] is not '.' and not ' ' &&
        !value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c));

    public static bool IsGuid(string? value) => value?.Length == 32 && Guid.TryParseExact(value, "N", out _);

    private static bool Contains(string root, string candidate)
    {
        var normalized = System.IO.Path.TrimEndingDirectorySeparator(root);
        var prefix = System.IO.Path.EndsInDirectorySeparator(normalized) ? normalized : normalized + System.IO.Path.DirectorySeparatorChar;
        return candidate.Equals(normalized, Comparison) || candidate.StartsWith(prefix, Comparison);
    }

    public static bool PathsOverlap(string first, string second) => Contains(first, second) || Contains(second, first);
    public bool Overlaps(PrivateStorageRoot other) => PathsOverlap(Path, other.Path);

    public string Resolve(params string[] components)
    {
        if (components.Any(c => !IsLeaf(c))) throw new FileNotFoundException("Invalid private file reference.");
        var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine([Path, .. components]));
        if (!Contains(Path, candidate)) throw new FileNotFoundException("Invalid private file reference.");
        CheckLinks(candidate);
        return candidate;
    }

    public IEnumerable<string> Entries(params string[] components)
    {
        var directory = Resolve(components);
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFileSystemEntries(directory).Select(entry => Resolve([.. components, System.IO.Path.GetFileName(entry)])).ToArray();
    }

    private static void CheckLinks(string candidate)
    {
        for (var current = candidate; !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Private storage cannot traverse links or reparse points.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}

public static class PrivateStorageIO
{
    public static async Task<byte[]> ReadAsync(string path, int limit, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await ReadBoundedAsync(input, limit, token);
    }

    public static async Task<byte[]> ReadBoundedAsync(Stream input, int limit, CancellationToken token)
    {
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("The private file exceeds its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
        return output.ToArray();
    }

    public static async Task WriteNewAsync(string path, byte[] ciphertext, CancellationToken token)
    {
        var created = false;
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            await output.WriteAsync(ciphertext, token);
            output.Flush(flushToDisk: true);
        }
        catch
        {
            if (created) File.Delete(path);
            throw;
        }
    }
}

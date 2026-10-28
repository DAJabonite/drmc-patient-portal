using System.Security.Cryptography;

namespace DrmcPatientPortal.Services;

public interface IStagedResultQuarantine
{
    Task TransferAsync(string source, string destination, CancellationToken token);
}

// A verified copy/finalize/delete works on different volumes and SMB shares as well as locally.
// Existing destinations are verified, never overwritten, so interruption is recoverable.
public sealed class StagedResultQuarantine : IStagedResultQuarantine
{
    public async Task TransferAsync(string source, string destination, CancellationToken token)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, "transfer.partial");
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var hash = await SHA256.HashDataAsync(input, token);
            if (!File.Exists(destination))
            {
                input.Position = 0;
                if (File.Exists(temporary))
                {
                    if ((File.GetAttributes(temporary) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Quarantine transfer cannot traverse a link.");
                    File.Delete(temporary);
                }
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await input.CopyToAsync(output, token);
                    output.Flush(true);
                }
                await using (var verify = File.OpenRead(temporary))
                {
                    var copiedHash = await SHA256.HashDataAsync(verify, token);
                    if (!hash.AsSpan().SequenceEqual(copiedHash)) throw new IOException("Quarantine transfer verification failed.");
                }
                File.Move(temporary, destination);
            }
            await using (var verify = File.OpenRead(destination))
            {
                var copiedHash = await SHA256.HashDataAsync(verify, token);
                if (!hash.AsSpan().SequenceEqual(copiedHash)) throw new IOException("Quarantine destination conflicts with the claimed file.");
            }
        }
        token.ThrowIfCancellationRequested();
        File.Delete(source);
        if (File.Exists(temporary)) File.Delete(temporary);
    }
}

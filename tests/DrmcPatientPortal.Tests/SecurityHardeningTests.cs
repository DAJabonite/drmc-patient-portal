using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using System.Xml.Linq;

namespace DrmcPatientPortal.Tests;

public class SecurityHardeningTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task PatientDocumentStorage_BindsTokenToSessionAndEncryptsAtRest()
    {
        var root = Path.Combine(Path.GetTempPath(), "drmc-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(root);
            var service = new PatientDocumentStorage(
                environment.Object,
                Options.Create(new PatientDocumentStorageOptions { RootPath = "documents", TemporaryPath = "temporary" }),
                new EphemeralDataProtectionProvider());
            await using var stream = new MemoryStream(TinyPng);
            var upload = new FormFile(stream, 0, TinyPng.Length, "file", "identity.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };

            var staged = await service.StageAsync(upload, "session-a");
            await Assert.ThrowsAsync<InvalidDataException>(() => service.CommitAsync(staged.Token, "session-b", "patient-1", "front"));
            var stored = await service.CommitAsync(staged.Token, "session-a", "patient-1", "front");
            var encrypted = await File.ReadAllBytesAsync(Path.Combine(root, "documents", "patient-1", stored.FileName));
            Assert.False(encrypted.AsSpan().StartsWith(TinyPng));
            Assert.Equal(TinyPng, await service.ReadAsync("patient-1", stored.FileName, stored.StorageVersion));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PatientDocumentStorage_RejectsDisguisedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "drmc-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(root);
            var service = new PatientDocumentStorage(environment.Object, Options.Create(new PatientDocumentStorageOptions()), new EphemeralDataProtectionProvider());
            await using var stream = new MemoryStream("not an image"u8.ToArray());
            var upload = new FormFile(stream, 0, stream.Length, "file", "identity.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
            await Assert.ThrowsAsync<InvalidDataException>(() => service.StageAsync(upload, "session-a"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PatientDocumentStorage_RejectsOversizeAndTraversalAndDiscardsStaging()
    {
        var root = Path.Combine(Path.GetTempPath(), "drmc-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(root);
            var service = new PatientDocumentStorage(
                environment.Object,
                Options.Create(new PatientDocumentStorageOptions { RootPath = "documents", TemporaryPath = "temporary", MaximumFileSizeMb = 1 }),
                new EphemeralDataProtectionProvider());

            await using var oversizedStream = new MemoryStream(new byte[1024 * 1024 + 1]);
            var oversized = new FormFile(oversizedStream, 0, oversizedStream.Length, "file", "large.png");
            await Assert.ThrowsAsync<InvalidDataException>(() => service.StageAsync(oversized, "session-a"));
            await Assert.ThrowsAsync<FileNotFoundException>(() => service.ReadAsync("patient-1", "../secret.idp", 2));

            await using var validStream = new MemoryStream(TinyPng);
            var valid = new FormFile(validStream, 0, TinyPng.Length, "file", "identity.png");
            var staged = await service.StageAsync(valid, "session-a");
            await service.DiscardStagedAsync(staged.Token, "session-a");
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "temporary")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LocalizationResources_HaveMatchingKeys()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var resourceRoot = Path.Combine(projectRoot, "src", "DrmcPatientPortal", "Resources");
        static string[] Keys(string path) => XDocument.Load(path).Descendants("data")
            .Select(x => (string?)x.Attribute("name")).Where(x => x is not null).Cast<string>().Order().ToArray();
        var english = Keys(Path.Combine(resourceRoot, "SharedResource.en.resx"));
        Assert.Equal(english, Keys(Path.Combine(resourceRoot, "SharedResource.fil.resx")));
        Assert.Equal(english, Keys(Path.Combine(resourceRoot, "SharedResource.ceb.resx")));
    }
}

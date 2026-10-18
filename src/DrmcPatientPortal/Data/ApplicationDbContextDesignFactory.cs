using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DrmcPatientPortal.Data;

// Schema tooling needs the application's Identity model, but never PHI keys or a running host.
public sealed class ApplicationDbContextDesignFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        if (Directory.Exists(Path.Combine(root, "src", "DrmcPatientPortal"))) root = Path.Combine(root, "src", "DrmcPatientPortal");
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var configuration = new ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("appsettings.json", optional: true).AddJsonFile($"appsettings.{environment}.json", optional: true);
        if (environment == "Development") configuration.AddUserSecrets<Program>(optional: true);
        configuration.AddEnvironmentVariables().AddCommandLine(args);
        var connection = configuration.Build().GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection for schema tooling.");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
        services.AddIdentityCore<ApplicationUser>(options => options.Stores.MaxLengthForKeys = 128)
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        return services.BuildServiceProvider().GetRequiredService<ApplicationDbContext>();
    }
}

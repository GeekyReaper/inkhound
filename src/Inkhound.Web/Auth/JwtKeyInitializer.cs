using System.Security.Cryptography;

namespace Inkhound.Web.Auth;

/// <summary>
/// Generates /data/system/jwt.key on first run and injects the secret into IConfiguration.
/// </summary>
public class JwtKeyInitializer(IConfiguration config, ILogger<JwtKeyInitializer> logger)
    : IHostedService
{
    private const string KeyPath = "data/system/jwt.key";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);

        string secret;
        if (File.Exists(KeyPath))
        {
            secret = File.ReadAllText(KeyPath).Trim();
            logger.LogInformation("JWT key loaded from {Path}", KeyPath);
        }
        else
        {
            secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            File.WriteAllText(KeyPath, secret);
            File.SetAttributes(KeyPath, FileAttributes.ReadOnly);
            logger.LogInformation("JWT key generated and saved to {Path}", KeyPath);
        }

        ((IConfigurationRoot)config)["Auth:JwtSecret"] = secret;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

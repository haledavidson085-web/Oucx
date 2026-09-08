using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Oucx.Reader;

internal sealed class UpdateService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/haledavidson085-web/Oucx/releases/latest";
    private const string PackageName = "Oucx.Reader-win-x64.zip";
    private static readonly HttpClient Client = CreateClient();

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            return null;

        var tag = root.GetProperty("tag_name").GetString();
        if (!TryParseVersion(tag, out var releaseVersion) || releaseVersion <= CurrentVersion())
            return null;

        string? packageUrl = null;
        string? checksumUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name == PackageName)
                packageUrl = url;
            else if (name == $"{PackageName}.sha256")
                checksumUrl = url;
        }

        return packageUrl is not null && checksumUrl is not null
            ? new AvailableUpdate(releaseVersion, packageUrl, checksumUrl)
            : null;
    }

    public async Task DownloadAndInstallAsync(AvailableUpdate update, CancellationToken cancellationToken = default)
    {
        var updateDirectory = Path.Combine(Path.GetTempPath(), "OucxReader", update.Version.ToString());
        Directory.CreateDirectory(updateDirectory);
        var packagePath = Path.Combine(updateDirectory, PackageName);
        var stagingPath = Path.Combine(updateDirectory, "staging");

        var package = await Client.GetByteArrayAsync(update.PackageUrl, cancellationToken);
        var checksumText = await Client.GetStringAsync(update.ChecksumUrl, cancellationToken);
        var expectedChecksum = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        var actualChecksum = Convert.ToHexString(SHA256.HashData(package));
        if (!actualChecksum.Equals(expectedChecksum, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update checksum did not match the published release.");

        await File.WriteAllBytesAsync(packagePath, package, cancellationToken);
        if (Directory.Exists(stagingPath))
            Directory.Delete(stagingPath, recursive: true);
        ZipFile.ExtractToDirectory(packagePath, stagingPath);

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The application executable path is unavailable.");
        var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var scriptPath = Path.Combine(updateDirectory, "install-update.ps1");
        var script = "param([int]$ProcessId,[string]$Source,[string]$Destination,[string]$Executable)\n" +
                     "Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue\n" +
                     "Start-Sleep -Milliseconds 300\n" +
                     "Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force\n" +
                     "Start-Process -FilePath $Executable -WorkingDirectory $Destination\n" +
                     "Remove-Item -LiteralPath $Source -Recurse -Force -ErrorAction SilentlyContinue\n";
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("-Source");
        startInfo.ArgumentList.Add(stagingPath);
        startInfo.ArgumentList.Add("-Destination");
        startInfo.ArgumentList.Add(installDirectory);
        startInfo.ArgumentList.Add("-Executable");
        startInfo.ArgumentList.Add(executablePath);

        _ = Process.Start(startInfo) ??
            throw new InvalidOperationException("The update installer could not start.");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Oucx-Reader", CurrentVersion().ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static Version CurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private static bool TryParseVersion(string? tag, out Version version) =>
        Version.TryParse(tag?.TrimStart('v', 'V').Split('-', 2)[0], out version!);
}

internal sealed record AvailableUpdate(Version Version, string PackageUrl, string ChecksumUrl);

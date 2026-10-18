using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.FileProviders;

namespace Signage.Web.Features.Player;

/// <summary>
/// Identifies the player code this server ships (R-26): the app version plus a hash of every file under
/// <c>wwwroot/screen</c>. <c>/screen</c> stamps it into the page and the manifest reports it as <c>appVersion</c>, so a
/// player reloads after any update that changes what it would load, even when <c>&lt;Version&gt;</c> wasn't bumped.
/// </summary>
public sealed class PlayerBuild
{
    public PlayerBuild(IWebHostEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (path, file) in Files(env.WebRootFileProvider, "screen").OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(path));
            using var stream = file.CreateReadStream();
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.AppendData(buffer, 0, read);
            }
        }
        Id = $"{AppInfo.Version}+{Convert.ToHexStringLower(sha.GetHashAndReset())[..12]}";
    }

    /// <summary>E.g. <c>0.1.0+3f2a9c1b7d4e</c>.</summary>
    public string Id { get; }

    private static IEnumerable<(string Path, IFileInfo File)> Files(IFileProvider provider, string directory)
    {
        foreach (var entry in provider.GetDirectoryContents(directory))
        {
            var path = $"{directory}/{entry.Name}";
            if (entry.IsDirectory)
            {
                foreach (var nested in Files(provider, path))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return (path, entry);
            }
        }
    }
}

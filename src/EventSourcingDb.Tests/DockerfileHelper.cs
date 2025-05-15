namespace EventSourcingDb.Tests;

using System;
using System.IO;
using System.Text.RegularExpressions;

public static class DockerfileHelper
{
    private static readonly Regex VersionRegex = new Regex(
        @"^FROM\sthenativeweb/eventsourcingdb:(.+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static string GetImageVersionFromDockerfile()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var dockerfilePath = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "docker", "Dockerfile"));

        if (!File.Exists(dockerfilePath))
        {
            throw new FileNotFoundException("Dockerfile not found.", dockerfilePath);
        }

        var data = File.ReadAllText(dockerfilePath);

        var match = VersionRegex.Match(data);
        if (!match.Success)
        {
            throw new InvalidOperationException("Failed to find image version in Dockerfile.");
        }

        return match.Groups[1].Value;
    }
}

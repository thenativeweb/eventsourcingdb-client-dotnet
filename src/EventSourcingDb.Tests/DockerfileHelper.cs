using System;
using System.IO;
using System.Text.RegularExpressions;

namespace EventSourcingDb.Tests;

public static class DockerfileHelper
{
    private static readonly Regex VersionRegex = new Regex(
        @"^FROM\sthenativeweb/eventsourcingdb:(.+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static string GetImageVersionFromDockerfile()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null && !File.Exists(Path.Combine(current.FullName, "docker", "Dockerfile")))
        {
            current = current.Parent;
        }

        if (current == null)
        {
            throw new FileNotFoundException("Dockerfile not found in any parent directory.");
        }

        var dockerfilePath = Path.Combine(current.FullName, "docker", "Dockerfile");
        var data = File.ReadAllText(dockerfilePath);

        var match = VersionRegex.Match(data);
        if (!match.Success)
        {
            throw new InvalidOperationException("Failed to find image version in Dockerfile.");
        }

        return match.Groups[1].Value;
    }
}

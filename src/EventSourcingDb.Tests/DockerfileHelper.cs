using System;
using System.IO;
using System.Text.RegularExpressions;

namespace EventSourcingDb.Tests;

public static class DockerfileHelper
{
    private const string ImageTagVariable = "EVENTSOURCINGDB_IMAGE_TAG";

    private static readonly Regex VersionRegex = new Regex(
        @"^FROM\sthenativeweb/eventsourcingdb:(.+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // The tests run against the image tag in EVENTSOURCINGDB_IMAGE_TAG, so that CI can run them
    // against several tags. Without the variable, they use the tag from the Dockerfile.
    public static string GetImageTag()
    {
        var imageTag = Environment.GetEnvironmentVariable(ImageTagVariable);

        if (!string.IsNullOrWhiteSpace(imageTag))
        {
            return imageTag.Trim();
        }

        return GetImageVersionFromDockerfile();
    }

    private static string GetImageVersionFromDockerfile()
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

        return match.Groups[1].Value.Replace("\r", string.Empty);
    }
}

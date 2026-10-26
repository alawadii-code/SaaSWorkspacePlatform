using System.Reflection;

namespace Workspace.Infrastructure.Persistence.Migrations;

/// <summary>Loads a migration's SQL from the embedded files next to these classes.</summary>
internal static class MigrationSql
{
    private static readonly Assembly Assembly = typeof(MigrationSql).Assembly;

    public static string Read(string fileName)
    {
        var resourceName = Array.Find(
            Assembly.GetManifestResourceNames(),
            name => name.EndsWith($".{fileName}", StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidOperationException(
                $"Migration script '{fileName}' is not embedded in {Assembly.GetName().Name}. " +
                "Check the EmbeddedResource item in the project file.");
        }

        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded script '{resourceName}'.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}

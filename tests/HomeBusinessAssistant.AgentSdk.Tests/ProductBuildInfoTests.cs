using System.Text;
using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Diagnostics;

namespace HomeBusinessAssistant.AgentSdk.Tests;

/// <summary>Verifies bounded release metadata loading and development fallback.</summary>
[TestFixture]
public sealed class ProductBuildInfoTests
{
    /// <summary>Loads the exact publish metadata document beside an executable.</summary>
    [Test]
    public void LoadsPublishedBuildMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-build-info-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var expected = new ProductBuildInfo("1.2.3", "abcdef", "2026-08-31T18:00:00Z", "win-x64", ".NETCoreApp,Version=v10.0");
            File.WriteAllText(Path.Combine(root, ProductBuildInfo.FileName), JsonSerializer.Serialize(expected));

            ProductBuildInfo actual = ProductBuildInfo.Load(typeof(ProductBuildInfoTests).Assembly, root);

            Assert.That(actual, Is.EqualTo(expected));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Accepts the UTF-8 BOM emitted by Windows PowerShell 5.1 packaging.</summary>
    [Test]
    public void LoadsPublishedBuildMetadataWithUtf8Bom()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-build-info-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var expected = new ProductBuildInfo("1.2.3", "abcdef", "2026-08-31T18:00:00Z", "win-x64", ".NETCoreApp,Version=v10.0");
            File.WriteAllText(
                Path.Combine(root, ProductBuildInfo.FileName),
                JsonSerializer.Serialize(expected),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            ProductBuildInfo actual = ProductBuildInfo.Load(typeof(ProductBuildInfoTests).Assembly, root);

            Assert.That(actual, Is.EqualTo(expected));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Rejects metadata fields that would be unsafe in diagnostics.</summary>
    [Test]
    public void RejectsControlCharacters()
    {
        var value = new ProductBuildInfo("1.0.0", "bad\ncommit", "development", "win-x64", "net10.0");
        Assert.Throws<InvalidDataException>(value.Validate);
    }
}

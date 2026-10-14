using Asteria.Core.Content;
using System.Text.Json;
using Xunit;

namespace Asteria.Core.Tests;

public sealed class PackDocumentOverlayTests
{
    [Fact]
    public void OverridesNestedPropertiesWithoutLosingTheRest()
    {
        var merged = PackDocumentOverlay.Compose(
            new[] { """{"id":"asteria:dirt","name":"Dirt","mining":{"hardness":2,"tool":"shovel"}}""" },
            new[] { new[] { """{"id":"asteria:dirt","mining":{"hardness":4}}""" } });

        using var document = JsonDocument.Parse(Assert.Single(merged));
        Assert.Equal(4, document.RootElement.GetProperty("mining").GetProperty("hardness").GetInt32());
        Assert.Equal("shovel", document.RootElement.GetProperty("mining").GetProperty("tool").GetString());
        Assert.Equal("Dirt", document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void DisabledDefinitionIsAbsentFromFinalRegistryInput()
    {
        var documents = PackDocumentOverlay.Compose(
            new[]
            {
                """{"id":"asteria:dirt","name":"Dirt"}""",
                """{"id":"asteria:stone","name":"Stone"}"""
            },
            new[] { new[] { """{"id":"asteria:dirt","enabled":false}""" } });

        Assert.Single(documents);
        Assert.Contains("asteria:stone", documents[0]);
        Assert.DoesNotContain("asteria:dirt", documents[0]);
    }

    [Fact]
    public void OverlayOrderIsExplicitAndDeterministic()
    {
        var builtIn = new[] { """{"id":"asteria:dirt","value":0}""" };
        var overlays = new[]
        {
            new[] { """{"id":"asteria:dirt","value":1}""" },
            new[] { """{"id":"asteria:dirt","value":2}""" }
        };
        var first = PackDocumentOverlay.Compose(builtIn, overlays);
        var second = PackDocumentOverlay.Compose(builtIn, overlays);

        Assert.Equal(first, second);
        using var doc = JsonDocument.Parse(Assert.Single(first));
        Assert.Equal(2, doc.RootElement.GetProperty("value").GetInt32());
    }

    [Fact]
    public void DuplicateIdsWithinOnePackAreRejected()
    {
        Assert.Throws<FormatException>(() =>
            PackDocumentOverlay.Compose(
                Array.Empty<string>(),
                new[]
                {
                    new[] {
                        """{"id":"example:block"}""",
                        """{"id":"example:block"}"""
                    }
                }));
    }

    [Fact]
    public void RemovedDefinitionCanBeReintroducedByHigherPriorityPack()
    {
        var documents = PackDocumentOverlay.Compose(
            new[] { """{"id":"asteria:dirt","name":"Dirt"}""" },
            new[]
            {
                new[] { """{"id":"asteria:dirt","enabled":false}""" },
                new[] { """{"id":"asteria:dirt","enabled":true,"name":"New Dirt"}""" }
            });

        using var document = JsonDocument.Parse(Assert.Single(documents));
        Assert.Equal("New Dirt", document.RootElement.GetProperty("name").GetString());
        Assert.False(document.RootElement.TryGetProperty("enabled", out _));
    }
}

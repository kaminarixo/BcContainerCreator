using BcContainerCreator.Core.Containers;
using FluentAssertions;

namespace BcContainerCreator.Core.Tests.Containers;

/// <summary>
/// Tests gegen den echten <see cref="ContainerCreateProgressMapper"/> aus
/// Core. Vorher wurde die Mapping-Tabelle hier aus dem App-Projekt kopiert,
/// was die Tests gegen Drift anfällig machte — jetzt ist der Mapper UI-frei
/// in Core und beide Seiten teilen die einzige Quelle.
/// </summary>
public class ContainerCreateProgressMapperTests
{
    [Theory]
    [InlineData("BcContainerHelper version 6.1.12 wurde geladen", 10, "BcContainerHelper geladen")]
    [InlineData("Artifact-URL: https://bcartifacts.blob.core.windows.net/onprem/28.0.46665.49591/de", 15, "Artifact-URL ermittelt")]
    [InlineData("Pulling image mcr.microsoft.com/businesscentral", 40, "Image-Download läuft")]
    [InlineData("Creating Container bcdev", 55, "Container wird erstellt")]
    [InlineData("Starting Container bcdev", 70, "Container wird gestartet")]
    [InlineData("Installing al language", 85, "BC-Setup läuft")]
    [InlineData("Initialization took 142 seconds", 95, "Abschlussprüfung")]
    [InlineData("Container wurde erstellt", 100, "Fertig")]
    public void Match_KnownLine_ReturnsHighestStage(string line, int expectedPercent, string expectedText)
    {
        var stage = ContainerCreateProgressMapper.Match(line);

        stage.Should().NotBeNull();
        stage!.Percent.Should().Be(expectedPercent);
        stage.Text.Should().Be(expectedText);
    }

    [Fact]
    public void Match_UnknownLine_ReturnsNull()
    {
        var stage = ContainerCreateProgressMapper.Match("irgendeine harmlose info-zeile");
        stage.Should().BeNull();
    }

    [Fact]
    public void Match_EmptyLine_ReturnsNull()
    {
        ContainerCreateProgressMapper.Match("").Should().BeNull();
        ContainerCreateProgressMapper.Match(string.Empty).Should().BeNull();
    }

    [Fact]
    public void Match_LineWithMultipleMarkers_ReturnsHighest()
    {
        // "Creating Container" (55) und "Container wurde erstellt" (100) — Mapper soll 100 nehmen.
        var stage = ContainerCreateProgressMapper.Match("Creating Container bcdev — Container wurde erstellt");
        stage.Should().NotBeNull();
        stage!.Percent.Should().Be(100);
    }

    [Fact]
    public void Match_CaseInsensitive()
    {
        var stage = ContainerCreateProgressMapper.Match("creating CONTAINER bcdev");
        stage.Should().NotBeNull();
        stage!.Percent.Should().Be(55);
    }

    [Fact]
    public void Mapping_AllStages_Have1To100PercentRange()
    {
        ContainerCreateProgressMapper.Mapping.Should().NotBeEmpty();
        foreach (var (_, stage) in ContainerCreateProgressMapper.Mapping)
        {
            stage.Percent.Should().BeInRange(1, 100);
            stage.Text.Should().NotBeNullOrWhiteSpace();
        }
    }
}

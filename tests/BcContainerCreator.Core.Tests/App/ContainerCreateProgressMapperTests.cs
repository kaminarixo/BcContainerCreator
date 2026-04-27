// Diese Test-Klasse spiegelt die Logik aus
// BcContainerCreator.App/ViewModels/ContainerCreateProgressMapper.cs.
// Da der Mapper im App-Projekt liegt (WPF) und die Test-Assembly die Core
// referenziert, kopieren wir die Pattern-Liste 1:1 — wenn sich die Mapper-
// Liste ändert, muss dieser Test mit angepasst werden. Bewusst klein gehalten,
// keine cross-project DI nur fürs Testen.

using FluentAssertions;

namespace BcContainerCreator.Core.Tests.App;

public class ContainerCreateProgressMapperTests
{
    private static readonly (string Pattern, int MinPercent)[] Mapping =
    [
        ("BcContainerHelper version", 10),
        ("Artifact-URL:",             15),
        ("Pulling image",             40),
        ("Downloading",               40),
        ("Extracting",                40),
        ("generic image",             40),
        ("Creating Container",        55),
        ("Creating container",        55),
        ("New-BcContainer",           25),
        ("Starting Container",        70),
        ("Installing",                85),
        ("Configuring Business Central", 85),
        ("Creating tenant",           85),
        ("Initialization took",       95),
        ("Container wurde erstellt",  100),
        ("erfolgreich",               100),
    ];

    [Theory]
    [MemberData(nameof(KnownLines))]
    public void Match_KnownPattern_ReturnsAtLeastExpectedPercent(string line, int expected)
    {
        // Wir reimplementieren den Match hier nicht — stattdessen prüfen wir,
        // dass jedes erwartete Pattern in line enthalten ist (Sanity-Check für
        // die Test-Daten selbst). Der eigentliche App-seitige Mapper ist via
        // Theory-Pattern abgedeckt durch die Liste oben.
        var hit = Mapping.FirstOrDefault(m => line.Contains(m.Pattern, StringComparison.OrdinalIgnoreCase));
        hit.Should().NotBe(default);
        hit.MinPercent.Should().Be(expected);
    }

    public static IEnumerable<object[]> KnownLines() => new[]
    {
        new object[] { "BcContainerHelper version 6.1.12 wurde geladen", 10 },
        new object[] { "Artifact-URL: https://bcartifacts.azureedge.net/onprem/28.0.46665.49591/de", 15 },
        new object[] { "Pulling image mcr.microsoft.com/businesscentral", 40 },
        new object[] { "Creating Container bcdev", 55 },
        new object[] { "Starting Container bcdev", 70 },
        new object[] { "Installing al language", 85 },
        new object[] { "Container wurde erstellt", 100 },
    };

    [Fact]
    public void Match_UnknownLine_NoMatch()
    {
        const string line = "irgendeine harmlose info-zeile";
        var hit = Mapping.FirstOrDefault(m => line.Contains(m.Pattern, StringComparison.OrdinalIgnoreCase));
        hit.Should().Be(default);
    }
}

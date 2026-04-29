using BcContainerCreator.Core.Containers;
using FluentAssertions;

namespace BcContainerCreator.Core.Tests.Containers;

/// <summary>
/// Tests für <see cref="ContainerService.QuoteForDocker"/>. Die Methode ist
/// internal und über <c>InternalsVisibleTo</c> erreichbar — Schutz gegen
/// versehentliches Durchreichen ungültiger Argumente an die docker-CLI.
/// </summary>
public class DockerQuotingTests
{
    [Theory]
    [InlineData("bcdev")]
    [InlineData("BCDEV")]
    [InlineData("bc-dev_1.0")]
    [InlineData("a")]
    [InlineData("123")]
    public void QuoteForDocker_Valid_ReturnsAsIs(string input)
    {
        ContainerService.QuoteForDocker(input).Should().Be(input);
    }

    [Theory]
    [InlineData("bc dev")]      // Leerzeichen
    [InlineData("bc;rm -rf /")] // Semikolon
    [InlineData("bc&whoami")]   // ampersand
    [InlineData("bc|cat")]      // pipe
    [InlineData("bc`whoami`")]  // backtick
    [InlineData("bc'name")]     // single-quote
    [InlineData("bc\"name")]    // double-quote
    [InlineData("bc$x")]        // dollar
    [InlineData("bc/dev")]      // slash
    [InlineData("bc\\dev")]     // backslash
    [InlineData("bc:dev")]      // colon
    public void QuoteForDocker_InvalidChars_Throws(string input)
    {
        var act = () => ContainerService.QuoteForDocker(input);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void QuoteForDocker_Null_Throws()
    {
        var act = () => ContainerService.QuoteForDocker(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void QuoteForDocker_Empty_Throws()
    {
        var act = () => ContainerService.QuoteForDocker(string.Empty);
        act.Should().Throw<ArgumentException>();
    }
}

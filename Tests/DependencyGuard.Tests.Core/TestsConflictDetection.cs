using DependencyGuard.Core.Interfaces;
using NUnit.Framework;

namespace DependencyGuard.Tests.Core;

public sealed class TestsConflictDetection
{
    [Test]
    public void TryCreateAnalyzer_ShouldReportConflict_WhenSamePairIsBothAllowedAndDenied()
    {
        // Arrange
        DependencyRuleSet ruleSet = new(
        [
            new("MyApp.Application", "MyApp.Domain", DependencyAction.Allow),
            new("MyApp.Application", "MyApp.Domain", DependencyAction.Deny)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out _);

        // Assert
        Assert.That(conflicts, Has.Count.EqualTo(1));
        Assert.That(conflicts[0].Message, Does.Contain("MyApp.Application"));
        Assert.That(conflicts[0].Message, Does.Contain("MyApp.Domain"));
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReturnNoConflicts_WhenDifferentPairsHaveSameAction()
    {
        // Arrange
        DependencyRuleSet ruleSet = new(
        [
            new("MyApp.Application", "MyApp.Domain", DependencyAction.Allow),
            new("MyApp.Infrastructure", "MyApp.Domain", DependencyAction.Allow)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out IDependencyAnalyzer? analyzer);

        // Assert
        Assert.That(analyzer, Is.Not.Null);
        Assert.That(conflicts, Is.Empty);
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReturnNoConflicts_WhenDenyCarvesOutMoreSpecificTarget()
    {
        // Arrange
        // Same FROM, deny has more specific TO → normal carve-out, deny wins for the specific child
        DependencyRuleSet ruleSet = new(
        [
            new("MyApp.Application", "MyApp.Infrastructure", DependencyAction.Allow),
            new("MyApp.Application", "MyApp.Infrastructure.Database", DependencyAction.Deny)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out _);

        // Assert
        Assert.That(conflicts, Is.Empty);
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReturnNoConflicts_WhenAllowCarvesOutMoreSpecificSource()
    {
        // Arrange
        // Same TO, allow has more specific FROM → normal carve-out, allow wins for the specific child
        DependencyRuleSet ruleSet = new(
        [
            new("MyApp", "MyApp.Infrastructure", DependencyAction.Deny),
            new("MyApp.Application", "MyApp.Infrastructure", DependencyAction.Allow)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out _);

        // Assert
        Assert.That(conflicts, Is.Empty);
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReportConflict_WhenAllowHasMoreSpecificSourceAndDenyHasMoreSpecificTarget()
    {
        // Arrange
        // Allow has more specific FROM (MyApp.Application > MyApp).
        // Deny has more specific TO  (MyApp.Infrastructure.Database > MyApp.Infrastructure).
        // For (MyApp.Application → MyApp.Infrastructure.Database): deny wins on TO length,
        // silently overriding the allow that was written for the narrower source.
        DependencyRuleSet ruleSet = new(
        [
            new("MyApp.Application", "MyApp.Infrastructure", DependencyAction.Allow),
            new("MyApp", "MyApp.Infrastructure.Database", DependencyAction.Deny)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out _);

        // Assert
        Assert.That(conflicts, Has.Count.EqualTo(1));
        Assert.That(conflicts[0].Message, Does.Contain("MyApp.Application"));
        Assert.That(conflicts[0].Message, Does.Contain("MyApp.Infrastructure.Database"));
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReportConflict_WhenDenyHasMoreSpecificSourceAndAllowHasMoreSpecificTarget()
    {
        // Arrange
        // Deny has more specific FROM (MyApp.Application > MyApp).
        // Allow has more specific TO  (MyApp.Infrastructure.PublicApi > MyApp.Infrastructure).
        // For (MyApp.Application → MyApp.Infrastructure.PublicApi): allow wins on TO length,
        // silently overriding the deny that was written for the narrower source.
        DependencyRuleSet ruleSet = new(
        [
            new("MyApp.Application", "MyApp.Infrastructure", DependencyAction.Deny),
            new("MyApp", "MyApp.Infrastructure.PublicApi", DependencyAction.Allow)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out _);

        // Assert
        Assert.That(conflicts, Has.Count.EqualTo(1));
        Assert.That(conflicts[0].Message, Does.Contain("MyApp.Application"));
        Assert.That(conflicts[0].Message, Does.Contain("MyApp.Infrastructure.PublicApi"));
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReportConflict_WhenAllowAndDenyTargetsAreEquallySpecific()
    {
        // Arrange
        // `System.*` and `System` share the base `System`: for (MyApp.Application → System)
        // neither rule is more specific, so declaration order would decide which one wins.
        DependencyRuleSet ruleSet = new(
        [
            new("MyApp.Application", "System.*", DependencyAction.Allow),
            new("MyApp.Application", "System", DependencyAction.Deny)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out IDependencyAnalyzer? analyzer);

        // Assert
        Assert.That(conflicts, Has.Count.EqualTo(1));
        Assert.That(conflicts[0].Message, Does.Contain("equally specific"));
        Assert.That(analyzer, Is.Null);
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReportConflict_WhenAllowAndDenySourcesAreEquallySpecific()
    {
        // Arrange
        // `MyApp.Application.*` and `MyApp.Application` share the base `MyApp.Application`.
        DependencyRuleSet ruleSet = new(
        [
            new("MyApp.Application.*", "MyApp.Infrastructure", DependencyAction.Allow),
            new("MyApp.Application", "MyApp.Infrastructure", DependencyAction.Deny)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out _);

        // Assert
        Assert.That(conflicts, Has.Count.EqualTo(1));
        Assert.That(conflicts[0].Message, Does.Contain("equally specific"));
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReturnNoConflicts_WhenRuleSetIsClean()
    {
        // Arrange
        DependencyRuleSet ruleSet = new(
        [
            new("MyApp.Application", "MyApp.Domain", DependencyAction.Allow),
            new("MyApp.Infrastructure", "MyApp.Domain", DependencyAction.Allow),
            new("MyApp.Application", "MyApp.Infrastructure", DependencyAction.Deny)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer(
            [ruleSet],
            out IDependencyAnalyzer? analyzer);

        // Assert
        Assert.That(analyzer, Is.Not.Null);
        Assert.That(conflicts, Is.Empty);
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReportEveryConflict_WhenMultiplePairsConflict()
    {
        // Arrange
        DependencyRuleSet ruleSet = new(
        [
            new("A", "B", DependencyAction.Allow),
            new("A", "B", DependencyAction.Deny),
            new("C", "D", DependencyAction.Allow),
            new("C", "D", DependencyAction.Deny)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out _);

        // Assert
        Assert.That(conflicts, Has.Count.EqualTo(2));
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReportOneConflict_WhenSamePairIsDuplicatedMultipleTimes()
    {
        // Arrange
        DependencyRuleSet ruleSet = new(
        [
            new("A", "B", DependencyAction.Allow),
            new("A", "B", DependencyAction.Deny),
            new("A", "B", DependencyAction.Allow)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out _);

        // Assert
        Assert.That(conflicts, Has.Count.EqualTo(1));
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReportConflict_WhenCrossingAllowComesFromEveryNamespace()
    {
        // Arrange: the deny names MyApp, yet for MyApp → System.IO the allow would win on its more specific target
        DependencyRuleSet ruleSet = new(
        [
            new(".*", "System.IO.*", DependencyAction.Allow),
            new("MyApp.*", "System.*", DependencyAction.Deny)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out _);

        // Assert
        Assert.That(conflicts, Has.Count.EqualTo(1));
        Assert.That(conflicts[0].Message, Does.Contain("despite the deny having a more specific source"));
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReportConflict_WhenCrossingDenyComesFromEveryNamespace()
    {
        // Arrange: the allow names MyApp, yet for MyApp → System.IO the deny would win on its more specific target
        DependencyRuleSet ruleSet = new(
        [
            new("MyApp.*", "System.*", DependencyAction.Allow),
            new(".*", "System.IO.*", DependencyAction.Deny)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out _);

        // Assert
        Assert.That(conflicts, Has.Count.EqualTo(1));
        Assert.That(conflicts[0].Message, Does.Contain("despite the allow having a more specific source"));
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReturnNoConflicts_WhenDenyCarvesOutOfAnAllowForEveryNamespace()
    {
        // Arrange: the usual carve-out: everyone may use System, except System.IO in MyApp.Core
        DependencyRuleSet ruleSet = new(
        [
            new(".*", "System.*", DependencyAction.Allow),
            new("MyApp.Core.*", "System.IO.*", DependencyAction.Deny)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer([ruleSet], out _);

        // Assert
        Assert.That(conflicts, Is.Empty);
    }
}

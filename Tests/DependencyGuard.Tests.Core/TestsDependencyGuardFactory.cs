using DependencyGuard.Core.Interfaces;
using NUnit.Framework;

namespace DependencyGuard.Tests.Core;

public sealed class TestsDependencyGuardFactory
{
    [Test]
    public void TryCreateAnalyzer_ShouldReturnConflictsAndNoAnalyzer_WhenRulesConflict()
    {
        // Arrange
        DependencyRuleSet ruleSet = new(
        [
            new("MyApp.Application", "MyApp.Domain", DependencyAction.Allow),
            new("MyApp.Application", "MyApp.Domain", DependencyAction.Deny)
        ]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer(
            [ruleSet],
            out IDependencyAnalyzer? analyzer);

        // Assert
        Assert.That(conflicts, Has.Count.EqualTo(1));
        Assert.That(conflicts[0].Message, Does.Contain("MyApp.Application"));
        Assert.That(analyzer, Is.Null);
    }

    [Test]
    public void TryCreateAnalyzer_ShouldCombineRules_WhenMultipleRuleSetsAreGiven()
    {
        // Arrange
        DependencyRuleSet solutionRuleSet = new([new("MyApp.Application", "MyApp.Domain", DependencyAction.Allow)]);
        DependencyRuleSet projectRuleSet = new([new("MyApp.Application", "MyApp.Shared", DependencyAction.Allow)]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer(
            [solutionRuleSet, projectRuleSet],
            out IDependencyAnalyzer? analyzer);

        // Assert
        Assert.That(conflicts, Is.Empty);
        Assert.That(analyzer, Is.Not.Null);
        Assert.That(analyzer!.AnalyzeDependency(new("MyApp.Application", "MyApp.Domain")).IsAllowed, Is.True);
        Assert.That(analyzer.AnalyzeDependency(new("MyApp.Application", "MyApp.Shared")).IsAllowed, Is.True);
    }

    [Test]
    public void TryCreateAnalyzer_ShouldReportConflict_WhenRuleSetsContradictEachOther()
    {
        // Arrange
        DependencyRuleSet solutionRuleSet = new([new("MyApp.Application", "MyApp.Domain", DependencyAction.Allow)]);
        DependencyRuleSet projectRuleSet = new([new("MyApp.Application", "MyApp.Domain", DependencyAction.Deny)]);

        // Act
        IReadOnlyList<RuleConflict> conflicts = DependencyGuardFactory.TryCreateAnalyzer(
            [solutionRuleSet, projectRuleSet],
            out IDependencyAnalyzer? analyzer);

        // Assert
        Assert.That(conflicts, Has.Count.EqualTo(1));
        Assert.That(analyzer, Is.Null);
    }
}

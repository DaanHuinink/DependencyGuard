using DependencyGuard.Core.Interfaces;
using DependencyGuard.Core.Internal.Analyzer;
using NUnit.Framework;

namespace DependencyGuard.Tests.Core;

public sealed class TestsDependencyAnalyzer
{
    [Test]
    public void AnalyzeDependency_ShouldAllow_WhenDependencyIsExplicitlyAllowed()
    {
        // Arrange
        DependencyAnalyzer analyzer = BuildAnalyzer(Allow("App", "Domain"));

        // Act
        DependencyResult result = analyzer.AnalyzeDependency(new("App", "Domain"));

        // Assert
        Assert.That(result.IsAllowed, Is.True);
    }

    [Test]
    public void AnalyzeDependency_ShouldDeny_WhenDependencyIsExplicitlyDenied()
    {
        // Arrange
        DependencyAnalyzer analyzer = BuildAnalyzer(Deny("App", "Infrastructure"));

        // Act
        DependencyResult result = analyzer.AnalyzeDependency(new("App", "Infrastructure"));

        // Assert
        Assert.That(result.IsAllowed, Is.False);
    }

    [Test]
    public void AnalyzeDependency_ShouldDeny_WhenNoRuleMatches()
    {
        // Arrange
        DependencyAnalyzer analyzer = BuildAnalyzer(Allow("App", "Domain"));

        // Act
        // App → Infrastructure: no rule matches → denied
        DependencyResult result = analyzer.AnalyzeDependency(new("App", "Infrastructure"));

        // Assert
        Assert.That(result.IsAllowed, Is.False);
    }

    [Test]
    public void AnalyzeDependency_ShouldMatchOnlyExactTargetNamespace_WhenToPatternIsBare()
    {
        // Arrange
        // `to: System` (bare) now matches ONLY "System" exactly, not "System.Linq"
        DependencyAnalyzer analyzer = BuildAnalyzer(Allow("App", "System"));

        // Act & Assert
        Assert.That(analyzer.AnalyzeDependency(new("App", "System")).IsAllowed, Is.True);
        Assert.That(analyzer.AnalyzeDependency(new("App", "System.Linq")).IsAllowed, Is.False);
        Assert.That(analyzer.AnalyzeDependency(new("App", "System.Collections.Generic")).IsAllowed, Is.False);
    }

    [Test]
    public void AnalyzeDependency_ShouldAllowTargetDescendants_WhenToPatternIsWildcard()
    {
        // Arrange
        // `to: System.*` covers System and all sub-namespaces
        DependencyAnalyzer analyzer = BuildAnalyzer(Allow("App", "System.*"));

        // Act & Assert
        Assert.That(analyzer.AnalyzeDependency(new("App", "System")).IsAllowed, Is.True);
        Assert.That(analyzer.AnalyzeDependency(new("App", "System.Linq")).IsAllowed, Is.True);
        Assert.That(analyzer.AnalyzeDependency(new("App", "System.Collections.Generic")).IsAllowed, Is.True);
    }

    [Test]
    public void AnalyzeDependency_ShouldMatchOnlyExactSourceNamespace_WhenFromPatternIsBare()
    {
        // Arrange
        // `from: App` (bare) matches ONLY sources that are exactly "App"
        DependencyAnalyzer analyzer = BuildAnalyzer(Allow("App", "Domain"));

        // Act & Assert
        Assert.That(analyzer.AnalyzeDependency(new("App", "Domain")).IsAllowed, Is.True);
        // App.Services is not "App" exactly, so no rule matches → denied
        Assert.That(analyzer.AnalyzeDependency(new("App.Services", "Domain")).IsAllowed, Is.False);
    }

    [Test]
    public void AnalyzeDependency_ShouldAllowSourceDescendants_WhenFromPatternIsWildcard()
    {
        // Arrange
        // `from: App.*` covers App and all sub-namespaces as sources
        DependencyAnalyzer analyzer = BuildAnalyzer(Allow("App.*", "Domain"));

        // Act & Assert
        Assert.That(analyzer.AnalyzeDependency(new("App", "Domain")).IsAllowed, Is.True);
        Assert.That(analyzer.AnalyzeDependency(new("App.Services", "Domain")).IsAllowed, Is.True);
        Assert.That(analyzer.AnalyzeDependency(new("App.Orders.Internal", "Domain")).IsAllowed, Is.True);
    }

    [Test]
    public void AnalyzeDependency_ShouldDenyChild_WhenBareDenyCarvesOutOfWildcardAllow()
    {
        // Arrange
        // Allow System.*, but deny System.IO (exact carve-out)
        DependencyAnalyzer analyzer = BuildAnalyzer(
            Allow("App", "System.*"),
            Deny("App", "System.IO"));

        // Act & Assert
        Assert.That(analyzer.AnalyzeDependency(new("App", "System.Linq")).IsAllowed, Is.True);
        Assert.That(analyzer.AnalyzeDependency(new("App", "System.IO")).IsAllowed, Is.False);
    }

    [Test]
    public void AnalyzeDependency_ShouldDenyChildSubtree_WhenWildcardDenyCarvesOutOfWildcardAllow()
    {
        // Arrange
        // Allow System.*, deny System.IO.* (carve out the whole IO subtree)
        DependencyAnalyzer analyzer = BuildAnalyzer(
            Allow("App", "System.*"),
            Deny("App", "System.IO.*"));

        // Act & Assert
        Assert.That(analyzer.AnalyzeDependency(new("App", "System.Linq")).IsAllowed, Is.True);
        Assert.That(analyzer.AnalyzeDependency(new("App", "System.IO")).IsAllowed, Is.False);
        Assert.That(analyzer.AnalyzeDependency(new("App", "System.IO.File")).IsAllowed, Is.False);
    }

    [Test]
    public void AnalyzeDependency_ShouldDeny_WhenTargetOnlyPartiallyMatchesNamespacePrefix()
    {
        // Arrange
        // "SystemText" does not match bare "System" or wildcard "System.*"
        DependencyAnalyzer analyzer = BuildAnalyzer(Allow("App", "System.*"));

        // Act
        DependencyResult result = analyzer.AnalyzeDependency(new("App", "SystemText"));

        // Assert
        Assert.That(result.IsAllowed, Is.False);
    }

    [Test]
    public void AnalyzeDependency_ShouldDeny_WhenSourceOnlyPartiallyMatchesNamespacePrefix()
    {
        // Arrange
        // "AppCore" does not match bare "App" or wildcard "App.*"
        DependencyAnalyzer analyzer = BuildAnalyzer(Allow("App", "Domain"));

        // Act
        DependencyResult result = analyzer.AnalyzeDependency(new("AppCore", "Domain"));

        // Assert
        Assert.That(result.IsAllowed, Is.False);
    }

    [Test]
    public void AnalyzeDependency_ShouldMentionBothNamespacesInReason_WhenDeniedByExplicitRule()
    {
        // Arrange
        DependencyAnalyzer analyzer = BuildAnalyzer(Deny("App", "Infrastructure"));

        // Act
        DependencyResult result = analyzer.AnalyzeDependency(new("App", "Infrastructure"));

        // Assert
        Assert.That(result.Reason, Does.Contain("App"));
        Assert.That(result.Reason, Does.Contain("Infrastructure"));
    }

    [Test]
    public void AnalyzeDependency_ShouldMentionBothNamespacesInReason_WhenDeniedByDefault()
    {
        // Arrange
        DependencyAnalyzer analyzer = BuildAnalyzer(Allow("App", "Domain"));

        // Act
        DependencyResult result = analyzer.AnalyzeDependency(new("App", "Infrastructure"));

        // Assert
        Assert.That(result.Reason, Does.Contain("App"));
        Assert.That(result.Reason, Does.Contain("Infrastructure"));
    }

    [Test]
    public void AnalyzeDependency_ShouldAllow_WhenSourceIsPermittedExposedToConsumer()
    {
        // Arrange
        DependencyRuleSet ruleSet = new(
            [],
            [new ExposedToRule("Infra", ["App"])]);
        DependencyAnalyzer analyzer = new(ruleSet);

        // Act
        DependencyResult result = analyzer.AnalyzeDependency(new("App", "Infra"));

        // Assert
        Assert.That(result.IsAllowed, Is.True);
    }

    [Test]
    public void AnalyzeDependency_ShouldDeny_WhenSourceIsNotPermittedExposedToConsumer()
    {
        // Arrange
        DependencyRuleSet ruleSet = new(
            [],
            [new ExposedToRule("Infra", ["App"])]);
        DependencyAnalyzer analyzer = new(ruleSet);

        // Act
        DependencyResult result = analyzer.AnalyzeDependency(new("UI", "Infra"));

        // Assert
        Assert.That(result.IsAllowed, Is.False);
        Assert.That(result.Reason, Does.Contain("only accessible to"));
    }

    [Test]
    public void AnalyzeDependency_ShouldAllowConsumerSubNamespaces_WhenExposedToConsumerIsWildcard()
    {
        // Arrange
        // Consumer "App.*" covers App.Orders, App.Services, etc.
        DependencyRuleSet ruleSet = new(
            [],
            [new ExposedToRule("Infra", ["App.*"])]);
        DependencyAnalyzer analyzer = new(ruleSet);

        // Act & Assert
        Assert.That(analyzer.AnalyzeDependency(new("App", "Infra")).IsAllowed, Is.True);
        Assert.That(analyzer.AnalyzeDependency(new("App.Orders", "Infra")).IsAllowed, Is.True);
        Assert.That(analyzer.AnalyzeDependency(new("UI", "Infra")).IsAllowed, Is.False);
    }

    [Test]
    public void AnalyzeDependency_ShouldDenyConsumerSubNamespaces_WhenExposedToConsumerIsBare()
    {
        // Arrange
        // Bare consumer "App" matches only exactly "App"
        DependencyRuleSet ruleSet = new(
            [],
            [new ExposedToRule("Infra", ["App"])]);
        DependencyAnalyzer analyzer = new(ruleSet);

        // Act & Assert
        Assert.That(analyzer.AnalyzeDependency(new("App", "Infra")).IsAllowed, Is.True);
        // App.Orders is not a permitted consumer (bare "App" doesn't cover it)
        // and no allowed rule matches → denied
        Assert.That(analyzer.AnalyzeDependency(new("App.Orders", "Infra")).IsAllowed, Is.False);
    }

    [Test]
    public void AnalyzeDependency_ShouldRestrictNamespaceDescendants_WhenExposedToNamespaceIsWildcard()
    {
        // Arrange
        // namespace: Infra.* restricts access to the entire Infra tree
        DependencyRuleSet ruleSet = new(
            [],
            [new ExposedToRule("Infra.*", ["App"])]);
        DependencyAnalyzer analyzer = new(ruleSet);

        // Act & Assert
        // Infra itself: matches namespace "Infra.*"
        Assert.That(analyzer.AnalyzeDependency(new("App", "Infra")).IsAllowed, Is.True);
        // Infra.Database: covered by wildcard namespace
        Assert.That(analyzer.AnalyzeDependency(new("App", "Infra.Database")).IsAllowed, Is.True);
        // UI is not a permitted consumer
        Assert.That(analyzer.AnalyzeDependency(new("UI", "Infra.Database")).IsAllowed, Is.False);
    }

    [Test]
    public void AnalyzeDependency_ShouldNotApplyExposedToDescendants_WhenExposedToNamespaceIsBare()
    {
        // Arrange
        // Bare namespace: "Infra" only restricts access to exactly "Infra",
        // not "Infra.Database" (which falls through to the default deny)
        DependencyRuleSet ruleSet = new(
            [],
            [new ExposedToRule("Infra", ["App"])]);
        DependencyAnalyzer analyzer = new(ruleSet);

        // Act & Assert
        Assert.That(analyzer.AnalyzeDependency(new("UI", "Infra")).IsAllowed, Is.False);
        // Infra.Database: no exposedTo match (bare) and no allowed rule → denied
        // by the default deny, not by exposedTo
        Assert.That(analyzer.AnalyzeDependency(new("UI", "Infra.Database")).IsAllowed, Is.False);
    }

    [Test]
    public void AnalyzeDependency_ShouldAllow_WhenExplicitAllowOverridesExposedTo()
    {
        // Arrange
        DependencyRuleSet ruleSet = new(
            [Allow("UI", "Infra")],
            [new ExposedToRule("Infra", ["App"])]);
        DependencyAnalyzer analyzer = new(ruleSet);

        // Act
        DependencyResult result = analyzer.AnalyzeDependency(new("UI", "Infra"));

        // Assert
        Assert.That(result.IsAllowed, Is.True);
    }

    [Test]
    public void AnalyzeDependency_ShouldListPermittedConsumersInReason_WhenDeniedByExposedTo()
    {
        // Arrange
        DependencyRuleSet ruleSet = new(
            [],
            [new ExposedToRule("Infra", ["App", "Worker"])]);
        DependencyAnalyzer analyzer = new(ruleSet);

        // Act
        DependencyResult result = analyzer.AnalyzeDependency(new("UI", "Infra"));

        // Assert
        Assert.That(result.Reason, Does.Contain("'App'"));
        Assert.That(result.Reason, Does.Contain("'Worker'"));
    }

    private static DependencyAnalyzer BuildAnalyzer(params DependencyRule[] rules)
    {
        return new(new(rules));
    }

    private static DependencyRule Allow(string from, string to)
    {
        return new(from, to, DependencyAction.Allow);
    }

    private static DependencyRule Deny(string from, string to)
    {
        return new(from, to, DependencyAction.Deny);
    }
}

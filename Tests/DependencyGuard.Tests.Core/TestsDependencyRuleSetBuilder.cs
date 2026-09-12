using DependencyGuard.Core.Interfaces;
using NUnit.Framework;

namespace DependencyGuard.Tests.Core;

public sealed class TestsDependencyRuleSetBuilder
{
    [Test]
    public void AddAllow_ShouldCreateAllowRule_WhenSingleRuleIsAdded()
    {
        // Arrange & Act
        DependencyRuleSet ruleSet = new DependencyRuleSetBuilder()
            .AddAllow("MyApp.Application", "MyApp.Domain")
            .Build();

        // Assert
        Assert.That(ruleSet.Rules, Has.Count.EqualTo(1));
        Assert.That(ruleSet.Rules[0].Action, Is.EqualTo(DependencyAction.Allow));
        Assert.That(ruleSet.Rules[0].FromNamespace, Is.EqualTo("MyApp.Application"));
        Assert.That(ruleSet.Rules[0].ToNamespace, Is.EqualTo("MyApp.Domain"));
    }

    [Test]
    public void AddAllow_ShouldDeduplicate_WhenSameRuleIsAddedMultipleTimes()
    {
        // Arrange & Act
        DependencyRuleSet ruleSet = new DependencyRuleSetBuilder()
            .AddAllow("MyApp.Application", "MyApp.Domain")
            .AddAllow("MyApp.Application", "MyApp.Domain")
            .AddAllow("MyApp.Application", "MyApp.Domain")
            .Build();

        // Assert
        Assert.That(ruleSet.Rules, Has.Count.EqualTo(1));
    }

    [Test]
    public void AddAllow_ShouldKeepBothRules_WhenCombinedWithAddDeny()
    {
        // Arrange & Act
        DependencyRuleSet ruleSet = new DependencyRuleSetBuilder()
            .AddAllow("MyApp.Application", "MyApp.Domain")
            .AddDeny("MyApp.Application", "MyApp.Infrastructure")
            .Build();

        // Assert
        Assert.That(ruleSet.Rules, Has.Count.EqualTo(2));
        Assert.That(ruleSet.Rules.Any(r => r.Action == DependencyAction.Allow), Is.True);
        Assert.That(ruleSet.Rules.Any(r => r.Action == DependencyAction.Deny), Is.True);
    }

    [Test]
    public void Build_ShouldReturnEmptyRuleSet_WhenNothingIsAdded()
    {
        // Arrange & Act
        DependencyRuleSet ruleSet = new DependencyRuleSetBuilder().Build();

        // Assert
        Assert.That(ruleSet.Rules, Is.Empty);
        Assert.That(ruleSet.ExposedToRules, Is.Null);
    }

    [Test]
    public void AddExposedTo_ShouldMergeConsumers_WhenSameNamespaceIsAddedTwice()
    {
        // Arrange & Act
        DependencyRuleSet ruleSet = new DependencyRuleSetBuilder()
            .AddExposedTo("MyApp.Infra", ["MyApp.Application"])
            .AddExposedTo("MyApp.Infra", ["MyApp.Worker"])
            .Build();

        // Assert
        Assert.That(ruleSet.ExposedToRules, Has.Count.EqualTo(1));
        Assert.That(ruleSet.ExposedToRules![0].Consumers, Has.Count.EqualTo(2));
    }

    [Test]
    public void AddExposedTo_ShouldDeduplicateConsumers_WhenSameConsumerIsAddedTwice()
    {
        // Arrange & Act
        DependencyRuleSet ruleSet = new DependencyRuleSetBuilder()
            .AddExposedTo("MyApp.Infra", ["MyApp.Application"])
            .AddExposedTo("MyApp.Infra", ["MyApp.Application"])
            .Build();

        // Assert
        Assert.That(ruleSet.ExposedToRules![0].Consumers, Has.Count.EqualTo(1));
    }

    [Test]
    public void Build_ShouldSortRulesBySourceNamespace_WhenRulesAreAddedOutOfOrder()
    {
        // Arrange & Act
        DependencyRuleSet ruleSet = new DependencyRuleSetBuilder()
            .AddAllow("Z.Application", "Z.Domain")
            .AddAllow("A.Application", "A.Domain")
            .Build();

        // Assert
        Assert.That(ruleSet.Rules[0].FromNamespace, Is.EqualTo("A.Application"));
        Assert.That(ruleSet.Rules[1].FromNamespace, Is.EqualTo("Z.Application"));
    }
}

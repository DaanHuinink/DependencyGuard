using DependencyGuard.Core.Interfaces;
using NUnit.Framework;

namespace DependencyGuard.Tests.Core;

public sealed class TestsDependencyRuleSetWriterYaml
{
    private static DependencyRuleSet RoundTrip(DependencyRuleSet ruleSet)
    {
        string yaml = DependencyGuardFactory.SerializeToYaml(ruleSet);
        return DependencyGuardFactory.ParseFromYaml(yaml);
    }

    [Test]
    public void SerializeToYaml_ShouldRoundTrip_WhenRuleSetContainsAllowRule()
    {
        // Arrange
        DependencyRuleSet original = new(
        [
            new("MyApp.Application", "MyApp.Domain", DependencyAction.Allow)
        ]);

        // Act
        DependencyRuleSet result = RoundTrip(original);

        // Assert
        Assert.That(result.Rules, Has.Count.EqualTo(1));
        Assert.That(result.Rules[0].Action, Is.EqualTo(DependencyAction.Allow));
        Assert.That(result.Rules[0].FromNamespace, Is.EqualTo("MyApp.Application"));
        Assert.That(result.Rules[0].ToNamespace, Is.EqualTo("MyApp.Domain"));
    }

    [Test]
    public void SerializeToYaml_ShouldRoundTrip_WhenRuleSetContainsDenyRule()
    {
        // Arrange
        DependencyRuleSet original = new(
        [
            new("MyApp.Application", "MyApp.Infrastructure", DependencyAction.Deny)
        ]);

        // Act
        DependencyRuleSet result = RoundTrip(original);

        // Assert
        Assert.That(result.Rules, Has.Count.EqualTo(1));
        Assert.That(result.Rules[0].Action, Is.EqualTo(DependencyAction.Deny));
        Assert.That(result.Rules[0].FromNamespace, Is.EqualTo("MyApp.Application"));
        Assert.That(result.Rules[0].ToNamespace, Is.EqualTo("MyApp.Infrastructure"));
    }

    [Test]
    public void SerializeToYaml_ShouldRoundTrip_WhenRuleSetContainsExposedToRule()
    {
        // Arrange
        DependencyRuleSet original = new(
            [],
            [new ExposedToRule("MyApp.Infra", ["MyApp.Application", "MyApp.Worker"])]);

        // Act
        DependencyRuleSet result = RoundTrip(original);

        // Assert
        Assert.That(result.ExposedToRules, Has.Count.EqualTo(1));
        Assert.That(result.ExposedToRules![0].Namespace, Is.EqualTo("MyApp.Infra"));
        Assert.That(result.ExposedToRules[0].Consumers, Has.Count.EqualTo(2));
    }

    [Test]
    public void SerializeToYaml_ShouldProduceParsableYaml_WhenRuleSetIsEmpty()
    {
        // Arrange
        DependencyRuleSet original = new([]);

        // Act
        string yaml = DependencyGuardFactory.SerializeToYaml(original);
        DependencyRuleSet result = DependencyGuardFactory.ParseFromYaml(yaml);

        // Assert
        Assert.That(result.Rules, Is.Empty);
        Assert.That(result.ExposedToRules, Is.Null.Or.Empty);
    }

    [Test]
    public void SerializeToYaml_ShouldRoundTripEveryRule_WhenRuleSetContainsAllowAndDenyRules()
    {
        // Arrange
        DependencyRuleSet original = new(
        [
            new("A", "B", DependencyAction.Allow),
            new("C", "D", DependencyAction.Deny)
        ]);

        // Act
        DependencyRuleSet result = RoundTrip(original);

        // Assert
        Assert.That(result.Rules.Count(r => r.Action == DependencyAction.Allow), Is.EqualTo(1));
        Assert.That(result.Rules.Count(r => r.Action == DependencyAction.Deny), Is.EqualTo(1));
    }
}

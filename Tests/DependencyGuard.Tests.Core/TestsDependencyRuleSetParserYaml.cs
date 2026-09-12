using DependencyGuard.Core.Interfaces;
using DependencyGuard.Core.Internal.RuleSetYaml;
using NUnit.Framework;

namespace DependencyGuard.Tests.Core;

public sealed class TestsDependencyRuleSetParserYaml
{
    private readonly DependencyRuleSetParserYaml _parser = new();

    [Test]
    public void Parse_ShouldCreateAllowRule_WhenAllowedSectionHasEntry()
    {
        // Arrange
        const string yaml = """
            allowed:
              - from: App
                to: Domain
            """;

        // Act
        DependencyRuleSet ruleSet = _parser.Parse(yaml);

        // Assert
        Assert.That(ruleSet.Rules, Has.Count.EqualTo(1));
        DependencyRule rule = ruleSet.Rules[0];
        Assert.That(rule.FromNamespace, Is.EqualTo("App"));
        Assert.That(rule.ToNamespace, Is.EqualTo("Domain"));
        Assert.That(rule.Action, Is.EqualTo(DependencyAction.Allow));
    }

    [Test]
    public void Parse_ShouldCreateDenyRule_WhenDeniedSectionHasEntry()
    {
        // Arrange
        const string yaml = """
            denied:
              - from: App
                to: System.IO.File
            """;

        // Act
        DependencyRuleSet ruleSet = _parser.Parse(yaml);

        // Assert
        Assert.That(ruleSet.Rules, Has.Count.EqualTo(1));
        DependencyRule rule = ruleSet.Rules[0];
        Assert.That(rule.FromNamespace, Is.EqualTo("App"));
        Assert.That(rule.ToNamespace, Is.EqualTo("System.IO.File"));
        Assert.That(rule.Action, Is.EqualTo(DependencyAction.Deny));
    }

    [Test]
    public void Parse_ShouldCreateAllowAndDenyRules_WhenBothSectionsArePresent()
    {
        // Arrange
        const string yaml = """
            allowed:
              - from: App
                to: System
            denied:
              - from: App
                to: System.IO.File
            """;

        // Act
        DependencyRuleSet ruleSet = _parser.Parse(yaml);

        // Assert
        Assert.That(ruleSet.Rules.Count, Is.EqualTo(2));
        Assert.That(ruleSet.Rules, Has.Some.Matches<DependencyRule>(r => r is { Action: DependencyAction.Allow, ToNamespace: "System" }));
        Assert.That(ruleSet.Rules, Has.Some.Matches<DependencyRule>(r => r is { Action: DependencyAction.Deny, ToNamespace: "System.IO.File" }));
    }

    [Test]
    public void Parse_ShouldReturnEmptyRuleSet_WhenDocumentIsEmpty()
    {
        // Act
        DependencyRuleSet ruleSet = _parser.Parse(string.Empty);

        // Assert
        Assert.That(ruleSet.Rules, Is.Empty);
    }

    [Test]
    public void Parse_ShouldOnlyCreateDenyRules_WhenAllowedSectionIsMissing()
    {
        // Arrange
        const string yaml = """
            denied:
              - from: App
                to: Infrastructure
            """;

        // Act
        DependencyRuleSet ruleSet = _parser.Parse(yaml);

        // Assert
        Assert.That(ruleSet.Rules, Has.Count.EqualTo(1));
        Assert.That(ruleSet.Rules[0].Action, Is.EqualTo(DependencyAction.Deny));
    }

    [Test]
    public void Parse_ShouldOnlyCreateAllowRules_WhenDeniedSectionIsMissing()
    {
        // Arrange
        const string yaml = """
            allowed:
              - from: App
                to: Domain
            """;

        // Act
        DependencyRuleSet ruleSet = _parser.Parse(yaml);

        // Assert
        Assert.That(ruleSet.Rules, Has.Count.EqualTo(1));
        Assert.That(ruleSet.Rules[0].Action, Is.EqualTo(DependencyAction.Allow));
    }

    [Test]
    public void Parse_ShouldCreateEveryAllowRule_WhenAllowedSectionHasMultipleEntries()
    {
        // Arrange
        const string yaml = """
            allowed:
              - from: App
                to: Domain
              - from: App
                to: Shared
              - from: Infrastructure
                to: Domain
            """;

        // Act
        DependencyRuleSet ruleSet = _parser.Parse(yaml);

        // Assert
        Assert.That(ruleSet.Rules.Count, Is.EqualTo(3));
        Assert.That(ruleSet.Rules, Has.All.Property("Action").EqualTo(DependencyAction.Allow));
    }

    [Test]
    public void Parse_ShouldCreateExposedToRule_WhenExposedToSectionHasSingleEntry()
    {
        // Arrange
        const string yaml = """
            exposedTo:
              - namespace: MyApp.Infra
                consumers:
                  - MyApp.Application
            """;

        // Act
        DependencyRuleSet ruleSet = _parser.Parse(yaml);

        // Assert
        Assert.That(ruleSet.ExposedToRules, Has.Count.EqualTo(1));
        Assert.That(ruleSet.ExposedToRules![0].Namespace, Is.EqualTo("MyApp.Infra"));
        Assert.That(ruleSet.ExposedToRules![0].Consumers, Has.Count.EqualTo(1));
        Assert.That(ruleSet.ExposedToRules![0].Consumers[0], Is.EqualTo("MyApp.Application"));
    }

    [Test]
    public void Parse_ShouldCreateEveryConsumer_WhenExposedToEntryHasMultipleConsumers()
    {
        // Arrange
        const string yaml = """
            exposedTo:
              - namespace: MyApp.Infra
                consumers:
                  - MyApp.Application
                  - MyApp.Worker
                  - MyApp.Admin
            """;

        // Act
        DependencyRuleSet ruleSet = _parser.Parse(yaml);

        // Assert
        Assert.That(ruleSet.ExposedToRules![0].Consumers, Has.Count.EqualTo(3));
        Assert.That(ruleSet.ExposedToRules![0].Consumers, Does.Contain("MyApp.Worker"));
    }

    [Test]
    public void Parse_ShouldCreateExposedToRulesInOrder_WhenExposedToSectionHasMultipleEntries()
    {
        // Arrange
        const string yaml = """
            exposedTo:
              - namespace: MyApp.Infra.Database
                consumers:
                  - MyApp.Application
              - namespace: MyApp.Infra.Messaging
                consumers:
                  - MyApp.Worker
            """;

        // Act
        DependencyRuleSet ruleSet = _parser.Parse(yaml);

        // Assert
        Assert.That(ruleSet.ExposedToRules, Has.Count.EqualTo(2));
        Assert.That(ruleSet.ExposedToRules![0].Namespace, Is.EqualTo("MyApp.Infra.Database"));
        Assert.That(ruleSet.ExposedToRules![1].Namespace, Is.EqualTo("MyApp.Infra.Messaging"));
    }

    [Test]
    public void Parse_ShouldReturnNoExposedToRules_WhenExposedToSectionIsMissing()
    {
        // Arrange
        const string yaml = """
            allowed:
              - from: App
                to: Domain
            """;

        // Act
        DependencyRuleSet ruleSet = _parser.Parse(yaml);

        // Assert
        Assert.That(ruleSet.ExposedToRules ?? [], Is.Empty);
    }

    [Test]
    public void Parse_ShouldReturnEmptyConsumerList_WhenExposedToEntryHasNoConsumers()
    {
        // Arrange
        const string yaml = """
            exposedTo:
              - namespace: MyApp.Infra
            """;

        // Act
        DependencyRuleSet ruleSet = _parser.Parse(yaml);

        // Assert
        Assert.That(ruleSet.ExposedToRules![0].Consumers, Is.Empty);
    }

    [Test]
    public void Parse_ShouldParseEverySection_WhenAllSectionsArePresent()
    {
        // Arrange
        const string yaml = """
            allowed:
              - from: App
                to: Domain
            denied:
              - from: App
                to: System.IO
            exposedTo:
              - namespace: Domain.Shared
                consumers:
                  - Domain.Orders
                  - Domain.Customers
            """;

        // Act
        DependencyRuleSet ruleSet = _parser.Parse(yaml);

        // Assert
        Assert.That(ruleSet.Rules, Has.Count.EqualTo(2));
        Assert.That(ruleSet.ExposedToRules, Has.Count.EqualTo(1));
        Assert.That(ruleSet.ExposedToRules![0].Consumers, Has.Count.EqualTo(2));
    }
}

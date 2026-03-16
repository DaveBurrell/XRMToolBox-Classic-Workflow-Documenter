using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Application;

/// <summary>
/// Computes deterministic quality metrics so overview cards can include a business-readable risk profile.
/// </summary>
public interface IWorkflowAnalysisEngine
{
    WorkflowQualityScore Analyze(WorkflowDefinition workflow, IReadOnlyList<ProcessingWarning> warnings);
}

public sealed class WorkflowAnalysisEngine : IWorkflowAnalysisEngine
{
    // ── Trigger specificity bonuses ───────────────────────────────────────────
    private const int TriggerSingleEventBonus = 6;
    private const int TriggerDualEventBonus = 3;
    private const int TriggerMultiEventBonus = 1;
    private const int TriggerNarrowFilterBonus = 8;   // 1–3 attribute filters
    private const int TriggerMediumFilterBonus = 4;   // 4–8 attribute filters
    private const int TriggerBroadFilterBonus = 1;    // >8 attribute filters

    // ── Complexity penalties ──────────────────────────────────────────────────
    private const int ComplexityLargeGraphThreshold = 20;
    private const int ComplexityLargeGraphPenalty = 10;
    private const int ComplexitySyncDenseThreshold = 12;
    private const int ComplexitySyncDensePenalty = 12;
    private const int ComplexityUnconditionedThreshold = 8;
    private const int ComplexityUnconditionedPenalty = 8;
    private const int ComplexityMaxPenalty = 60;

    // ── Dependency impact ─────────────────────────────────────────────────────
    private const int DependencyExternalCallScore = 10;
    private const int DependencyChildWorkflowScore = 5;
    private const int DependencyReferenceScore = 3;
    private const int DependencyMaxPenalty = 25;

    // ── Warning density penalties ─────────────────────────────────────────────
    private const int WarningBlockingPenaltyPerItem = 6;
    private const int WarningCriticalSeverityScore = 5;
    private const int WarningErrorSeverityScore = 4;
    private const int WarningWarningSeverityScore = 2;
    private const int WarningInfoSeverityScore = 1;
    private const int WarningDensityNumerator = 8;
    private const int WarningMaxPenalty = 30;

    public WorkflowQualityScore Analyze(WorkflowDefinition workflow, IReadOnlyList<ProcessingWarning> warnings)
    {
        var warningList = warnings ?? Array.Empty<ProcessingWarning>();

        var triggerSpecificity = CalculateTriggerSpecificity(workflow.Trigger);
        var complexity = CalculateComplexityPenalty(workflow);
        var dependencyImpact = CalculateDependencyImpact(workflow.Dependencies);
        var warningDensity = CalculateWarningDensityPenalty(warningList, workflow);

        var rawScore = 100 - complexity - dependencyImpact - warningDensity + triggerSpecificity;
        var overall = Math.Clamp(rawScore, 0, 100);
        var riskBand = GetRiskBand(overall);

        var breakdown = new QualityScoreBreakdown(
            TriggerSpecificity: triggerSpecificity,
            Complexity: complexity,
            DependencyImpact: dependencyImpact,
            WarningDensity: warningDensity);

        var summary = BuildSummary(overall, riskBand, workflow, warningList);
        return new WorkflowQualityScore(overall, riskBand, breakdown, summary);
    }

    private static int CalculateTriggerSpecificity(WorkflowTrigger trigger)
    {
        var eventCount = 0;
        if (trigger.OnCreate)
        {
            eventCount++;
        }

        if (trigger.OnUpdate)
        {
            eventCount++;
        }

        if (trigger.OnDelete)
        {
            eventCount++;
        }

        if (eventCount == 0)
        {
            return 0;
        }

        // Narrowly-scoped trigger events and field filters improve predictability.
        var eventSpecificity = eventCount switch
        {
            1 => TriggerSingleEventBonus,
            2 => TriggerDualEventBonus,
            _ => TriggerMultiEventBonus
        };

        var filterSpecificity = trigger.AttributeFilters.Count switch
        {
            0 => 0,
            <= 3 => TriggerNarrowFilterBonus,
            <= 8 => TriggerMediumFilterBonus,
            _ => TriggerBroadFilterBonus
        };

        return eventSpecificity + filterSpecificity;
    }

    private static int CalculateComplexityPenalty(WorkflowDefinition workflow)
    {
        var nodes = workflow.StageGraph.Nodes.Count;
        var edges = workflow.StageGraph.Edges.Count;
        var conditions = CountConditionNodes(workflow.RootCondition);

        // Keep the penalty non-linear to highlight very dense process graphs.
        var basePenalty = nodes + (edges / 2) + (conditions * 2);
        if (nodes >= ComplexityLargeGraphThreshold)
        {
            basePenalty += ComplexityLargeGraphPenalty;
        }

        if (workflow.ExecutionMode == ExecutionMode.Synchronous && nodes >= ComplexitySyncDenseThreshold)
        {
            basePenalty += ComplexitySyncDensePenalty;
        }

        if (workflow.RootCondition is null && nodes >= ComplexityUnconditionedThreshold)
        {
            basePenalty += ComplexityUnconditionedPenalty;
        }

        return Math.Min(basePenalty, ComplexityMaxPenalty);
    }

    private static int CalculateDependencyImpact(IReadOnlyList<WorkflowDependency> dependencies)
    {
        if (dependencies.Count == 0)
        {
            return 0;
        }

        var total = 0;
        foreach (var dependency in dependencies)
        {
            total += dependency.DependencyType switch
            {
                "ExternalCall" => DependencyExternalCallScore,
                "ChildWorkflow" => DependencyChildWorkflowScore,
                _ => DependencyReferenceScore
            };
        }

        return Math.Min(total, DependencyMaxPenalty);
    }

    private static int CalculateWarningDensityPenalty(
        IReadOnlyList<ProcessingWarning> warnings,
        WorkflowDefinition workflow)
    {
        if (warnings.Count == 0)
        {
            return 0;
        }

        var blockingPenalty = warnings.Count(x => x.IsBlocking) * WarningBlockingPenaltyPerItem;
        var severityPenalty = warnings.Sum(w => w.Severity switch
        {
            WarningSeverity.Critical => WarningCriticalSeverityScore,
            WarningSeverity.Error => WarningErrorSeverityScore,
            WarningSeverity.Warning => WarningWarningSeverityScore,
            _ => WarningInfoSeverityScore
        });

        var nodeCount = Math.Max(workflow.StageGraph.Nodes.Count, 1);
        var densityPenalty = (warnings.Count * WarningDensityNumerator) / nodeCount;

        return Math.Min(blockingPenalty + severityPenalty + densityPenalty, WarningMaxPenalty);
    }

    private static int CountConditionNodes(ConditionNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        return 1 + node.Children.Sum(CountConditionNodes);
    }

    private static string GetRiskBand(int overall)
    {
        if (overall >= 80)
        {
            return "Low";
        }

        if (overall >= 60)
        {
            return "Medium";
        }

        return "High";
    }

    private static string BuildSummary(
        int overall,
        string riskBand,
        WorkflowDefinition workflow,
        IReadOnlyList<ProcessingWarning> warnings)
    {
        var modeText = workflow.ExecutionMode == ExecutionMode.Synchronous ? "real-time" : "background";
        return $"Quality score {overall}/100 ({riskBand} risk). " +
            $"This {modeText} workflow has {workflow.StageGraph.Nodes.Count} steps, " +
            $"{workflow.Dependencies.Count} dependencies, and {warnings.Count} warnings.";
    }
}

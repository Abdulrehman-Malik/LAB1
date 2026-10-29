using Autoscaler;

namespace Lab.Tests;

/// <summary>The autoscaler's rule, case by case. Run with: dotnet test</summary>
public class ScalingDecisionTests
{
    private static readonly ServiceRule Rule = new()
    {
        Min = 1,
        Max = 3,
        ScaleUpAbovePercent = 60,
        ScaleDownBelowPercent = 20,
        CooldownSeconds = 45,
    };

    [Fact]
    public void Busy_service_gets_one_more_replica()
    {
        var (target, _) = ScalingDecision.Decide(Rule, replicas: 1, cpuPercent: 90, inCooldown: false);
        Assert.Equal(2, target);
    }

    [Fact]
    public void Never_scales_above_max()
    {
        var (target, _) = ScalingDecision.Decide(Rule, replicas: 3, cpuPercent: 99, inCooldown: false);
        Assert.Equal(3, target);
    }

    [Fact]
    public void Idle_service_loses_one_replica()
    {
        var (target, _) = ScalingDecision.Decide(Rule, replicas: 3, cpuPercent: 5, inCooldown: false);
        Assert.Equal(2, target);
    }

    [Fact]
    public void Idle_service_keeps_at_least_min_replicas()
    {
        var (target, _) = ScalingDecision.Decide(Rule, replicas: 1, cpuPercent: 0, inCooldown: false);
        Assert.Equal(1, target);
    }

    [Fact]
    public void Cooldown_blocks_changes()
    {
        var (target, reason) = ScalingDecision.Decide(Rule, replicas: 2, cpuPercent: 99, inCooldown: true);
        Assert.Equal(2, target);
        Assert.Equal("cooldown", reason);
    }

    [Fact]
    public void Scaled_to_zero_by_hand_comes_back_to_min_even_in_cooldown()
    {
        var (target, _) = ScalingDecision.Decide(Rule, replicas: 0, cpuPercent: 0, inCooldown: true);
        Assert.Equal(1, target);
    }

    [Fact]
    public void Too_many_replicas_are_cut_back_to_max()
    {
        var (target, _) = ScalingDecision.Decide(Rule, replicas: 5, cpuPercent: 50, inCooldown: false);
        Assert.Equal(3, target);
    }

    [Fact]
    public void A_single_spike_does_not_scale_when_load_must_be_sustained()
    {
        var rule = new ServiceRule { Min = 1, Max = 3, ScaleUpAbovePercent = 60, ScaleDownBelowPercent = 20, SustainedChecks = 2 };

        var (first, _) = ScalingDecision.Decide(rule, replicas: 1, cpuPercent: 90, inCooldown: false, checksAbove: 1);
        var (second, _) = ScalingDecision.Decide(rule, replicas: 1, cpuPercent: 90, inCooldown: false, checksAbove: 2);

        Assert.Equal(1, first);    // first high reading: wait
        Assert.Equal(2, second);   // still high on the next check: scale up
    }

    [Fact]
    public void Scale_down_also_waits_for_sustained_low_cpu()
    {
        var rule = new ServiceRule { Min = 1, Max = 3, ScaleUpAbovePercent = 60, ScaleDownBelowPercent = 20, SustainedChecks = 3 };

        var (target, _) = ScalingDecision.Decide(rule, replicas: 3, cpuPercent: 0, inCooldown: false, checksBelow: 2);

        Assert.Equal(3, target);
    }

    [Fact]
    public void Cpu_between_thresholds_changes_nothing()
    {
        var (target, reason) = ScalingDecision.Decide(Rule, replicas: 2, cpuPercent: 40, inCooldown: false);
        Assert.Equal(2, target);
        Assert.Equal("no change", reason);
    }
}

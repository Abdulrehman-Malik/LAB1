namespace Autoscaler;

/// <summary>
/// The scaling rule as a pure function: no Docker, no clock, so it is easy to unit-test
/// (tests/Lab.Tests/ScalingDecisionTests.cs). Moves one replica at a time.
/// </summary>
public static class ScalingDecision
{
    /// <param name="checksAbove">How many checks in a row (including this one) CPU has been above scaleUpAbovePercent.</param>
    /// <param name="checksBelow">How many checks in a row (including this one) CPU has been below scaleDownBelowPercent.</param>
    public static (int Target, string Reason) Decide(
        ServiceRule rule, int replicas, double cpuPercent, bool inCooldown, int checksAbove = 1, int checksBelow = 1)
    {
        // Bounds always win, even during cooldown (e.g. someone scaled the service to 0 by hand)
        if (replicas < rule.Min) return (rule.Min, $"below min {rule.Min}");
        if (replicas > rule.Max) return (rule.Max, $"above max {rule.Max}");

        if (inCooldown) return (replicas, "cooldown");

        // Load must be sustained: one short CPU spike (JIT, GC, a slow health check) must not add a replica
        if (cpuPercent > rule.ScaleUpAbovePercent && replicas < rule.Max)
        {
            return checksAbove >= rule.SustainedChecks
                ? (replicas + 1, $"CPU above {rule.ScaleUpAbovePercent}% for {checksAbove} checks")
                : (replicas, $"CPU high, waiting ({checksAbove}/{rule.SustainedChecks} checks)");
        }
        if (cpuPercent < rule.ScaleDownBelowPercent && replicas > rule.Min)
        {
            return checksBelow >= rule.SustainedChecks
                ? (replicas - 1, $"CPU below {rule.ScaleDownBelowPercent}% for {checksBelow} checks")
                : (replicas, $"CPU low, waiting ({checksBelow}/{rule.SustainedChecks} checks)");
        }

        return (replicas, "no change");
    }
}

using Autoscaler;

namespace Lab.Tests;

/// <summary>Checks the real infra/autoscale.yml, so a bad edit is caught by "dotnet test" instead of in production.</summary>
public class AutoscaleConfigTests
{
    private static readonly AutoscaleConfig Config = AutoscaleConfig.Load(Path.Combine(AppContext.BaseDirectory, "autoscale.yml"));

    [Fact]
    public void Config_file_lists_services()
    {
        Assert.NotEmpty(Config.Services);
        Assert.Equal("lab", Config.Stack);
    }

    [Fact]
    public void Every_service_keeps_at_least_one_instance()
    {
        Assert.All(Config.Services, s => Assert.True(s.Value.Min >= 1, $"{s.Key}: min must be >= 1"));
    }

    [Fact]
    public void Scale_up_threshold_is_above_scale_down_threshold()
    {
        // If they overlap the service would flap: scale up, then immediately down, then up again...
        Assert.All(Config.Services, s => Assert.True(
            s.Value.ScaleUpAbovePercent > s.Value.ScaleDownBelowPercent,
            $"{s.Key}: scaleUpAbovePercent ({s.Value.ScaleUpAbovePercent}) must be above scaleDownBelowPercent ({s.Value.ScaleDownBelowPercent})"));
    }

    [Fact]
    public void Max_replicas_fit_on_an_8_GB_laptop()
    {
        Assert.All(Config.Services, s => Assert.True(s.Value.Max <= 3, $"{s.Key}: max {s.Value.Max} is too many for this machine"));
    }

    [Fact]
    public void Every_service_ignores_single_cpu_spikes()
    {
        Assert.All(Config.Services, s => Assert.True(s.Value.SustainedChecks >= 2,
            $"{s.Key}: sustainedChecks should be at least 2, or one JIT/GC spike can trigger a scale-up"));
    }

    [Fact]
    public void Background_worker_is_not_autoscaled()
    {
        Assert.DoesNotContain("notification-worker", Config.Services.Keys);
    }

    [Fact]
    public void Min_below_one_in_the_file_is_raised_to_one()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "services:\n  demo-api:\n    min: 0\n    max: 0\n");

        var config = AutoscaleConfig.Load(path);

        Assert.Equal(1, config.Services["demo-api"].Min);
        Assert.Equal(1, config.Services["demo-api"].Max);
    }
}

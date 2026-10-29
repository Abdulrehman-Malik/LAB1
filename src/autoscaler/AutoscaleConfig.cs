using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Autoscaler;

/// <summary>The contents of infra/autoscale.yml.</summary>
public class AutoscaleConfig
{
    public string Stack { get; set; } = "lab";
    public int CheckIntervalSeconds { get; set; } = 15;
    public int StartupGraceSeconds { get; set; } = 60;
    public Dictionary<string, ServiceRule> Services { get; set; } = [];

    public static AutoscaleConfig Load(string path)
    {
        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        var config = deserializer.Deserialize<AutoscaleConfig>(File.ReadAllText(path));

        // The lab's rule: there is always at least one instance running, whatever the file says
        foreach (var rule in config.Services.Values)
        {
            rule.Min = Math.Max(1, rule.Min);
            rule.Max = Math.Max(rule.Min, rule.Max);
            rule.SustainedChecks = Math.Max(1, rule.SustainedChecks);
        }
        return config;
    }
}

public class ServiceRule
{
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 3;
    public double ScaleUpAbovePercent { get; set; } = 60;
    public double ScaleDownBelowPercent { get; set; } = 20;
    public int CooldownSeconds { get; set; } = 60;
    public int SustainedChecks { get; set; } = 1;   // checks in a row the CPU condition must hold before acting
}

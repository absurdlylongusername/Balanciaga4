namespace Balanciaga4.Options;

public sealed class LbOptionsRaw
{
    public string Listen { get; set; } = "0.0.0.0:8080";
    public Policy Policy { get; set; } = Policy.RoundRobin;
    public string[] Backends { get; set; } = [];
    public LimitsOptions Limits { get; set; } = new();
    public TimeoutsOptions Timeouts { get; set; } = new();
    public HealthCheckOptions HealthCheck { get; set; } = new();
}

namespace M2Forge.Core.Firmware;

public sealed record HealthReport(FirmwareHealth Health, string Detail, string? Name = null)
{
    public bool IsUsable => Health != FirmwareHealth.Damaged;
}

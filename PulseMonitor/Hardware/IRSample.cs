namespace PulseMonitor.Hardware;

public readonly record struct IRSample(long Timestamp, uint IR, uint Red);

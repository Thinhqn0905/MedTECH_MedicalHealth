namespace PulseMonitor.Processing;

public readonly record struct ProcessedSample(long Timestamp, uint IR, uint Red, int BPM, int SpO2);

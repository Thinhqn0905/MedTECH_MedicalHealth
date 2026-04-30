namespace PulseMonitor.Processing;

public readonly record struct DiagnosticSample(
    long Timestamp, 
    uint? IR = null, 
    uint? Red = null, 
    int? BPM = null, 
    int? SpO2 = null, 
    float? Ecg = null
);

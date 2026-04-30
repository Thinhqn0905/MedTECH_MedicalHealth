namespace PulseMonitor.Hardware;

/// <summary>
/// Carries the HRV diagnostic result emitted by the ESP32-S3 firmware
/// approximately every 5 seconds when enough RR-intervals are available.
/// </summary>
/// <param name="Timestamp">Board millis() at the time of computation.</param>
/// <param name="Sdnn">SDNN (ms) — standard deviation of NN intervals. ≥50 ms = healthy.</param>
/// <param name="Rmssd">RMSSD (ms) — root mean square of successive differences.</param>
/// <param name="Rhythm">Rhythm classification string: "Normal", "Tachycardia", "Bradycardia", "Irregular".</param>
/// <param name="StressLevel">0=Low, 1=Moderate, 2=High, 3=VeryHigh (SDNN-based sympathovagal balance).</param>
public readonly record struct AiDiagnosticResult(
    long   Timestamp,
    float  Sdnn,
    float  Rmssd,
    string Rhythm,
    int    StressLevel);

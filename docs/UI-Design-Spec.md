# PulseMonitor UI Design Specification

## Theme
- Background: #F4F8FB
- Primary accent: #007AFF
- Secondary accent: #34C759
- Alert color: #FF3B30
- Font family: Segoe UI
- Base font size: 13px

## Layout
- Top header bar: logo + app name + connection status dot
- Left sidebar: BPM card, SpO2 card, session timer card
- Main area: scrolling waveform chart with 8-second window
- Bottom toolbar: Connect, Start Recording, Export Email, Settings

## Data Display Rules
- BPM numeral: 48px, bold, rounded card
- SpO2 numeral: 48px, bold, rounded card
- Alert conditions:
  - SpO2 < 95 => alert red
  - BPM < 50 or BPM > 120 => alert red
  - Otherwise use healthy green

## Waveform Styling
- IR channel color: #007AFF
- Red channel color: #C5CDD5
- Chart window length: 8 seconds

## Interaction
- Hardware I/O runs on background tasks
- UI updates dispatched to WPF Dispatcher
- Connection status dot reflects connection events in real time

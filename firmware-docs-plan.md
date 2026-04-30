# ESP32 Firmware Planning Documents

## Goal
Scan the entire MedTech_Device source tree, then create 4 planning documents in `/docs/` as specified by `context.xml`: PLANNING.md, FIRMWARE_PPG.md, FIRMWARE_ECG.md, MODEL_PLAN.md.

## Tasks
- [x] Task 1: Write source scan summary → Verify: every file/dir listed with one-line purpose
- [x] Task 2: Create `docs/PLANNING.md` (arch diagram, BLE topology, phase roadmap, checklist) → Verify: file exists, ASCII diagram renders, all checklist items start `[ ]`
- [x] Task 3: Create `docs/FIRMWARE_PPG.md` (MAX30102 wiring, registers, HR algo, SpO2, BLE GATT, test checklist) → Verify: file exists, hex values present or `[VERIFY]` tagged
- [x] Task 4: Create `docs/FIRMWARE_ECG.md` (AD8232 wiring, ADC config, lead-off, BLE peripheral, data framing, test checklist) → Verify: file exists, pin assignments present
- [x] Task 5: Create `docs/MODEL_PLAN.md` (AFDB model locate, TFLite conversion pipeline, ESP32 deployment, inference trigger, checklist) → Verify: file exists, references `Model/AFDB_MODEL.tflite`
- [x] Task 6: Output completion summary table → Verify: table printed with document / purpose / checklist-count

## Done When
- [x] All 4 `.md` files exist in `docs/`
- [x] No fabricated values (all uncertain values tagged `[VERIFY]`)
- [x] All checklist items start unchecked `[ ]`

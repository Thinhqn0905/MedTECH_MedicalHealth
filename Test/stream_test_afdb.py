#!/usr/bin/env python3
"""
stream_test_afdb.py — Serial Streaming Test Harness for AFDB AF Detection

Streams pre-processed ECG windows from .npy test files to ESP32-S3 over Serial,
collects inference results, and computes accuracy metrics.

Protocol:
  PC → ESP32:  "STREAM_TEST\n"          → ESP32: "STREAM_TEST_ACK\n"
  PC → ESP32:  "WINDOW\n" + 10000 bytes → ESP32: "RESULT:<prob>:<time_ms>\n"
  PC → ESP32:  "STREAM_END\n"           → ESP32: "STREAM_END_ACK\n"

Usage:
  python stream_test_afdb.py --port COM7 --data-dir ./test_4_records/individual_records
"""

import argparse
import glob
import os
import struct
import sys
import time

import numpy as np
import serial
import serial.tools.list_ports


def is_esp_usb_serial(port):
    for p in serial.tools.list_ports.comports():
        if p.device.upper() == port.upper():
            return p.vid == 0x303A
    return False


def open_serial(port, baud, timeout_s, port_is_esp_usb):
    ser = serial.Serial()
    ser.port = port
    ser.baudrate = baud
    ser.timeout = timeout_s
    ser.write_timeout = timeout_s
    # Avoid ESP32-S3 auto-bootloader by keeping DTR/RTS low before open on UART bridges.
    if not port_is_esp_usb:
        ser.setDTR(False)
        ser.setRTS(False)
    ser.open()
    if port_is_esp_usb:
        # USB-Serial/JTAG CDC often needs DTR asserted to allow writes.
        ser.setDTR(True)
        ser.setRTS(False)
    else:
        ser.setDTR(False)
        ser.setRTS(False)
    ser.reset_input_buffer()
    ser.reset_output_buffer()
    return ser


def force_app_boot(ser, port_is_esp_usb):
    if port_is_esp_usb:
        return
    # IO0 high (DTR False), pulse EN low/high (RTS True -> False).
    ser.setDTR(False)
    ser.setRTS(True)
    time.sleep(0.1)
    ser.setRTS(False)
    time.sleep(0.3)


def read_result_line(ser, timeout_s):
    deadline = time.time() + timeout_s
    while time.time() < deadline:
        line = ser.readline().decode(errors="ignore").strip()
        if not line:
            continue
        if line.startswith("RESULT:"):
            return line
        print(f"  [device] {line}")
    return "RESULT:TIMEOUT:0"


def safe_write(ser, payload, port, baud, timeout_s, label, port_is_esp_usb, reopen=True, retries=2):
    for attempt in range(retries + 1):
        try:
            ser.reset_output_buffer()
            if port_is_esp_usb:
                ser.setDTR(True)
                ser.setRTS(False)
            else:
                ser.setDTR(False)
                ser.setRTS(False)
            ser.write(payload)
            return ser, True
        except serial.SerialTimeoutException:
            if not reopen or attempt >= retries:
                print(f"ERROR: {label} write timeout.")
                return ser, False
            print(f"WARNING: {label} write timeout, reopening serial (attempt {attempt + 1}/{retries + 1}).")
            try:
                force_app_boot(ser, port_is_esp_usb)
                ser.close()
            except Exception:
                pass
            time.sleep(0.5)
            ser = open_serial(port, baud, timeout_s, port_is_esp_usb)
    return ser, False


def send_payload(ser, payload, chunk_size, delay_s):
    offset = 0
    total = len(payload)
    while offset < total:
        try:
            written = ser.write(payload[offset:offset + chunk_size])
        except serial.SerialTimeoutException:
            return False
        if written is None:
            written = 0
        if written == 0:
            time.sleep(delay_s)
            continue
        offset += written
        if delay_s:
            time.sleep(delay_s)
    ser.flush()
    return True


def main():
    parser = argparse.ArgumentParser(description="AFDB Streaming Test Harness")
    parser.add_argument("--port", required=True, help="Serial port (e.g. COM7)")
    parser.add_argument("--baud", type=int, default=115200, help="Baud rate")
    parser.add_argument("--data-dir", required=True, help="Directory with X_*.npy and y_*.npy files")
    parser.add_argument("--max-windows", type=int, default=0, help="Max windows per patient (0=all)")
    parser.add_argument("--threshold", type=float, default=0.5, help="AF threshold")
    args = parser.parse_args()

    # Find test files
    x_files = sorted(glob.glob(os.path.join(args.data_dir, "X_*.npy")))
    if not x_files:
        print(f"ERROR: No X_*.npy files found in {args.data_dir}")
        sys.exit(1)

    print(f"Found {len(x_files)} patient records.")

    port_is_esp_usb = is_esp_usb_serial(args.port)
    # Open serial safely, then assert DTR/RTS so the driver can transmit.
    ser = open_serial(args.port, args.baud, 10, port_is_esp_usb)
    force_app_boot(ser, port_is_esp_usb)
    
    time.sleep(2)  # Wait for ESP32 (if it did reboot)

    print("Connecting to ESP32... waiting for boot")
    # Enter test mode with retry (handles ESP32 reboot on serial open)
    ack = ""
    for attempt in range(20):
        ser, ok = safe_write(
            ser,
            b"STREAM_TEST\n",
            args.port,
            args.baud,
            10,
            label="STREAM_TEST",
            port_is_esp_usb=port_is_esp_usb,
            reopen=True,
        )
        if not ok:
            ser.close()
            sys.exit(1)
        time.sleep(0.5)
        while ser.in_waiting > 0:
            line = ser.readline().decode(errors="ignore").strip()
            if line == "STREAM_TEST_ACK":
                ack = line
                break
            elif line:
                print(f"  [boot] {line}")
        if ack == "STREAM_TEST_ACK":
            break

    if ack != "STREAM_TEST_ACK":
        print(f"ERROR: Expected STREAM_TEST_ACK, got timeout.")
        ser.close()
        sys.exit(1)

    print("ESP32 entered STREAM_TEST mode.")

    # Overall stats
    total_tp = 0
    total_fp = 0
    total_tn = 0
    total_fn = 0
    total_inf_times = []

    for x_path in x_files:
        patient_id = os.path.basename(x_path).replace("X_", "").replace(".npy", "")
        y_path = os.path.join(args.data_dir, f"y_{patient_id}.npy")

        if not os.path.exists(y_path):
            print(f"  WARNING: Missing y_{patient_id}.npy, skipping.")
            continue

        X = np.load(x_path)  # shape: (N, 2500, 1) float32
        y = np.load(y_path)  # shape: (N,) int8

        n_windows = X.shape[0]
        if args.max_windows > 0:
            n_windows = min(n_windows, args.max_windows)

        print(f"\n--- Patient {patient_id}: {n_windows} windows (AF={np.sum(y[:n_windows])}) ---")

        tp = fp = tn = fn = 0
        inf_times = []
        errors = 0

        for i in range(n_windows):
            window = X[i, :, 0].astype(np.float32)  # (2500,) float32
            label = int(y[i])

            chunk_size = 512
            chunk_delay_s = 0.05 # Force 50ms delay for maximum stability

            # Send WINDOW command
            ser, ok = safe_write(
                ser,
                b"WINDOW\n",
                args.port,
                args.baud,
                10,
                label="WINDOW",
                port_is_esp_usb=port_is_esp_usb,
                reopen=False,
            )
            if not ok:
                ser.close()
                sys.exit(1)
            time.sleep(0.01)

            # Send 2500 float32 values as binary (little-endian)
            if not send_payload(ser, window.tobytes(), chunk_size, chunk_delay_s):
                print("ERROR: WINDOW_DATA write timeout.")
                ser.close()
                sys.exit(1)

            # Read result
            result_line = read_result_line(ser, timeout_s=12)

            if result_line.startswith("RESULT:") and "ERROR" not in result_line and "TIMEOUT" not in result_line:
                parts = result_line.split(":")
                prob = float(parts[1])
                inf_time = int(parts[2]) if len(parts) > 2 else 0
                inf_times.append(inf_time)

                pred = 1 if prob > args.threshold else 0
                if pred == 1 and label == 1:
                    tp += 1
                elif pred == 1 and label == 0:
                    fp += 1
                elif pred == 0 and label == 0:
                    tn += 1
                else:
                    fn += 1
            else:
                errors += 1
                if errors <= 5:
                    print(f"  ERROR at window {i}: {result_line}")

            # Progress
            if (i + 1) % 100 == 0 or i == n_windows - 1:
                print(f"  [{i+1}/{n_windows}] TP={tp} FP={fp} TN={tn} FN={fn} errs={errors}", end="\r")

        print()

        # Per-patient metrics
        total = tp + fp + tn + fn
        if total > 0:
            acc = (tp + tn) / total * 100
            sens = tp / max(tp + fn, 1) * 100
            spec = tn / max(tn + fp, 1) * 100
            avg_time = np.mean(inf_times) if inf_times else 0
            print(f"  Accuracy: {acc:.1f}% | Sensitivity: {sens:.1f}% | Specificity: {spec:.1f}%")
            print(f"  Avg inference time: {avg_time:.0f} ms | Errors: {errors}")

        total_tp += tp
        total_fp += fp
        total_tn += tn
        total_fn += fn
        total_inf_times.extend(inf_times)

    # Exit test mode
    ser.write(b"STREAM_END\n")
    ser.readline()
    ser.close()

    # Overall metrics
    print("\n" + "=" * 60)
    print("OVERALL RESULTS")
    print("=" * 60)
    total = total_tp + total_fp + total_tn + total_fn
    if total > 0:
        acc = (total_tp + total_tn) / total * 100
        sens = total_tp / max(total_tp + total_fn, 1) * 100
        spec = total_tn / max(total_tn + total_fp, 1) * 100
        print(f"Total windows: {total}")
        print(f"TP={total_tp} FP={total_fp} TN={total_tn} FN={total_fn}")
        print(f"Accuracy:    {acc:.2f}%")
        print(f"Sensitivity: {sens:.2f}%")
        print(f"Specificity: {spec:.2f}%")
        if total_inf_times:
            print(f"Avg inference: {np.mean(total_inf_times):.0f} ms")
            print(f"Max inference: {np.max(total_inf_times):.0f} ms")
    else:
        print("No valid results collected.")


if __name__ == "__main__":
    main()

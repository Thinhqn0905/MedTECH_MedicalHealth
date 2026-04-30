#pragma once
// ============================================================
// af_inference.h — TFLite Micro wrapper for AFDB AF detection
//
// Runs INT8 quantized Dilated Mobile TCN model on ESP32-S3.
// Input:  float[2500] Z-Score normalized ECG window [-3, 3]
// Output: AF probability (0.0 – 1.0)
// ============================================================
#ifndef AF_INFERENCE_H
#define AF_INFERENCE_H

#include <stdint.h>

/// Tensor arena size (bytes). Model has ~236 tensors.
/// Prefer PSRAM if available; fall back to internal SRAM when PSRAM is absent.
#define TENSOR_ARENA_SIZE (245 * 1024)

/// AF detection threshold (probability above this = AF positive)
#define AF_THRESHOLD 0.5f

/// Initialize TFLite Micro interpreter. Call once in setup().
/// Returns true if initialization succeeded.
bool afInferenceInit();
bool afInferenceIsReady();

/// Run AF inference on a normalized ECG window.
/// @param normalizedWindow  Pointer to float[2500] Z-Score normalized data
/// @param outProbability    Output AF probability [0.0, 1.0]
/// @return true if inference succeeded, false on error
bool afInferenceRun(const float *normalizedWindow, float *outProbability);

/// Get the last inference time in milliseconds.
uint32_t afInferenceTimeMs();

#endif // AF_INFERENCE_H

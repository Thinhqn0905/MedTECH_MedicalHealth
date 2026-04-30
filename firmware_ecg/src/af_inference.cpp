// ============================================================
// af_inference.cpp — TFLite Micro AF detection engine
//
// Legacy path (TensorFlowLite_ESP32) is used for stream_test
// stability. ESP-NN path remains for separate debugging.
// ============================================================
#include "af_inference.h"
#include "afdb_model_data.h"

#include <Arduino.h>

#if defined(USE_LEGACY_TFLITE_ESP32)
#include "tensorflow/lite/micro/all_ops_resolver.h"
#include "tensorflow/lite/micro/micro_error_reporter.h"
#include "tensorflow/lite/micro/micro_interpreter.h"
#include "tensorflow/lite/schema/schema_generated.h"
#include <TensorFlowLite_ESP32.h>
#else
#include "esp32-hal-psram.h"
#include "esp_heap_caps.h"
#include "tensorflow/lite/micro/micro_interpreter.h"
#include "tensorflow/lite/micro/micro_mutable_op_resolver.h"
#include "tensorflow/lite/micro/tflite_bridge/micro_error_reporter.h"
#include "tensorflow/lite/schema/schema_generated.h"
#endif

#include <esp_task_wdt.h>

// ============================================================
// Statics
// ============================================================
static const tflite::Model *s_model = nullptr;
static tflite::MicroInterpreter *s_interpreter = nullptr;
static TfLiteTensor *s_input = nullptr;
static TfLiteTensor *s_output = nullptr;

// Tensor arena — prefer PSRAM, fall back to internal SRAM
static uint8_t *s_tensorArena = nullptr;

// Quantization parameters (from model inspection)
static float s_inputScale = 0.0f;
static int32_t s_inputZeroPoint = 0;
static float s_outputScale = 0.0f;
static int32_t s_outputZeroPoint = 0;

// Timing
static uint32_t s_lastInferenceMs = 0;

// Op resolver (legacy uses AllOpsResolver; ESP-NN uses explicit ops)
#if defined(USE_LEGACY_TFLITE_ESP32)
static tflite::AllOpsResolver s_resolver;
static tflite::MicroErrorReporter s_errorReporter;
#else
static tflite::MicroMutableOpResolver<20> s_resolver;
static tflite::MicroErrorReporter s_errorReporter;
#endif

// ============================================================
// Init
// ============================================================
bool afInferenceInit() {
  Serial.println("[AF] Initializing TFLite Micro...");
  Serial.printf("[AF] Free heap: %u bytes, PSRAM: %u bytes\n",
                (unsigned)ESP.getFreeHeap(), (unsigned)ESP.getFreePsram());

#if defined(USE_LEGACY_TFLITE_ESP32)
  // Force internal SRAM for legacy path stability.
  /*
#ifdef BOARD_HAS_PSRAM
    ...
#endif
  */
  s_tensorArena = (uint8_t *)malloc(TENSOR_ARENA_SIZE);
  if (s_tensorArena != nullptr) {
    Serial.printf("[AF] Tensor arena: %d KB on internal SRAM\n",
                  TENSOR_ARENA_SIZE / 1024);
  }

  if (s_tensorArena == nullptr) {
    Serial.printf("[AF] ERROR: Failed to allocate %d KB tensor arena!\n",
                  TENSOR_ARENA_SIZE / 1024);
    return false;
  }
#else
  // Prefer PSRAM, but fall back to internal SRAM if PSRAM is missing.
  bool usingPsram = false;
#ifdef BOARD_HAS_PSRAM
  if (psramFound()) {
    s_tensorArena = (uint8_t *)heap_caps_aligned_alloc(
        16, TENSOR_ARENA_SIZE, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    if (s_tensorArena != nullptr) {
      usingPsram = true;
    }
  }
#endif
  if (s_tensorArena == nullptr) {
    Serial.println("[AF] PSRAM not detected or allocation failed. Using internal SRAM.");
    s_tensorArena = (uint8_t *)heap_caps_aligned_alloc(
        16, TENSOR_ARENA_SIZE, MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
    if (s_tensorArena == nullptr) {
      s_tensorArena = (uint8_t *)malloc(TENSOR_ARENA_SIZE);
    }
  }

  if (s_tensorArena == nullptr) {
    Serial.printf("[AF] FATAL: Failed to allocate %d KB tensor arena\n",
                  TENSOR_ARENA_SIZE / 1024);
    return false;
  }

  memset(s_tensorArena, 0, TENSOR_ARENA_SIZE);
  if (usingPsram) {
    Serial.printf(
        "[AF] Allocated %d KB aligned arena on PSRAM at %p (align=%d)\n",
        TENSOR_ARENA_SIZE / 1024, s_tensorArena,
        (int)((uintptr_t)s_tensorArena & 0xF));
  } else {
    Serial.printf("[AF] Tensor arena: %d KB on internal SRAM at %p\n",
                  TENSOR_ARENA_SIZE / 1024, s_tensorArena);
  }
#endif

  // Load model
  Serial.println("[AF] Loading model...");
  s_model = tflite::GetModel(afdb_model_data);
  if (s_model == nullptr) {
    Serial.println("[AF] ERROR: Failed to load model!");
    return false;
  }

  if (s_model->version() != TFLITE_SCHEMA_VERSION) {
    Serial.printf("[AF] ERROR: Model schema version %lu != expected %d\n",
                  (unsigned long)s_model->version(), TFLITE_SCHEMA_VERSION);
    return false;
  }

#if defined(USE_LEGACY_TFLITE_ESP32)
  // Create interpreter (legacy uses AllOpsResolver).
  Serial.println("[AF] Creating interpreter...");
  s_interpreter = new tflite::MicroInterpreter(
      s_model, s_resolver, s_tensorArena, TENSOR_ARENA_SIZE, &s_errorReporter);
#else
  // Add exactly the 18 operators used by AFDB_int8.tflite
  // (verified via Python: inspect_model.py)
  Serial.println("[AF] Registering operators...");
  s_resolver.AddAdd();             // x11
  s_resolver.AddBatchToSpaceNd();  // x4
  s_resolver.AddConcatenation();   // x1
  s_resolver.AddConv2D();          // x13
  s_resolver.AddDepthwiseConv2D(); // x5
  s_resolver.AddExpandDims();      // x18
  s_resolver.AddFullyConnected();  // x11
  s_resolver.AddLeakyRelu();       // x18
  s_resolver.AddLogistic();        // x6
  s_resolver.AddMean();            // x6
  s_resolver.AddMul();             // x11
  s_resolver.AddPack();            // x5
  s_resolver.AddReduceMax();       // x1
  s_resolver.AddReshape();         // x23
  s_resolver.AddShape();           // x5
  s_resolver.AddSpaceToBatchNd();  // x4
  s_resolver.AddStridedSlice();    // x5
  s_resolver.AddQuantize();        // for INT8 I/O

  // Create interpreter
  Serial.println("[AF] Creating interpreter...");
  s_interpreter = new tflite::MicroInterpreter(
      s_model, s_resolver, s_tensorArena, TENSOR_ARENA_SIZE);
#endif

  // Allocate tensors
  Serial.println("[AF] Allocating tensors...");
  TfLiteStatus allocStatus = s_interpreter->AllocateTensors();
  if (allocStatus != kTfLiteOk) {
    Serial.println("[AF] ERROR: AllocateTensors() failed!");
    return false;
  }

  // Get input/output tensor pointers
  s_input = s_interpreter->input(0);
  s_output = s_interpreter->output(0);

  Serial.printf("[AF] Input tensor type: %d, dims: %d\n", (int)s_input->type,
                s_input->dims->size);
  Serial.printf("[AF] Output tensor type: %d, dims: %d\n", (int)s_output->type,
                s_output->dims->size);

  // Read quantization parameters
  s_inputScale = s_input->params.scale;
  s_inputZeroPoint = s_input->params.zero_point;
  s_outputScale = s_output->params.scale;
  s_outputZeroPoint = s_output->params.zero_point;

  Serial.printf("[AF] Model loaded OK. Arena used: %zu / %d bytes\n",
                s_interpreter->arena_used_bytes(), TENSOR_ARENA_SIZE);
  Serial.printf("[AF] Input:  shape=[%d,%d,%d] scale=%.6f zp=%ld\n",
                s_input->dims->data[0], s_input->dims->data[1],
                s_input->dims->data[2], s_inputScale, (long)s_inputZeroPoint);
  Serial.printf("[AF] Output: shape=[%d,%d] scale=%.6f zp=%ld\n",
                s_output->dims->data[0], s_output->dims->data[1], s_outputScale,
                (long)s_outputZeroPoint);

  Serial.println("[AF] AI inference engine ready.");
  return true;
}

// ============================================================
// Check if ready
// ============================================================
bool afInferenceIsReady() { return s_interpreter != nullptr; }

// ============================================================
// Run Inference
// ============================================================
bool afInferenceRun(const float *normalizedWindow, float *outProbability) {
  if (s_interpreter == nullptr) {
    if (!afInferenceInit()) {
      Serial.println("[AF] ERROR: Lazy init failed");
      return false;
    }
  }

  if (s_input == nullptr || s_output == nullptr) {
    Serial.println("[AF] ERROR: Input/Output tensors are NULL");
    return false;
  }

  int8_t *inputData = s_input->data.int8;
  Serial.printf(
      "[AF] Quantizing input... (ptr=%p, bytes=%zu, scale=%.6f, zp=%d)\n",
      inputData, s_input->bytes, s_inputScale, (int)s_inputZeroPoint);
  Serial.flush();

  if (inputData == nullptr || s_input->bytes < 2500) {
    Serial.println(
        "[AF] ERROR: Input data pointer is NULL or buffer too small!");
    return false;
  }

  // Quantize float → int8
  // formula: q = clamp(round(x / scale) + zero_point, -128, 127)
  float invScale = (s_inputScale != 0) ? (1.0f / s_inputScale) : 1.0f;

  for (int i = 0; i < 2500; i++) {
    float val = normalizedWindow[i];
    int32_t q = (int32_t)(val * invScale + 0.5f) + s_inputZeroPoint;
    if (q < -128)
      q = -128;
    if (q > 127)
      q = 127;
    inputData[i] = (int8_t)q;
  }

  // Run inference
  uint32_t startMs = millis();

  Serial.println("[AF] Invoking model...");
  Serial.flush();

  // Reset watchdog before long inference
  esp_task_wdt_reset();

  TfLiteStatus invokeStatus = s_interpreter->Invoke();

  // Reset watchdog after long inference
  esp_task_wdt_reset();

  s_lastInferenceMs = millis() - startMs;
  Serial.printf("[AF] Invoke done in %lu ms\n",
                (unsigned long)s_lastInferenceMs);
  Serial.flush();

  if (invokeStatus != kTfLiteOk) {
    Serial.printf("[AF] ERROR: Invoke() failed with status %d\n",
                  (int)invokeStatus);
    return false;
  }

  // Dequantize output → float
  // formula: x = (q - zero_point) * scale
  int8_t rawOutput = s_output->data.int8[0];
  *outProbability = (rawOutput - s_outputZeroPoint) * s_outputScale;

  return true;
}

// ============================================================
// Get last inference time
// ============================================================
uint32_t afInferenceTimeMs() { return s_lastInferenceMs; }

# AFDB Model Conversion & Deployment Plan

## 1. Model Location

**Source file:** `Model/AFDB_MODEL.tflite`
- File size: 73 KB (75,232 bytes)
- Format: TensorFlow Lite (already in `.tflite` format)

> **Note:** The model is already in TFLite format. The conversion pipeline below applies if the source model needs re-conversion from a different format (e.g., SavedModel, Keras H5, ONNX) or if INT8 quantization is required.

## 2. Model Analysis

Before conversion, determine the model characteristics:

```python
import tensorflow as tf

# Load and inspect the existing TFLite model
interpreter = tf.lite.Interpreter(model_path="Model/AFDB_MODEL.tflite")
interpreter.allocate_tensors()

input_details = interpreter.get_input_details()
output_details = interpreter.get_output_details()

print("Input:", input_details)
print("Output:", output_details)
```

**Expected model characteristics** [VERIFY after running above]:
| Property | Expected Value |
|----------|---------------|
| Input shape | `[1, N]` where N = number of RR intervals or ECG samples per window |
| Input dtype | float32 or int8 |
| Output shape | `[1, 2]` (binary: AF / Not-AF) or `[1, 1]` (probability) |
| Output dtype | float32 or int8 |
| Model size | 73 KB (current) |

## 3. Conversion Pipeline

### Step 1: Verify Source Model

If the `.tflite` file is already the final model, skip to Step 4. If a source format exists (SavedModel, H5, etc.), proceed:

```python
# From SavedModel directory
import tensorflow as tf

model = tf.saved_model.load("model/afdb_saved_model/")
# OR from Keras
model = tf.keras.models.load_model("model/afdb_model.h5")
```

### Step 2: Convert to TFLite (float32)

```python
import tensorflow as tf

# Load the source model
converter = tf.lite.TFLiteConverter.from_saved_model("model/afdb_saved_model/")
# OR: converter = tf.lite.TFLiteConverter.from_keras_model(model)

tflite_float_model = converter.convert()

with open("Model/afdb_model_float32.tflite", "wb") as f:
    f.write(tflite_float_model)

print(f"Float32 model size: {len(tflite_float_model)} bytes")
```

### Step 3: INT8 Quantization

```python
import tensorflow as tf
import numpy as np

converter = tf.lite.TFLiteConverter.from_saved_model("model/afdb_saved_model/")

# Enable full integer quantization
converter.optimizations = [tf.lite.Optimize.DEFAULT]

# Representative dataset for calibration (required for INT8)
def representative_data_gen():
    """Generate representative input samples for quantization calibration.
    Use actual RR-interval sequences from the AFDB dataset."""
    for _ in range(100):
        # [VERIFY] — adjust shape to match model input
        data = np.random.uniform(400, 1200, size=(1, 30)).astype(np.float32)
        yield [data]

converter.representative_dataset = representative_data_gen
converter.target_spec.supported_ops = [tf.lite.OpsSet.TFLITE_BUILTINS_INT8]
converter.inference_input_type = tf.int8
converter.inference_output_type = tf.int8

tflite_int8_model = converter.convert()

with open("Model/afdb_model_int8.tflite", "wb") as f:
    f.write(tflite_int8_model)

print(f"INT8 model size: {len(tflite_int8_model)} bytes")
```

### Step 4: Validate Converted Model

```python
import tensorflow as tf
import numpy as np

# Test float32 model
interp_float = tf.lite.Interpreter(model_path="Model/AFDB_MODEL.tflite")
interp_float.allocate_tensors()

input_details = interp_float.get_input_details()
output_details = interp_float.get_output_details()

# Generate test input [VERIFY shape]
test_input = np.random.uniform(400, 1200, size=input_details[0]['shape']).astype(np.float32)
interp_float.set_tensor(input_details[0]['index'], test_input)
interp_float.invoke()
float_output = interp_float.get_tensor(output_details[0]['index'])

print(f"Float32 output: {float_output}")

# If INT8 model exists, compare
interp_int8 = tf.lite.Interpreter(model_path="Model/afdb_model_int8.tflite")
interp_int8.allocate_tensors()
# ... (similar test with quantized input)
```

## 4. ESP32-S3 Deployment

### Memory Budget

| Resource | Available | Model Requirement |
|----------|-----------|------------------|
| Flash (model storage) | 8 MB total, ~4 MB app partition | 73 KB (current .tflite) — ✅ fits easily |
| SRAM (runtime tensors) | ~512 KB usable | [VERIFY] — depends on tensor arena size |
| PSRAM (if available) | 8 MB (board-dependent) | Can offload tensor arena to PSRAM |

### TFLite Micro Integration

**PlatformIO dependency (add to `platformio.ini`):**
```ini
lib_deps =
    ; ... existing deps ...
    https://github.com/tensorflow/tflite-micro.git  ; [VERIFY — may need specific tag]
```

> **Alternative:** Use the pre-built Arduino library `Arduino_TensorFlowLite` [VERIFY availability for ESP32-S3].

**Firmware integration sketch:**
```cpp
#include "tensorflow/lite/micro/all_ops_resolver.h"
#include "tensorflow/lite/micro/micro_interpreter.h"
#include "tensorflow/lite/schema/schema_generated.h"

// Model data (convert .tflite to C array via xxd)
// xxd -i Model/AFDB_MODEL.tflite > model_data.h
#include "model_data.h"

namespace {
  constexpr int kTensorArenaSize = 32 * 1024;  // [VERIFY — start with 32KB, increase if OOM]
  uint8_t tensor_arena[kTensorArenaSize];
  
  tflite::AllOpsResolver resolver;
  const tflite::Model* model = nullptr;
  tflite::MicroInterpreter* interpreter = nullptr;
  TfLiteTensor* input = nullptr;
  TfLiteTensor* output = nullptr;
}

void setupModel() {
  model = tflite::GetModel(afdb_model_tflite);
  if (model->version() != TFLITE_SCHEMA_VERSION) {
    Serial.println("Model schema mismatch!");
    return;
  }

  static tflite::MicroInterpreter static_interpreter(
      model, resolver, tensor_arena, kTensorArenaSize);
  interpreter = &static_interpreter;

  if (interpreter->AllocateTensors() != kTfLiteOk) {
    Serial.println("AllocateTensors failed — increase kTensorArenaSize");
    return;
  }

  input = interpreter->input(0);
  output = interpreter->output(0);

  Serial.printf("Model loaded. Input: [%d], Output: [%d]\n",
                input->dims->data[1], output->dims->data[1]);
}

bool runInference(const float* rr_intervals, int count) {
  // [VERIFY] — copy RR intervals into input tensor
  for (int i = 0; i < count && i < input->dims->data[1]; i++) {
    input->data.f[i] = rr_intervals[i];
  }

  if (interpreter->Invoke() != kTfLiteOk) {
    return false;
  }

  // [VERIFY] — interpret output
  float af_probability = output->data.f[0];
  return af_probability > 0.5f;  // threshold [VERIFY]
}
```

### Model Data Embedding

Convert `.tflite` to C header:
```bash
xxd -i Model/AFDB_MODEL.tflite > firmware/src/model_data.h
```

This produces a `const unsigned char` array (~73 KB) stored in Flash (PROGMEM).

## 5. Inference Trigger

### When to Run Inference
- **Trigger:** Every 30 heartbeat cycles (approximately every 20–30 seconds at rest)
- **Input:** Last N RR-intervals from the `HrvAnalyzer` circular buffer
- **Compute time budget:** < 100 ms per inference (must not block sampling loop)

### Integration with Existing Firmware

```
  loop()
    │
    ├── readFifoAndStream()     ← 100 Hz sampling (unchanged)
    │     └── panTompkinsUpdate() → RR interval
    │           └── g_hrv.submitRrInterval()
    │
    ├── HRV emit path (5s)      ← existing
    │     └── publishAiResult()
    │
    └── AF inference path (30 beats) ← NEW
          └── if (rrCount % 30 == 0)
                └── runInference(rr_buffer)
                      └── publishAfResult()
```

### Output JSON
```json
{"ts":1234567890,"af":{"detected":true,"confidence":0.87}}
```

## 6. Conversion Checklist

- [ ] AFDB source model identified and loadable — verify `Model/AFDB_MODEL.tflite` loads in Python TFLite Interpreter
- [ ] Input tensor shape and dtype documented — run `interpreter.get_input_details()`
- [ ] Output tensor shape and dtype documented — run `interpreter.get_output_details()`
- [ ] SavedModel export successful (if re-training from source) — or skip if `.tflite` is final
- [ ] TFLite float32 conversion successful, accuracy verified — compare output on 10 test vectors
- [ ] INT8 quantization successful — accuracy degradation < 2% [VERIFY threshold]
- [ ] INT8 `.tflite` file size < 100 KB — confirm fits in ESP32-S3 flash partition
- [ ] `xxd` conversion to C header successful — `model_data.h` generated
- [ ] TFLite Micro compiles on ESP32-S3 PlatformIO target — no linker errors
- [ ] `AllocateTensors()` succeeds with `kTensorArenaSize` ≤ 64 KB
- [ ] TFLite Micro runs inference on ESP32-S3 without OOM — test with synthetic RR data
- [ ] Inference latency < 100 ms — measure with `micros()` before/after `Invoke()`
- [ ] AF detection tested on known AFDB positive sample — model outputs AF=true
- [ ] AF detection tested on known normal sample — model outputs AF=false

import tensorflow as tf
import sys

model_path = r"E:\PROJECTS\MedTech_Device\Model\AFDB_int8.tflite"
interp = tf.lite.Interpreter(model_path=model_path)
interp.allocate_tensors()

# Get operator details
ops_details = interp._get_ops_details()
op_names = sorted(set(d["op_name"] for d in ops_details))
print("=== OPERATORS USED IN MODEL ===")
for op in op_names:
    count = sum(1 for d in ops_details if d["op_name"] == op)
    print("  {} (x{})".format(op, count))
print("\nTotal unique ops: {}".format(len(op_names)))
print("Total op nodes: {}".format(len(ops_details)))

# Input/Output details
inp = interp.get_input_details()[0]
out = interp.get_output_details()[0]
print("\nInput shape: {}".format(inp["shape"]))
print("Input dtype: {}".format(inp["dtype"]))
print("Input quant: {}".format(inp["quantization_parameters"]))
print("\nOutput shape: {}".format(out["shape"]))
print("Output dtype: {}".format(out["dtype"]))
print("Output quant: {}".format(out["quantization_parameters"]))

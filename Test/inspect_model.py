import tensorflow as tf

interp = tf.lite.Interpreter(model_path=r'E:\PROJECTS\MedTech_Device\Model\AFDB_int8.tflite')
interp.allocate_tensors()

print("=== All Tensor Details ===")
for i, detail in enumerate(interp.get_tensor_details()):
    name = detail["name"]
    shape = detail["shape"]
    dtype = detail["dtype"]
    print(f"{i:3d} | {name:50s} | shape={str(shape):20s} | dtype={dtype}")

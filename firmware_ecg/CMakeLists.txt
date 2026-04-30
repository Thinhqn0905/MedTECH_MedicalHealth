cmake_minimum_required(VERSION 3.16.0)

# Exclude esp-tflite-micro/esp-nn when building the legacy stream test.
if(DEFINED USE_LEGACY_TFLITE_ESP32 AND USE_LEGACY_TFLITE_ESP32)
	set(EXCLUDE_COMPONENTS esp-tflite-micro esp-nn)
endif()

include($ENV{IDF_PATH}/tools/cmake/project.cmake)
project(firmware_ecg)

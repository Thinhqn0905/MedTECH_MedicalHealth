# This file was automatically generated for projects
# without default 'CMakeLists.txt' file.

FILE(GLOB_RECURSE app_sources ${CMAKE_SOURCE_DIR}/src/*.*)

set(app_includes "")
if(DEFINED USE_LEGACY_TFLITE_ESP32 AND USE_LEGACY_TFLITE_ESP32)
	set(app_includes "${CMAKE_SOURCE_DIR}/components/esp-tflite-micro/third_party/flatbuffers/include")
endif()

idf_component_register(SRCS ${app_sources} INCLUDE_DIRS ${app_includes})

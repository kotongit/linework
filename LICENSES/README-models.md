# Bundled model and test image

**YOLOX-Nano**, person detection for auto-crop on Windows.
- File: `windows/Linework.App/Resources/Models/yolox_nano.onnx`, 3,659,407 bytes
- SHA-256: `c789161ed43c8269fcd4e67c67eeeb4e80c622da2eb296a20bc6007bd18a0b7d`
- Source: https://github.com/Megvii-BaseDetection/YOLOX/releases/tag/0.1.1rc0
- License: Apache-2.0, in `YOLOX-Apache-2.0.txt`. Copyright Megvii Inc.

**ONNX Runtime** runs it. MIT license, Copyright Microsoft Corporation, from NuGet package `Microsoft.ML.OnnxRuntime`.

**Test image** `shared/fixtures/sample-person.jpg`: NASA portrait of astronaut Eileen Collins, public domain as a work of NASA, taken from scikit-image's `astronaut.png`. Used only by tests.

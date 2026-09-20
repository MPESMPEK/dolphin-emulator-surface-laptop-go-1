# Surface Laptop Go 1 Configuration Preset

Copy these `.ini` files to your Dolphin `User/Config/` directory (or `%USERPROFILE%/Documents/Dolphin Emulator/Config/`):

* `Dolphin.ini`:
  - `GFXBackend = D3D12`
  - `CPUThread = True` (Dual-Core enabled)
  - `FullscreenResolution = 1536x1024` (Native 3:2 PixelSense)
  - `FastDiscSpeed = True`
* `GFX.ini`:
  - `ShaderCompilationMode = 2` (Hybrid Ubershaders)
  - `WaitForShadersBeforeStarting = True`
  - `ShaderCompilerThreads = 2`
  - `InternalResolution = 1` (1x Native)
  - `FastDepthCalc = True`
  - `SkipDuplicateXFBs = True`

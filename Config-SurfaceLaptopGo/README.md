# Surface Laptop Go 1 Configuration Presets

Preset konfigurasi siap pakai yang dioptimalkan untuk Intel Core i5 Ice Lake (Iris Plus / UHD Graphics) pada Surface Laptop Go 1.

### 1. Dolphin Emulator (GameCube & Wii)
Salin berkas ke `Dolphin-x64/User/Config/`:
* `Dolphin.ini`:
  - `GFXBackend = D3D12`
  - `CPUThread = True` (Dual-Core enabled)
  - `FullscreenResolution = 1536x1024` (Native 3:2 PixelSense)
  - `FastDiscSpeed = True`
* `GFX.ini`:
  - `ShaderCompilationMode = 2` (Hybrid Ubershaders)
  - `WaitForShadersBeforeStarting = True`
  - `InternalResolution = 1` (1x Native)

### 2. PCSX2 (Sony PlayStation 2)
Salin berkas ke `PCSX2-x64/inis/`:
* `PCSX2.ini`:
  - `Renderer = D3D12`
  - `AspectRatio = 3:2`
  - `UpscaleMultiplier = 1.0`
  - `EnableFastBoot = True`

### 3. DuckStation (Sony PlayStation 1)
Salin berkas `DuckStation-settings.ini` ke `DuckStation-x64/settings.ini`:
* `settings.ini`:
  - `Renderer = D3D12`
  - `AspectRatio = Custom (3:2)`
  - `PGXPEnable = true` (Koreksi geometri 3D presisi)
  - `PGXPTextureCorrection = true` (Koreksi tekstur goyang khas PS1)
  - `EnableFastBoot = true`

### 4. PPSSPP (Sony PlayStation Portable - PSP)
Preset dari `MPESMPEK/ppsspp-avx2`, salin ke `PPSSPP-x64/memstick/PSP/SYSTEM/ppsspp.ini`:
* `ppsspp.ini`:
  - `GraphicsBackend = Direct3D 11 / Vulkan`
  - `InternalResolution = 2 (2x Native PSP ~ 960x544)`
  - `FastMemoryAccess = True`
  - `SeparateSASThread = True`
  - `DisplayAspectRatio = 1`

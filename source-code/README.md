# Surface Gaming Suite — Master Source Code Repositories

Koleksi lengkap seluruh repositori kode sumber (*source code*) dari emulator dan komponen yang terintegrasi di dalam **Surface Gaming Suite (Surface Laptop Go 1 Edition)**:

---

## 📁 Daftar Repositori Kode Sumber Emulator

| No | Komponen / Emulator | Platform Konsol | Bahasa & Engine | Repositori GitHub Asal |
|---|---|---|---|---|
| **00** | **Surface Gaming Suite** | Multi-Emulator Hub & Touch Controller | C# (.NET 8 WPF) | [TouchJoystick/](TouchJoystick) (di dalam repo ini) |
| **01** | **Dolphin Emulator** | Nintendo GameCube & Wii | C++20 / Qt6 | [Source/](Source) (di dalam repo ini) |
| **02** | **PCSX2** | Sony PlayStation 2 | C++20 / Qt6 | [MPESMPEK/pcsx2-surface-laptop-go-1](https://github.com/MPESMPEK/pcsx2-surface-laptop-go-1) |
| **03** | **DuckStation** | Sony PlayStation 1 | C++20 / Qt6 | [MPESMPEK/ps1-emu-surface](https://github.com/MPESMPEK/ps1-emu-surface) |
| **04** | **PPSSPP** | Sony PlayStation Portable (PSP) | C++17 / Win32 / Vulkan | [MPESMPEK/ppsspp-avx2](https://github.com/MPESMPEK/ppsspp-avx2) |

---

## 🎯 Standarisasi Optimasi Perangkat Keras:
Semua kode sumber emulator di atas telah dikompilasi dan dikalibrasi dengan:
1. **Instruksi SIMD AVX2 + FMA (256-bit)** untuk efisiensi CPU maksimal pada Intel Core i5-1035G1 Ice Lake.
2. **Kustomisasi Rasio Aspek 3:2** (1536x1024) pas memenuhi layar PixelSense Surface Go tanpa gambar melar.
3. **Backend Grafis Modern (Direct3D 11/12 & Vulkan)** untuk menjaga suhu adem dan mencapai 60 FPS stabil.
4. **Mode Portabel Mandiri** (portable.txt / installed.txt) tanpa mengotori registry Windows.
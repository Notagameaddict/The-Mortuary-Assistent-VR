# Official OpenXR loader

The included PowerShell script obtains the loader from Khronos' official `OpenXR-SDK-Source` repository.

It:

1. clones the pinned official release tag;
2. configures `DYNAMIC_LOADER=ON`;
3. builds the x64 Release loader;
4. copies only `openxr_loader.dll`;
5. records its SHA-256.

The DLL is intentionally not included in this package.

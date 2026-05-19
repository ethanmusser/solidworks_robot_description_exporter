# Vendored SOLIDWORKS API DLLs

This directory exists so that CI builds (GitHub Actions, see [.github/workflows/release.yml](../../../.github/workflows/release.yml)) can produce the add-in DLL and the Inno Setup installer without requiring a full SOLIDWORKS installation on the runner.

## Contents

- `api/redist/SolidWorks.Interop.sldworks.dll`
- `api/redist/SolidWorks.Interop.swconst.dll`
- `api/redist/SolidWorks.Interop.swpublished.dll`
- `api/redist/redist.txt` (verbatim copy of the SOLIDWORKS-shipped redistribution
  statement that authorizes redistributing the above three DLLs)

These were copied unmodified from a stock **SOLIDWORKS 2024 (SP5)** install, specifically `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\`. The `redist.txt` in that same folder explicitly grants permission to redistribute the `SolidWorks.Interop.*` DLLs.

## How it is consumed

The project's [SW2RD.csproj](../../SW2RD.csproj) references SOLIDWORKS DLLs via `$(SolidWorksPath)\api\redist\...`. Local developers keep the value pointing at their SOLIDWORKS install (`C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS` by default; dev boxes with a side-by-side install, e.g. a path like `... SOLIDWORKS (2)`, should override via `/p:SolidWorksPath=...`). CI sets `/p:SolidWorksPath=<repo>\SW2RD\lib\sw-api` so the same HintPaths resolve to these vendored copies instead, no csproj changes needed.

## Updating

To refresh to a newer SOLIDWORKS major version, replace the three DLLs and `redist.txt` with files from the new install's `api/redist/` folder. Keep the layout (`lib/sw-api/api/redist/`) identical so the `$(SolidWorksPath)\api\redist\...` HintPaths continue to resolve.

# SolidWorks Robot Description Exporter

A SolidWorks add-in that exports assemblies as URDF and MJCF robot descriptions.

The exporter builds on [Stephen Brawner's SolidWorks to URDF Exporter](https://github.com/ros/solidworks_urdf_exporter) and adds native MJCF support, including separate visual / collision meshes, `<site>` tags, and per-link inertial-source selection.

## SolidWorks Version Requirements

The minimum required version of SolidWorks for use with this add-in is 2018 Service Pack 5. SolidWorks 2017 or earlier may work but is not tested.

## Usage

URDF-side usage follows the original [ROS Wiki](http://wiki.ros.org/sw_urdf_exporter) and associated [tutorials](http://wiki.ros.org/sw_urdf_exporter/Tutorials); the SW UI flow is unchanged. MJCF output is selected from the same "Export Robot Description" PropertyManager page via the output-format combo box.

## Development

1. Install Visual Studio 2017 or newer.
1. Install the `.NET desktop development` workload from `Tools > Get Tools and Features...`.
1. Install the [SolidWorks API tools](https://help.solidworks.com/2019/english/api/sldworksapiprogguide/GettingStarted/SolidWorks_API_Getting_Started_Overview.htm).
1. Launch Visual Studio with admin privileges (right-click -> `Run as Administrator`).
1. Open `solidworks_urdf_exporter/SW2RD.sln`.
1. Enable Debugging:
    1. Right click `SW2RD` in the Solution Explorer -> `Properties`.
    1. Click the `Debug` tab.
    1. Ensure `Configuration:` is set to `Debug`.
    1. Ensure `Start external program:` is pointing to the SolidWorks executable (e.g. `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe`).

Local development uses the SolidWorks API DLLs from the developer's SolidWorks install (`$(SolidWorksPath)` in [SW2RD/SW2RD.csproj](SW2RD/SW2RD.csproj)). The vendored copies under [SW2RD/lib/sw-api/](SW2RD/lib/sw-api/) are only consulted by CI; see that folder's README for details.

## Releasing

The Inno Setup installer is built automatically by
[.github/workflows/release.yml](.github/workflows/release.yml):

1. Publish a Release on GitHub (Releases tab -> Draft a new release). Pick or
   create the tag you want the release to point at and write release notes.
1. The workflow builds `SW2RD.dll` in `Release` configuration against the
   vendored SolidWorks API DLLs under
   [SW2RD/lib/sw-api/](SW2RD/lib/sw-api/), compiles
   [INSTALL/Install.iss](INSTALL/Install.iss) with Inno Setup, and attaches
   two assets to the triggering release:
   - `sw2rdSetup_<tag>.exe` - the installer itself
   - `sw2rdSetup_<tag>.exe.sha256` - SHA-256 checksum of the installer
1. The workflow also mints a [GitHub Artifact Attestation](https://docs.github.com/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)
   for the installer. The attestation cryptographically ties the .exe to this
   workflow file, the source commit, and the workflow run that produced it.
   See "Verifying downloads" below.
1. The pipeline can also be invoked manually from the Actions tab
   (`workflow_dispatch`) to smoke-test changes without cutting a real
   release; in that mode the installer + checksum are uploaded only as
   workflow artifacts, not attached to a release.

## Verifying downloads

The installer is not Authenticode-signed, so Windows SmartScreen and some antivirus / EDR products will warn about an "unknown publisher" the first time you run it. To verify that the `.exe` you downloaded was actually produced by this repository's CI pipeline (and not modified in transit, on the release page, or by anyone other than this workflow), use one of the two paths below.

### GitHub CLI

This verifies the cryptographic [build provenance attestation](https://docs.github.com/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations) minted by GitHub at build time. Requires the [`gh`](https://cli.github.com/) CLI but no additional tooling.

```powershell
gh attestation verify sw2rdSetup_<tag>.exe --repo ethanmusser/solidworks_robot_description_exporter
```

A successful run prints the workflow file (`.github/workflows/release.yml`), the source commit SHA, and the timestamp of the run that produced the file.

This proves that the installer was built by `.github/workflows/release.yml` in this repository, from a specific commit, by GitHub-hosted runners, at a specific time. This does NOT prove that the resulting executable is free of malware; only its provenance.

### SHA-256 checksum

Each release also publishes a `sw2rdSetup_<tag>.exe.sha256` file alongside the installer. To verify:

```powershell
Get-FileHash sw2rdSetup_<tag>.exe -Algorithm SHA256
# Compare the printed hash against the contents of sw2rdSetup_<tag>.exe.sha256
```

On Linux / WSL / macOS:

```bash
sha256sum -c sw2rdSetup_<tag>.exe.sha256
```

## Migrating from SW2URDF

The SolidWorks Robot Description Exporter (SW2RD) is intentionally a separate add-in from the original SolidWorks URDF Exporter (SW2URDF): the DLL filename, COM CLSID, install directory, and Inno Setup AppId are all distinct. Both add-ins can coexist on the same machine.

When SW2RD opens an existing SW2URDF assembly, it transparently reads the saved export configuration and writes the new attribute (`SW2RD Export Configuration (v1)`) on the next save. The old `URDF Export Configuration (v1.5)` attribute is preserved on the model so the SW2URDF exporter can still read it.

Supported import paths are based on the saved configuration schema, not the SW2URDF product version. SW2RD imports the latest SW2URDF XML configuration schema (`URDF Export Configuration (v1.5)`) and older configurations stored as v1.3-v1.5 DataContract XML or pre-v1.3 `SerialNode` XML.

## Converting mesh format from 3dxml to dae

Executing the following command will convert the format of the exported mesh from 3DXML to DAE, and rewrite the URDF, allowing you to display colored meshes in visualization tools like RViz:

```bash
pip3 install scikit-robot -U
convert-urdf-mesh <URDF_PATH> --output <OUTPUT_URDF_PATH>
```

### Troubleshooting

1. `AxImp.exe` error - Check the installation of the .NET tools. If there is no error, install the Windows 10 SDK.
1. `Resources.resx` error - Check if `solidworks_urdf_exporter/SW2RD/Properties/Resources.resx` exists and is empty. If empty, delete this file then right-click `SW2RD` in the Solution Explorer and select `Properties`. Navigate to the Resources tab and click the button to create a new file.

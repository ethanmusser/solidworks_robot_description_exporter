# SolidWorks to URDF Exporter

Authored and maintained by [Stephen Brawner](brawner@gmail.com). Past supporters include [PickNik Consulting](https://picknik.ai), Verb Surgical, Open Robotics, and Willow Garage. 

## Latest Release

**SolidWorks 2021**

https://github.com/ros/solidworks_urdf_exporter/releases/tag/1.6.1

**SolidWorks 2020**

https://github.com/ros/solidworks_urdf_exporter/releases/tag/1.6.0

**SolidWorks 2019 on 2018 SP 5**

https://github.com/ros/solidworks_urdf_exporter/releases/tag/1.5.1

## SolidWorks Version Requirements

1. The minimum required version of SolidWorks for use with this add-in is 2018 Service Pack 5. SolidWorks 2017 or earlier may work. See [this issue](https://github.com/ros/solidworks_urdf_exporter/issues/73).

## Usage

See the [ROS Wiki](http://wiki.ros.org/sw_urdf_exporter) and associated [tutorials](http://wiki.ros.org/sw_urdf_exporter/Tutorials).

## Development

1. Install Visual Studio 2017
1. Install .NET desktop development
    1. From Visual Studio: `Tools > Get Tools and Features...`
    1. Check `.NET desktop development` package
    1. Select `Modify`
1. Install the [SolidWorks API tools](https://help.solidworks.com/2019/english/api/sldworksapiprogguide/GettingStarted/SolidWorks_API_Getting_Started_Overview.htm)
1. Launch Visual Studio with admin privileges. Right click and select `Run as Administrator`
1. Open `sw2urdf/SW2URDF.sln`  
1. Enable Debugging
    1. Right click `SW2URDF` in the Solution Explorer
    1. Click the `Debug` Tab
    1. Ensure `Configuration:` is set to `Debug`
    1. Ensure `Start external program:` is pointing to the SolidWorks executable. For example `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe`

Local development continues to use the SolidWorks API DLLs from the developer's SolidWorks install (`$(SolidWorksPath)` in[SW2URDF.csproj](SW2URDF/SW2URDF.csproj)). The vendored copies under [SW2URDF/lib/sw-api/](SW2URDF/lib/sw-api/) are only consulted by CI; see that folder's README for details.

## Releasing

The Inno Setup installer is built automatically by
[.github/workflows/release.yml](.github/workflows/release.yml):

1. Publish a Release on GitHub (Releases tab -> Draft a new release). Pick or
   create the tag you want the release to point at and write release notes.
1. The workflow builds `SW2URDF.dll` in `Release` configuration against the
   vendored SolidWorks API DLLs under
   [SW2URDF/lib/sw-api/](SW2URDF/lib/sw-api/), compiles
   [INSTALL/Install.iss](INSTALL/Install.iss) with Inno Setup, and attaches
   two assets to the triggering release:
   - `sw2urdfSetup_<tag>.exe` - the installer itself
   - `sw2urdfSetup_<tag>.exe.sha256` - SHA-256 checksum of the installer
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
gh attestation verify sw2urdfSetup_<tag>.exe -R ethanmusser/solidworks_urdf_exporter
```

A successful run prints the workflow file (`.github/workflows/release.yml`), the source commit SHA, and the timestamp of the run that produced the file.

This proves that the installer was built by `.github/workflows/release.yml` in this repository, from a specific commit, by GitHub-hosted runners, at a specific time. This does NOT prove that the resulting executable is free of malware; only its provenance.

### SHA-256 checksum

Each release also publishes a `sw2urdfSetup_<tag>.exe.sha256` file alongside the installer. To verify:

```powershell
Get-FileHash sw2urdfSetup_<tag>.exe -Algorithm SHA256
# Compare the printed hash against the contents of sw2urdfSetup_<tag>.exe.sha256
```

On Linux / WSL / macOS:

```bash
sha256sum -c sw2urdfSetup_<tag>.exe.sha256
```

## Converting mesh format from 3dxml to dae

Executing the following command will convert the format of the exported mesh from 3DXML to DAE, and rewrite the URDF, allowing you to display colored meshes in visualization tools like RViz:

```bash
pip3 install scikit-robot -U
convert-urdf-mesh <URDF_PATH> --output <OUTPUT_URDF_PATH>
```

### Trouble Shooting

1. `AxImp.exe` error - Check the installation of the .Net Tools. If there is no error, install the Windows 10 SDK.
1. `Resourse.resx` error - Check if `sw2urdf/SW2URDF/Resources.resx` exists and is empty. If empty, delete this file then right click the `SW2URDF` in the Solution Explorer and select `Properties`. Navigate to the Resources tab and click the button to create a new file.

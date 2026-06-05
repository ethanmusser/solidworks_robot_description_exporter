# Testing SW2RD

This project programmatically runs the tests of the SW2RD project. The tests rely on the models provided in the examples directory, so any changes to those files may cause these tests to fail. Update any tests to reflect corresponding changes in the models.

## To Build

There is a test that checks that the git repo is not dirty, and all files have been committed. To pass that test, you need to commit all files, then rebuild the solution.

When you build the solution, you should see two successful builds, `SW2RD` and `TestRunner`.

## To Run

Run the `TestRunner` executable, it will locate the SW2RD Dll automatically.

```
TestRunner\bin\Debug\net452>TestRunner.exe
```

If you only want to run a subset of tests, the first argument of `TestRunner.exe` is an optional filter parameter. Any test with a fully qualified `NameSpace.ClassName.FunctionName` that contains the provided string will be run. For example, to run just the versioning tests.

```
TestRunner\bin\Debug\net452>TestRunner.exe TestVersioning
```

## Differential output comparison (`diff`)

`TestRunner diff <baselineDir> <candidateDir>` compares two already-produced
export trees and reports semantic differences. It does **not** open SolidWorks,
so the pre-merge workflow is:

1. Build the baseline (e.g. `main`) DLL, register it, and export the assemblies
   you care about into `out\baseline\<model>\`.
2. Build the candidate (your branch) DLL, register it, and export the same
   assemblies into `out\candidate\<model>\`.
3. Diff them:

```
TestRunner.exe diff "out\baseline\3_DOF_ARM" "out\candidate\3_DOF_ARM"
```

The comparison is numeric-aware: XML files (`.urdf`, `.xml`, ...) are compared
structurally with a `G9` / `1e-9` tolerance, ignoring XML comments (the per-build
version stamp lives there), attribute order, and `0` vs `0.0` noise. Mesh /
binary files (`.stl`, `.dae`, ...) are not content-compared - only their presence
and byte-size delta are reported, because tessellation carries float noise. All
other files (`.yaml`, `.launch`, `CMakeLists.txt`, ...) are compared as
whitespace-normalized text.

Exit codes: `0` = trees are equivalent, `2` = differences found, `3` = usage / IO
error. The diff report is printed to stdout.

## Golden fixtures (`TestExportGoldens`)

`TestExportGoldens` is the SW-free golden regression for the URDF / MJCF writers.
Each model lives under `solidworks_urdf_exporter/test-fixtures/golden/<model>/`
and carries one fixture per output format:

- `urdf.snapshot.json` / `mjcf.snapshot.json` - the frozen *writer input* (the
  canonical `KinematicTree` plus MJCF mesh-asset / site auxiliary data and writer
  options). This is the **ground truth** and only changes when the SolidWorks
  extraction layer changes.
- `expected.urdf` / `expected.mjcf.xml` - the blessed *writer output* (the real
  captured export), one per snapshot.

The four core models are `TOY_BLOCK`, `4_WHEELER`, `3_DOF_ARM`, and
`4_BAR_LINKAGE`. The test replays each `*.snapshot.json` through the writer
selected by its `Format` and compares to the matching `expected.*` with the same
numeric tolerance the `diff` tool uses. Adding a model (or a format to an
existing model) is just dropping a `snapshot` + `expected` pair in place - no
code change required.

### Regenerating fixtures

There are two independent levers, matching the two halves of a fixture:

- **Writer change (SW-free, fast).** When you intentionally change `URDFBuilder`
  / `MJCFBuilder` output, rebase every `expected.*` from its trusted
  `snapshot.json` in one pass with the bless fact (the snapshots, i.e. the SW
  extraction ground truth, are left untouched):

  ```
  set SW2RD_BLESS_GOLDEN=1
  TestRunner.exe BlessExpectedOutputs
  set SW2RD_BLESS_GOLDEN=
  ```

  Then eyeball the `git diff` of the fixtures - that diff *is* the semantic
  change your writer edit introduced.

- **Extraction change (requires SolidWorks).** When the SW-side extraction
  changes (inertia, frames, mesh grouping), recapture the snapshot *and* the
  expected output from a real export. Set `SW2RD_CAPTURE_GOLDEN=1` before
  launching SolidWorks, export the assembly normally, and the exporter writes a
  `<output>.snapshot.json` next to the produced `.urdf` / `.xml`. Copy that
  snapshot into the model folder as `urdf.snapshot.json` / `mjcf.snapshot.json`
  and the produced output as `expected.urdf` / `expected.mjcf.xml`.

After regenerating, eyeball the `git diff` of the fixtures before committing -
that diff *is* the semantic change your code introduced.

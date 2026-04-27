# MJCF Export Handoff

Context for a new agent (or contributor) picking up the MJCF work in this
repository.

## Reference material

- Plan (do **not** edit; it's the contract for the original work):
  [`plan.md`](./plan.md)
- Past chat transcript that produced the initial implementation:
  [`transcript.jsonl`](./transcript.jsonl)

## Objective

Add a MuJoCo MJCF export path to the existing SolidWorks-to-URDF Exporter
without altering the URDF flow. All eight todos in [`plan.md`](./plan.md) are
already implemented; what remains is verification (see "Open follow-ups").

## Hard guiding principle (USER instruction, repeat verbatim)

> **No silent defaults.** Every element emitted in the MJCF must be either
> (a) a direct translation of URDF-tree data the user already authored, or
> (b) something the user explicitly opted into via `MjcfOptionsDialog` or
> the per-link Sites checklist. The compiler/option/default blocks expose
> only the knobs the dialog surfaces.

If a future change considers auto-generating something (a default `<light>`,
a guessed contact group, materials translation, etc.), push back unless the
user has explicitly approved it. Past decisions in this thread followed this
rule (e.g., sensors / cameras / lights deferred, materials only when the user
set a Color).

## Decisions the user made (won't be obvious from code alone)

1. **Dedicated "Export MJCF..." button**, not a URDF/MJCF format toggle on
   the existing URDF flow. Reason: MJCF has features (actuators, equalities,
   excludes, integrator/timestep) that have no URDF analog and would muddy
   the URDF UI.
2. **Scope = "mvp_plus_sites"**: This PR ships joints, inertial, geoms,
   assets, equalities (mimic), contact excludes, actuators, and **only**
   sites for the visual-extras family. Sensors, cameras, lights, textures,
   and OBJ meshes are explicitly deferred. Sites were included now because
   they are the lowest-cost addition and a prerequisite for later sensor
   work.
3. **Sites are user-selected**, never auto-emitted. The user picks them per
   link from a `CheckedListBox` of available reference coord systems,
   excluding the one already used as the joint origin. They are persisted
   on `Link.SiteCoordSystemNames` and round-trip through CSV.
4. **Sites are valid on fixed-frame links**, even though those links produce
   no `<inertial>`/`<geom>`. The save/fill calls intentionally sit outside
   the `if (!Link.isFixedFrame)` block in
   `AssemblyExportFormExtension.cs`.
5. **`MjcfWriter` must stay SolidWorks-agnostic.** Anything needing the CAD
   runtime (coord-system resolution, mesh export) is pre-computed in
   `ExportHelper.ExportMjcf` / `ResolveAllSites` and passed in as plain data
   (`IDictionary<string,List<MjcfSite>>`, `IDictionary<string,string>`).
   This is what allows `TestMjcfWriter` to run without SolidWorks installed.

## Subtle implementation notes worth knowing

- **Mesh filename flow.** `ExportHelperMjcf` writes STLs into the MJCF
  package's `meshes/` dir using a basename only, populates a
  `linkName -> basename` dictionary, and updates each
  `Link.Visual.Geometry.Mesh.Filename`. `MjcfWriter` resolves the basename
  against `<compiler meshdir="...">`. If the dictionary is missing an
  entry, the writer falls back to `link.Visual.Geometry.Mesh.Filename`
  (also basename-only) and finally to `SanitizeName(link.Name) + ".STL"`.
- **Site name normalization.** `ExportHelperMjcf.StripComponentSuffix`
  strips the SolidWorks `"<component>"` suffix from coord-system names.
  Duplicate bare names are disambiguated by appending `_2`, `_3`, ... in
  `ResolveSitesForLink`. Final names are then run through
  `MjcfWriter.SanitizeName`.
- **Quaternion convention.** `MjcfWriter.RpyToQuat` converts URDF extrinsic
  XYZ RPY (= intrinsic ZYX) into MuJoCo's default `[w,x,y,z]` quat. Used
  for body, inertial, geom, and site rotations. Don't change this without
  also updating tests; both an identity and a pure-roll case are pinned in
  `TestMjcfWriter`.
- **Planar joints** are approximated as two orthogonal `<slide>` joints in
  the plane normal to the URDF axis, named `{joint}_x` and `{joint}_y`.
  MuJoCo has no first-class planar primitive.
- **Floating base** must be the *root*; MJCF requires the free joint at
  the top body. The writer handles `link.Joint.Type == "floating"`
  specially when `isRoot=true`.
- **Mimic equality polycoef.** Encoded as `"offset multiplier 0 0 0"` so
  MJCF evaluates `dependent = multiplier*source + offset`. This is verified
  in `TestMjcfWriter.MimicJoint_EmitsEqualityConstraint_WhenOptedIn`.
- **CSV round-trip.** `Link.SiteCoordSystemNames` is serialized as a single
  semicolon-joined string under the column "Site Coord Systems"
  (`URDFExport/CSV/ContextToColumns.cs`). This matches the existing
  `SWComponents` convention so the plain `StringDictionary` reader keeps
  working.
- **UI is built programmatically.** `AssemblyExportFormMjcf.cs` and
  `MjcfOptionsDialog.cs` create their controls in code rather than via a
  `.Designer.cs` so we don't have to touch the Designer-managed layout of
  the existing form. The Sites groupbox is positioned by shrinking
  `treeViewLinkProperties` to make room. If a Designer round-trip ever
  runs on `AssemblyExportForm.Designer.cs`, double-check no programmatic
  controls were stomped.

## Files actually changed in the implementation session

(For context only; the diff itself is the source of truth.)

- New: `SW2URDF/MJCF/MjcfWriter.cs`, `MjcfOptions.cs`, `MjcfPackage.cs`,
  `MjcfSite.cs`
- New: `SW2URDF/URDFExport/ExportHelperMjcf.cs` (partial of `ExportHelper`)
- New: `SW2URDF/UI/AssemblyExportFormMjcf.cs` (partial of
  `AssemblyExportForm`), `SW2URDF/UI/MjcfOptionsDialog.cs`
- New: `SW2URDF/Test/TestMjcfWriter.cs`
- Modified: `SW2URDF/URDF/Link.cs` (added `SiteCoordSystemNames`),
  `SW2URDF/URDFExport/CSV/ContextToColumns.cs`,
  `SW2URDF/UI/AssemblyExportForm.cs` (calls `InitializeMjcfUi`),
  `SW2URDF/UI/AssemblyExportFormExtension.cs` (Save/Fill site hooks),
  `SW2URDF/Test/TestExportHelper.cs` (`TestExportMjcf` theory),
  `SW2URDF/SW2URDF.csproj` (Compile entries)

## Open follow-ups (NOT done in the implementation session)

1. **Build verification on Windows.** The implementation session ran on
   Linux; the project targets `.NET Framework v4.5.2` and references
   SolidWorks interop DLLs. Nobody has actually compiled the new code.
   First task for the next session should be running an MSBuild on a
   Windows host with SolidWorks installed and triaging any compile errors.
2. **Manual MJCF validation** with the bundled assemblies (`3_DOF_ARM`,
   `4_WHEELER`, `ORIGINAL_3_DOF_ARM`): export, then open the produced
   `.xml` in `simulate` and / or `mujoco.MjModel.from_xml_path()`. The plan
   explicitly calls this out under "Testing".
3. **`TestExportMjcf` is integration-level**: it spins up SolidWorks via
   the `SWTestFixture`. Make sure CI (if any) still has the SW Test
   collection. The pure unit tests in `TestMjcfWriter` are deliberately
   *not* in the SW collection and *do not* take a `SWTestFixture`; they
   should run on any machine.
4. **Future PRs (out of scope here)**: sensors, cameras, lights, material /
   texture translation, OBJ mesh export. Each of these must come with its
   own UI opt-in to honor the no-silent-defaults rule.

## How sites flow end to end (one-liner per stage)

1. UI (`AssemblyExportFormMjcf.FillSitesForLink`) populates a checklist
   from `helper.GetRefCoordinateSystems()` minus the joint coord system.
2. `SaveSitesForLink` writes checked names to `link.SiteCoordSystemNames`.
3. `ExportHelperMjcf.ResolveSitesForLink` looks up each name via
   `GetCoordinateSystemTransform`, transforms global → link-local using
   the link's joint coord system, returns `MjcfSite` objects.
4. `MjcfWriter.WriteSites` consumes the per-link list and emits `<site>`
   under each `<body>`.

## Things to avoid

- Don't add anything to `<default>`, `<option>`, `<compiler>`, etc. that
  the user hasn't exposed in `MjcfOptionsDialog`.
- Don't auto-emit `<light>` / `<camera>` / `<sensor>` / `<keyframe>`.
- Don't add textures or materials beyond the `rgba` translation already in
  `MjcfWriter.WriteVisualColor` (which only fires when the user actually
  set a color).
- Don't change `Link.Clone()` semantics without re-checking
  `SiteCoordSystemNames` is deep-copied (it currently is, in
  `Link.SetSWComponents`).
- Don't reintroduce SolidWorks types in `MjcfWriter.cs`; the testability
  story depends on that file staying CAD-free.

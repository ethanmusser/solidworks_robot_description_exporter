# sw2rd-postprocess

A post-process CLI for robot descriptions exported by the SolidWorks Robot
Description Exporter (SW2RD). It rewrites the meshes of an **already-exported**
package (the XML plus the `meshes/*.STL` it references) for simulation. The
SolidWorks add-in is not involved and is not modified.

One tool, four operation subcommands:

| Subcommand | Purpose |
| --- | ----- |
| `prepare` | **Easy-mode**: make the model MuJoCo-loadable with no tuning - prune empty meshes and decimate only meshes over MuJoCo's 200,000-face limit. |
| `decimate` | Reduce mesh face counts with MeshLab Quadric Edge Collapse Decimation (`--ratio` or `--target-faces`). |
| `prune` | Remove empty (0-face) / unreadable meshes only; leave valid meshes untouched. |
| `decompose` | Replace concave collision meshes with their [CoACD](https://github.com/SarahWeiii/CoACD) convex-hull unions. |

`prepare` / `decimate` / `prune` share the MeshLab simplification backend;
`decompose` uses CoACD. Why MuJoCo cares: its STL decoder rejects meshes with
more than 200,000 faces (dense SolidWorks parts) or fewer than 1 (degenerate
sub-assembly exports), and it collides each mesh as its convex hull (so concave
collision shapes need decomposing).

## Scope

- **MJCF** (`.xml` / `.mjcf`): supported.
- **URDF** (`.urdf`): planned; not implemented yet.

## Install

Requires Python >= 3.12. All subcommands' dependencies are installed together.

With [uv](https://docs.astral.sh/uv/):

```bash
cd solidworks_urdf_exporter/tools/sw2rd-postprocess
uv sync
uv run sw2rd-postprocess --help
```

Or with pip:

```bash
pip install -e solidworks_urdf_exporter/tools/sw2rd-postprocess
```

## Usage

```bash
# Easy-mode: make an exported MJCF MuJoCo-loadable (prune empties + cap at 200k):
sw2rd-postprocess prepare "my_robot/mjcf/my_robot.xml"

# Decimate every mesh for a lighter / slower simulator:
sw2rd-postprocess decimate model.xml --ratio 0.3           # keep 30% of each mesh
sw2rd-postprocess decimate model.xml --target-faces 5000   # 5k faces per mesh

# Drop only the empty/unreadable meshes, never decimate a valid one:
sw2rd-postprocess prune model.xml

# Convex-decompose collision meshes (1 cm concavity tolerance):
sw2rd-postprocess decompose model.xml --all --threshold 0.01

# Targeting (decimate / prune / decompose): class or name selectors
sw2rd-postprocess decimate model.xml --collision --target-faces 2000
sw2rd-postprocess decompose model.xml --body gripper_link --match "*_collision"
```

## Output destinations

Shared by every subcommand; the three are mutually exclusive:

| Option | Result |
| --- | ----- |
| (default) | Produced meshes (`M_simplified.STL` / `M_hull{i}.STL`) land beside the originals in `meshes/`; the XML is written to `<input>.<op>.<ext>`. Originals are kept. |
| `-o/--output-file PATH` | As above, but the XML goes to `PATH` (its `meshdir` is corrected so the meshes still resolve). |
| `--output-dir DIR` | A **self-contained** tree: `DIR/<name>` + `DIR/meshes/` with ALL referenced meshes (changed ones rewritten, unchanged ones copied). Portable. |
| `--in-place` | **Overwrite** the original meshes and XML; pruned/orphaned mesh files are deleted. The flag is itself the opt-in - there is no separate confirmation. |

## Key parameters

| Flag | Subcommands | Meaning |
| --- | --- | ----- |
| `--max-faces N` | `prepare`, `decimate` | Per-mesh face cap (default 200000, MuJoCo's limit). `0` disables it. In `prepare` it is also the filter (only over-cap meshes are reduced). |
| `--ratio R` / `--target-faces N` | `decimate` | Decimate every targeted mesh to this fraction / absolute count (exactly one required). |
| `--quality-threshold`, `--boundary-weight`, `--no-*` | `decimate` | MeshLab QECD tuning. |
| `--threshold`, `--max-hulls`, `--normalized`, `--seed`, ... | `decompose` | CoACD parameters (threshold in meters by default; SW2RD meshes are SI). |
| `--visual` / `--collision` / `--all` / `--geom` / `--mesh` / `--body` / `--match` | targeting | Restrict which geoms are processed. |

See `sw2rd-postprocess <subcommand> --help` for the full list. Meshes that fail
to load or process are logged and left unchanged; empty/unreadable meshes have
their geoms pruned so the output stays loadable.

"""MJCF backend: rewrite mesh geoms via MuJoCo MjSpec for every operation.

Two operations share the read / rewrite / write plumbing here:

* :func:`simplify_mjcf` decimates (or, with ``prune_only``, just prunes) the
  targeted mesh geoms. A reduced mesh repoints (or overwrites, depending on the
  output mode) its geom; an empty / unreadable mesh has its geom pruned.
* :func:`decompose_mjcf` replaces each targeted collision geom referencing mesh
  ``M`` with N convex-hull geoms (``M_hull0`` ... ) and their assets.

Both consume an :class:`~sw2rd_postprocess.output.OutputPlan` that decides where
produced meshes go, whether unchanged meshes are copied (self-contained tree),
and whether orphaned source files are deleted (in-place). ``MjSpec.to_xml()``
compiles the model - decoding every declared mesh and enforcing MuJoCo's
"1 to 200,000 faces" limit - which is why orphaned and degenerate meshes are
removed and over-limit meshes are brought under the cap before serialization.
"""

from __future__ import annotations

import shutil
from pathlib import Path
from typing import TYPE_CHECKING

import mujoco as mj
from loguru import logger

from sw2rd_postprocess.mesh_decompose import DecompositionSettings, decompose_mesh
from sw2rd_postprocess.mesh_simplify import (
    MeshStatus,
    SimplificationSettings,
    simplify_mesh,
)
from sw2rd_postprocess.targeting import (
    COLLISION_NAME_SUFFIX,
    select_decompose_targets,
    select_simplify_targets,
)

if TYPE_CHECKING:
    from sw2rd_postprocess.output import OutputPlan

SIMPLIFIED_SUFFIX = "_simplified"


def _referenced_meshnames(spec: mj.MjSpec) -> set[str]:
    return {
        g.meshname
        for g in spec.geoms
        if g.type == mj.mjtGeom.mjGEOM_MESH and g.meshname
    }


def _delete_orphaned_assets(
    spec: mj.MjSpec,
    mesh_by_name: dict,
    candidate_sources: set[str],
) -> set[str]:
    """Delete candidate mesh assets no geom references; return their filenames.

    The returned basenames are the on-disk files that became orphaned, so the
    caller can delete them in in-place mode.
    """
    referenced = _referenced_meshnames(spec)
    removed_files: set[str] = set()
    for src in candidate_sources:
        if src not in referenced and src in mesh_by_name:
            asset = mesh_by_name.pop(src)
            removed_files.add(asset.file)
            spec.delete(asset)
            logger.info("Removed unreferenced mesh asset {}.", src)
    return removed_files


def _serialize(spec: mj.MjSpec) -> str:
    """Serialize the spec to XML (this compiles + decodes every declared mesh)."""
    try:
        return spec.to_xml()
    except ValueError as exc:
        logger.error(
            "MuJoCo could not serialize the model: {}\n"
            "A declared mesh likely still exceeds MuJoCo's 1..200000-face limit "
            "(e.g. a mesh referenced only by a non-targeted geom). Re-run without "
            "class/name targeting, or lower --max-faces.",
            exc,
        )
        raise


def _copy_unchanged_meshes(
    spec: mj.MjSpec, plan: OutputPlan, produced: set[str]
) -> None:
    """Copy every still-referenced mesh we did not write into the output tree."""
    for mesh in spec.meshes:
        if mesh.file in produced:
            continue  # we already wrote this one into the output tree
        src = plan.source_mesh_dir / mesh.file
        dst = plan.output_mesh_dir / mesh.file
        if src.resolve() == dst.resolve():
            continue
        if src.is_file():
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
        else:
            logger.warning(
                "Referenced mesh {} not found; output tree may be incomplete.", src
            )


def _finalize(
    spec: mj.MjSpec,
    plan: OutputPlan,
    produced: set[str],
    removed_source_files: set[str],
) -> None:
    """Copy/delete meshes per the plan and write the XML with a valid meshdir.

    ``to_xml()`` compiles the model, resolving each mesh ``file`` against
    ``modelfiledir`` + ``meshdir``. ``modelfiledir`` defaults to the input XML's
    directory, which is wrong once the XML is relocated (``--output-dir`` / ``-o``
    elsewhere). Pointing ``modelfiledir`` at the OUTPUT XML's directory makes the
    relative ``meshdir`` resolve to the output mesh dir; ``modelfiledir`` is a
    resolution base only and is NOT serialized into the XML.
    """
    plan.output_xml.parent.mkdir(parents=True, exist_ok=True)
    plan.output_mesh_dir.mkdir(parents=True, exist_ok=True)

    if plan.copy_unchanged:
        _copy_unchanged_meshes(spec, plan, produced)

    spec.modelfiledir = str(plan.output_xml.parent)
    spec.meshdir = plan.meshdir
    plan.output_xml.write_text(_serialize(spec), encoding="utf-8")

    if plan.delete_orphans:
        for fname in removed_source_files:
            target = plan.source_mesh_dir / fname
            try:
                target.unlink(missing_ok=True)
                logger.info("Deleted orphaned mesh file {}.", target)
            except OSError as exc:
                logger.warning("Could not delete {}: {}", target, exc)


# --------------------------------------------------------------------------- #
# simplify / prune
# --------------------------------------------------------------------------- #


def _simplified_dest(plan: OutputPlan, asset: object) -> Path:
    """Where the decimated copy of ``asset`` should be written."""
    file = Path(asset.file)
    name = (
        f"{file.stem}{SIMPLIFIED_SUFFIX}{file.suffix}"
        if plan.keep_suffix
        else file.name
    )
    return plan.output_mesh_dir / name


def _ensure_simplified_asset(
    spec: mj.MjSpec,
    mesh_by_name: dict,
    base_mesh_name: str,
    simplified_path: Path,
) -> str:
    """Add (or reuse) a ``<mesh>`` asset for the simplified file; return its name."""
    name = f"{base_mesh_name}{SIMPLIFIED_SUFFIX}"
    if name not in mesh_by_name:
        mesh = spec.add_mesh()
        mesh.name = name
        mesh.file = Path(simplified_path).name
        mesh_by_name[name] = mesh
    return name


def _simplify_source(
    spec: mj.MjSpec,
    plan: OutputPlan,
    settings: SimplificationSettings,
    mesh_by_name: dict,
    asset: object,
    produced: set[str],
    simplified_name_by_source: dict[str, str],
) -> MeshStatus:
    """Decimate one source mesh once; record the simplified asset / file name."""
    dest = _simplified_dest(plan, asset)
    outcome = simplify_mesh(
        plan.source_mesh_dir / asset.file, settings, output_path=dest
    )
    if outcome.status == MeshStatus.SIMPLIFIED:
        produced.add(Path(outcome.path).name)
        if plan.keep_suffix:
            simplified_name_by_source[asset.name] = _ensure_simplified_asset(
                spec, mesh_by_name, asset.name, outcome.path
            )
        else:
            # Overwrite-in-place style: the asset keeps its name and basename;
            # only its file content changed.
            asset.file = Path(outcome.path).name
    return outcome.status


def simplify_mjcf(
    input_path: Path,
    plan: OutputPlan,
    settings: SimplificationSettings,
    *,
    visual: bool = False,
    collision: bool = False,
    geoms: set[str] | None = None,
    meshes: set[str] | None = None,
    bodies: set[str] | None = None,
    match: str | None = None,
) -> int:
    """Simplify (and prune) targeted mesh geoms; return the number simplified."""
    input_path = Path(input_path)
    spec = mj.MjSpec.from_file(str(input_path))
    mesh_by_name = {m.name: m for m in spec.meshes}
    plan.output_mesh_dir.mkdir(parents=True, exist_ok=True)

    targets = select_simplify_targets(
        spec,
        visual=visual,
        collision=collision,
        geoms=geoms,
        meshes=meshes,
        bodies=bodies,
        match=match,
    )
    if not targets:
        logger.warning(
            "No matching geoms; output will equal input. (Default considers "
            "every mesh geom; use --visual / --collision / --geom / --mesh / "
            "--body / --match to narrow.)"
        )

    status_by_source: dict[str, MeshStatus] = {}
    simplified_name_by_source: dict[str, str] = {}
    produced: set[str] = set()
    replaced_sources: set[str] = set()
    pruned_sources: set[str] = set()
    geoms_to_prune: list = []
    simplified = 0

    for geom in targets:
        source_name = geom.meshname
        asset = mesh_by_name.get(source_name)
        if asset is None:
            logger.warning(
                "geom {} references unknown mesh {}; skipping.", geom.name, source_name
            )
            continue

        if source_name not in status_by_source:
            status_by_source[source_name] = _simplify_source(
                spec,
                plan,
                settings,
                mesh_by_name,
                asset,
                produced,
                simplified_name_by_source,
            )

        status = status_by_source[source_name]
        if status == MeshStatus.SIMPLIFIED:
            if plan.keep_suffix:
                geom.meshname = simplified_name_by_source[source_name]
                replaced_sources.add(source_name)
            simplified += 1
        elif status in (MeshStatus.EMPTY, MeshStatus.UNREADABLE):
            geoms_to_prune.append(geom)
            pruned_sources.add(source_name)
        # WITHIN_BUDGET / FAILED: keep the original mesh as-is.

    for geom in geoms_to_prune:
        logger.info("Pruned geom {} (degenerate/unreadable mesh).", geom.name)
        spec.delete(geom)

    removed = _delete_orphaned_assets(
        spec, mesh_by_name, replaced_sources | pruned_sources
    )
    _finalize(spec, plan, produced, removed)
    logger.info(
        "Wrote {} ({} simplified, {} pruned).",
        plan.output_xml,
        simplified,
        len(geoms_to_prune),
    )
    return simplified


# --------------------------------------------------------------------------- #
# decompose
# --------------------------------------------------------------------------- #


def _collision_group(geom: object) -> int | None:
    """Group number the collision default assigns, so added geoms can match it."""
    try:
        return int(geom.classname.geom.group)
    except Exception:  # noqa: BLE001 - missing/oddly-shaped default; caller defaults
        return None


def _ensure_hull_mesh(
    spec: mj.MjSpec,
    mesh_by_name: dict,
    base_mesh_name: str,
    index: int,
    hull_path: Path,
) -> str:
    """Add (or reuse) a ``<mesh>`` asset for a hull file; return its asset name."""
    name = f"{base_mesh_name}_hull{index}"
    if name not in mesh_by_name:
        mesh = spec.add_mesh()
        mesh.name = name
        mesh.file = Path(hull_path).name
        mesh_by_name[name] = mesh
    return name


def _apply_hulls(
    spec: mj.MjSpec,
    geom: object,
    asset: object,
    hull_paths: list[Path],
    mesh_by_name: dict,
) -> None:
    """Rewrite ``geom`` to the first hull and add geoms for the rest."""
    base_geom_name = geom.name or f"{asset.name}{COLLISION_NAME_SUFFIX}"
    base_mesh_name = asset.name
    body = geom.parent
    coll_group = _collision_group(geom)

    mesh0 = _ensure_hull_mesh(spec, mesh_by_name, base_mesh_name, 0, hull_paths[0])
    geom.meshname = mesh0
    geom.name = f"{base_geom_name}_hull0"

    for i, hull_path in enumerate(hull_paths[1:], start=1):
        mesh_name = _ensure_hull_mesh(spec, mesh_by_name, base_mesh_name, i, hull_path)
        new_geom = body.add_geom()
        new_geom.type = mj.mjtGeom.mjGEOM_MESH
        new_geom.meshname = mesh_name
        new_geom.name = f"{base_geom_name}_hull{i}"
        new_geom.classname = geom.classname
        new_geom.pos = geom.pos
        new_geom.quat = geom.quat
        if coll_group is not None:
            new_geom.group = coll_group


def decompose_mjcf(
    input_path: Path,
    plan: OutputPlan,
    settings: DecompositionSettings,
    *,
    all_collision: bool = False,
    geoms: set[str] | None = None,
    meshes: set[str] | None = None,
    bodies: set[str] | None = None,
    match: str | None = None,
) -> int:
    """Decompose targeted collision geoms; return the number decomposed."""
    input_path = Path(input_path)
    spec = mj.MjSpec.from_file(str(input_path))
    mesh_by_name = {m.name: m for m in spec.meshes}
    plan.output_mesh_dir.mkdir(parents=True, exist_ok=True)

    targets = select_decompose_targets(
        spec,
        all_collision=all_collision,
        geoms=geoms,
        meshes=meshes,
        bodies=bodies,
        match=match,
    )
    if not targets:
        logger.warning(
            "No matching geoms to decompose; output will equal input. (Default "
            "targets class='collision' geoms; use --all / --geom / --mesh / "
            "--body / --match to widen.)"
        )

    produced: set[str] = set()
    replaced_sources: set[str] = set()
    decomposed = 0
    for geom in targets:
        source_name = geom.meshname
        asset = mesh_by_name.get(source_name)
        if asset is None:
            logger.warning(
                "geom {} references unknown mesh {}; skipping.",
                geom.name,
                source_name,
            )
            continue
        hull_paths = decompose_mesh(
            plan.source_mesh_dir / asset.file,
            settings,
            output_dir=plan.output_mesh_dir,
        )
        if not hull_paths:
            continue  # core already warned; leave the original geom in place
        for hull_path in hull_paths:
            produced.add(Path(hull_path).name)
        _apply_hulls(spec, geom, asset, hull_paths, mesh_by_name)
        replaced_sources.add(source_name)
        decomposed += 1

    removed = _delete_orphaned_assets(spec, mesh_by_name, replaced_sources)
    _finalize(spec, plan, produced, removed)
    logger.info("Wrote {} ({} geom(s) decomposed).", plan.output_xml, decomposed)
    return decomposed

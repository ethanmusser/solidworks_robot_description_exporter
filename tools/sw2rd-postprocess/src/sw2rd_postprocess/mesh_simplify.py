"""Format-agnostic mesh-simplification core (MeshLab QECD via pymeshlab).

Loads a single triangle mesh, runs Quadric Edge Collapse Decimation, and writes
one binary STL of the decimated result. The MJCF/URDF backends call
:func:`simplify_mesh` and rewrite their XML to reference the returned file.

The headline use case is making meshes loadable by MuJoCo, whose STL decoder
rejects meshes with more than ``MUJOCO_MAX_FACES`` triangles (and fewer than 1).
The default ("prepare") settings only touch meshes that exceed that limit and
bring them at or below it; ``ratio`` / ``target_faces`` give explicit control
when you want to decimate every selected mesh.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from pathlib import Path

from loguru import logger

# pymeshlab is imported lazily inside the functions that need it, so importing
# this module (e.g. for SimplificationSettings) does not require the heavy native
# MeshLab stack to be installed.

# MuJoCo's STL decoder hard-limits a mesh to this many faces.
MUJOCO_MAX_FACES = 200_000

# QECD cannot meaningfully reduce below a tetrahedron.
_MIN_FACES = 4

# pymeshlab renamed this filter across releases; modern name first, legacy second.
_QECD_FILTERS = (
    "meshing_decimation_quadric_edge_collapse",
    "simplification_quadric_edge_collapse_decimation",
)


class MeshStatus(Enum):
    """Outcome of attempting to simplify one mesh.

    * ``SIMPLIFIED`` - a reduced STL was written (``path`` set); repoint geom(s).
    * ``WITHIN_BUDGET`` - already small enough; leave untouched.
    * ``EMPTY`` - zero triangles (degenerate export); the backend prunes the
      referencing geom(s) since MuJoCo cannot load a 0-face mesh.
    * ``UNREADABLE`` - the file could not be loaded; pruned like ``EMPTY``.
    * ``FAILED`` - loaded but decimation/saving failed; the original is kept.
    """

    SIMPLIFIED = "simplified"
    WITHIN_BUDGET = "within_budget"
    EMPTY = "empty"
    UNREADABLE = "unreadable"
    FAILED = "failed"


@dataclass
class SimplifyOutcome:
    status: MeshStatus
    path: Path | None = None


@dataclass
class SimplificationSettings:
    """MeshLab Quadric Edge Collapse Decimation tuning parameters.

    The per-mesh decimation target is resolved by :func:`resolve_target_faces`
    from three knobs, in priority order:

    * ``target_faces`` (> 0): decimate every selected mesh to this absolute count.
    * ``ratio`` (set): decimate every selected mesh to this fraction of its faces.
    * otherwise: **cap mode** - only meshes with more than ``max_faces`` faces are
      decimated, each down to ``max_faces``.

    ``max_faces`` (default :data:`MUJOCO_MAX_FACES`) also acts as a hard upper
    bound in the ``target_faces`` / ``ratio`` modes. Set it to ``None`` to disable
    both the cap and the cap-mode filter.

    ``prune_only`` skips decimation entirely: readable, non-empty meshes are left
    as-is and only empty / unreadable meshes are reported for pruning. The
    remaining fields map onto MeshLab's filter options.
    """

    max_faces: int | None = MUJOCO_MAX_FACES
    target_faces: int = 0
    ratio: float | None = None
    prune_only: bool = False
    quality_threshold: float = 0.3
    preserve_boundary: bool = True
    boundary_weight: float = 1.0
    preserve_normal: bool = True
    preserve_topology: bool = True
    optimal_placement: bool = True
    planar_quadric: bool = True
    auto_clean: bool = True


def resolve_target_faces(
    face_count: int, settings: SimplificationSettings
) -> int | None:
    """Return the absolute target face count for a mesh, or None to skip it."""
    if settings.target_faces and settings.target_faces > 0:
        target = settings.target_faces
    elif settings.ratio is not None:
        target = round(face_count * settings.ratio)
    elif settings.max_faces is not None:
        if face_count <= settings.max_faces:
            return None  # already MuJoCo-loadable; nothing to do
        target = settings.max_faces
    else:
        return None  # no target requested and no cap to enforce

    if settings.max_faces is not None:
        target = min(target, settings.max_faces)
    target = max(target, _MIN_FACES)

    if target >= face_count:
        return None  # not a reduction
    return target


def _run_decimation(
    mesh_set: object, target_faces: int, settings: SimplificationSettings
) -> None:
    """Apply QECD to the current mesh, tolerant of pymeshlab filter renames."""
    import pymeshlab

    params = {
        "targetfacenum": target_faces,
        "targetperc": 0.0,
        "qualitythr": settings.quality_threshold,
        "preserveboundary": settings.preserve_boundary,
        "boundaryweight": settings.boundary_weight,
        "preservenormal": settings.preserve_normal,
        "preservetopology": settings.preserve_topology,
        "optimalplacement": settings.optimal_placement,
        "planarquadric": settings.planar_quadric,
        "autoclean": settings.auto_clean,
    }
    last_exc: Exception | None = None
    for name in _QECD_FILTERS:
        try:
            mesh_set.apply_filter(name, **params)
        except pymeshlab.PyMeshLabException as exc:
            message = str(exc).lower()
            if (
                "does not exist" in message
                or "unknown" in message
                or "not found" in message
            ):
                last_exc = exc
                continue
            raise
        else:
            return
    if last_exc is not None:
        raise last_exc


def simplify_mesh(
    mesh_path: Path,
    settings: SimplificationSettings,
    output_path: Path | None = None,
) -> SimplifyOutcome:
    """Decimate ``mesh_path`` and write the result.

    ``output_path`` is where a ``SIMPLIFIED`` result is written; when ``None`` it
    defaults to a sibling ``<stem>_simplified.STL``. Every non-``SIMPLIFIED``
    status leaves the file system untouched.
    """
    import pymeshlab

    mesh_path = Path(mesh_path)
    mesh_set = pymeshlab.MeshSet()
    try:
        mesh_set.load_new_mesh(str(mesh_path))
    except Exception as exc:  # noqa: BLE001 - degrade gracefully on any loader error
        logger.warning("Could not load mesh {}: {}", mesh_path, exc)
        return SimplifyOutcome(MeshStatus.UNREADABLE)

    before = mesh_set.current_mesh().face_number()
    if before == 0:
        logger.warning(
            "Mesh {} has 0 triangles (degenerate export); MuJoCo cannot load it.",
            mesh_path,
        )
        return SimplifyOutcome(MeshStatus.EMPTY)

    # prune_only: the mesh is readable and non-empty, which is all we check -
    # never decimate (treat it as within budget).
    target = None if settings.prune_only else resolve_target_faces(before, settings)
    if target is None:
        logger.info(
            "{}: {} faces within budget; leaving unchanged.", mesh_path.name, before
        )
        return SimplifyOutcome(MeshStatus.WITHIN_BUDGET)

    try:
        _run_decimation(mesh_set, target, settings)
    except Exception as exc:  # noqa: BLE001 - a MeshLab failure must not abort the run
        logger.warning("MeshLab decimation failed on {}: {}", mesh_path, exc)
        return SimplifyOutcome(MeshStatus.FAILED)

    after = mesh_set.current_mesh().face_number()
    out_path = (
        Path(output_path)
        if output_path is not None
        else mesh_path.with_name(f"{mesh_path.stem}_simplified{mesh_path.suffix}")
    )
    out_path.parent.mkdir(parents=True, exist_ok=True)
    try:
        mesh_set.save_current_mesh(str(out_path), binary=True)
    except Exception as exc:  # noqa: BLE001 - degrade gracefully on any writer error
        logger.warning("Could not write simplified mesh {}: {}", out_path, exc)
        return SimplifyOutcome(MeshStatus.FAILED)

    logger.info(
        "{}: {} -> {} faces (target {}).", mesh_path.name, before, after, target
    )
    return SimplifyOutcome(MeshStatus.SIMPLIFIED, out_path)

"""Format-agnostic convex-decomposition core (CoACD via coacd + trimesh).

Loads a single triangle mesh, runs CoACD on it, and writes one binary STL per
resulting convex hull. The MJCF/URDF backends call :func:`decompose_mesh` and
rewrite their XML to reference the returned hull files.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from loguru import logger

# coacd and trimesh are imported lazily inside the functions that need them, so
# importing this module (e.g. for DecompositionSettings) does not require the
# heavy native CoACD/trimesh stack to be installed.


@dataclass
class DecompositionSettings:
    """CoACD tuning parameters.

    ``threshold`` is the concavity stop criterion. With ``real_metric`` enabled
    (the default) it is expressed directly in the mesh's own units - meters, for
    SW2RD exports, which are SI - rather than CoACD's normalized [0, 1] scale.
    Only ``threshold`` and ``max_convex_hull`` are commonly tuned; the rest are
    sensible CoACD defaults exposed for completeness.
    """

    threshold: float = 0.01
    max_convex_hull: int = -1
    real_metric: bool = True
    preprocess_mode: str = "auto"
    preprocess_resolution: int = 50
    resolution: int = 2000
    mcts_nodes: int = 20
    mcts_iterations: int = 150
    mcts_max_depth: int = 3
    pca: bool = False
    merge: bool = True
    seed: int = 0


def _run_coacd(mesh: object, settings: DecompositionSettings) -> list:
    """Call ``coacd.run_coacd`` defensively across coacd versions."""
    import coacd

    kwargs = {
        "threshold": settings.threshold,
        "max_convex_hull": settings.max_convex_hull,
        "preprocess_mode": settings.preprocess_mode,
        "preprocess_resolution": settings.preprocess_resolution,
        "resolution": settings.resolution,
        "mcts_nodes": settings.mcts_nodes,
        "mcts_iterations": settings.mcts_iterations,
        "mcts_max_depth": settings.mcts_max_depth,
        "pca": settings.pca,
        "merge": settings.merge,
        "seed": settings.seed,
    }
    if settings.real_metric:
        try:
            return coacd.run_coacd(mesh, real_metric=True, **kwargs)
        except TypeError:
            logger.warning(
                "Installed coacd does not support 'real_metric'; falling back to "
                "the normalized threshold {}. Upgrade coacd for meters-based "
                "thresholds.",
                settings.threshold,
            )
    return coacd.run_coacd(mesh, **kwargs)


def decompose_mesh(
    mesh_path: Path,
    settings: DecompositionSettings,
    output_dir: Path | None = None,
) -> list[Path]:
    """Decompose ``mesh_path`` into convex hulls.

    Hull files (``<stem>_hull{i}.STL``) are written into ``output_dir`` when
    given, else beside the source mesh. Returns the list of hull paths (at least
    one on success). On any failure (unreadable / empty mesh, CoACD error) logs a
    warning and returns an empty list so the caller can leave the original geom.
    """
    import coacd
    import trimesh

    mesh_path = Path(mesh_path)
    try:
        tm = trimesh.load(mesh_path, force="mesh")
    except Exception as exc:  # noqa: BLE001 - degrade gracefully on any loader error
        logger.warning("Could not load mesh {}: {}", mesh_path, exc)
        return []

    if tm.vertices is None or len(tm.vertices) == 0 or len(tm.faces) == 0:
        logger.warning("Mesh {} has no triangles; skipping.", mesh_path)
        return []

    try:
        coacd_mesh = coacd.Mesh(tm.vertices, tm.faces)
        parts = _run_coacd(coacd_mesh, settings)
    except Exception as exc:  # noqa: BLE001 - a CoACD failure must not abort the run
        logger.warning("CoACD failed on {}: {}", mesh_path, exc)
        return []

    if not parts:
        logger.warning("CoACD produced no parts for {}; skipping.", mesh_path)
        return []

    target_dir = Path(output_dir) if output_dir is not None else mesh_path.parent
    target_dir.mkdir(parents=True, exist_ok=True)
    hull_paths: list[Path] = []
    for i, part in enumerate(parts):
        vertices, faces = part
        hull = trimesh.Trimesh(vertices=vertices, faces=faces)
        hull_path = target_dir / f"{mesh_path.stem}_hull{i}{mesh_path.suffix}"
        hull.export(hull_path, file_type="stl")
        hull_paths.append(hull_path)

    logger.info("{} -> {} convex hull(s)", mesh_path.name, len(hull_paths))
    return hull_paths

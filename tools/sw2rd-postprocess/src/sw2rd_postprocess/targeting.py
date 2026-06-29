"""Shared geom-selection logic for every MJCF post-process operation.

SW2RD-exported MJCF marks visual geometry with ``class="visual"`` and collision
geometry with ``class="collision"`` (with ``_visual`` / ``_collision`` name
suffixes as a fallback). The name-based selectors (``geoms`` / ``meshes`` /
``bodies`` / ``match``) target any mesh geom by name so the operations also work
on non-SW2RD MJCF. The two entry points differ only in their default scope:

* :func:`select_simplify_targets` defaults to **every** mesh geom (decimation /
  pruning is safe for both classes).
* :func:`select_decompose_targets` defaults to **collision** geoms only (visual
  render meshes must stay concave).
"""

from __future__ import annotations

import fnmatch
import re

import mujoco as mj

VISUAL_CLASS = "visual"
COLLISION_CLASS = "collision"
VISUAL_NAME_SUFFIX = "_visual"
COLLISION_NAME_SUFFIX = "_collision"


def _classname(geom: object) -> str:
    """Return the geom's default-class name, or '' if it has none."""
    cls = getattr(geom, "classname", None)
    return getattr(cls, "name", "") or ""


def geom_class(geom: object) -> str:
    """Classify a mesh geom as 'visual', 'collision', or '' (by class/name)."""
    cls = _classname(geom)
    if cls in (VISUAL_CLASS, COLLISION_CLASS):
        return cls
    name = geom.name or ""
    if name.endswith(COLLISION_NAME_SUFFIX):
        return COLLISION_CLASS
    if name.endswith(VISUAL_NAME_SUFFIX):
        return VISUAL_CLASS
    return ""


def is_collision_geom(geom: object) -> bool:
    """Return True for SW2RD collision mesh geoms (by class or name suffix)."""
    if geom.type != mj.mjtGeom.mjGEOM_MESH or not geom.meshname:
        return False
    return geom_class(geom) == COLLISION_CLASS


def _name_matches(name: str, pattern: str) -> bool:
    """Match a name against a pattern as a regex OR a shell glob."""
    if not name:
        return False
    if fnmatch.fnmatch(name, pattern):
        return True
    try:
        return re.search(pattern, name) is not None
    except re.error:
        return False


def _mesh_geoms(spec: mj.MjSpec) -> list:
    return [g for g in spec.geoms if g.type == mj.mjtGeom.mjGEOM_MESH and g.meshname]


def _name_selected(
    geom: object,
    geoms: set[str],
    meshes: set[str],
    bodies: set[str],
    match: str | None,
) -> bool:
    return (
        geom.name in geoms
        or geom.meshname in meshes
        or (geom.parent is not None and geom.parent.name in bodies)
        or (match is not None and _name_matches(geom.name, match))
    )


def select_simplify_targets(
    spec: mj.MjSpec,
    *,
    visual: bool = False,
    collision: bool = False,
    geoms: set[str] | None = None,
    meshes: set[str] | None = None,
    bodies: set[str] | None = None,
    match: str | None = None,
) -> list:
    """Pick the mesh geoms to consider for simplification / pruning.

    With no selector at all this returns every mesh geom. The name-based
    selectors take precedence and return their union, independent of class.
    Otherwise ``visual`` / ``collision`` restrict to the matching class(es).
    """
    geoms = geoms or set()
    meshes = meshes or set()
    bodies = bodies or set()
    mesh_geoms = _mesh_geoms(spec)

    if geoms or meshes or bodies or match:
        return [
            g for g in mesh_geoms if _name_selected(g, geoms, meshes, bodies, match)
        ]

    if visual or collision:
        wanted = set()
        if visual:
            wanted.add(VISUAL_CLASS)
        if collision:
            wanted.add(COLLISION_CLASS)
        return [g for g in mesh_geoms if geom_class(g) in wanted]

    return mesh_geoms


def select_decompose_targets(
    spec: mj.MjSpec,
    *,
    all_collision: bool = False,
    geoms: set[str] | None = None,
    meshes: set[str] | None = None,
    bodies: set[str] | None = None,
    match: str | None = None,
) -> list:
    """Pick the mesh geoms to convex-decompose.

    With no explicit selector (and without ``all_collision``) this returns the
    SW2RD collision geoms only. Explicit selectors target any mesh geom by name
    (the union of all provided criteria), independent of class.
    """
    geoms = geoms or set()
    meshes = meshes or set()
    bodies = bodies or set()
    mesh_geoms = _mesh_geoms(spec)

    if all_collision or not (geoms or meshes or bodies or match):
        return [g for g in mesh_geoms if is_collision_geom(g)]

    return [g for g in mesh_geoms if _name_selected(g, geoms, meshes, bodies, match)]

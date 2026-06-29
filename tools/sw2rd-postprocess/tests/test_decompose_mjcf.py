"""Structural tests for the MJCF convex-decomposition rewrite (CoACD monkeypatched)."""

from __future__ import annotations

import shutil
from pathlib import Path

import pytest

mj = pytest.importorskip("mujoco")
trimesh = pytest.importorskip("trimesh")

from sw2rd_postprocess.mesh_decompose import DecompositionSettings  # noqa: E402
from sw2rd_postprocess.mjcf import decompose_mjcf  # noqa: E402
from sw2rd_postprocess.output import resolve_output_plan  # noqa: E402

MJCF_TEMPLATE = """<mujoco model="fixture">
  <compiler meshdir="../meshes/" />
  <default>
    <default class="visual">
      <geom contype="0" conaffinity="0" group="2" />
    </default>
    <default class="collision">
      <geom group="3" rgba="0.5 0.6 0.7 0.4" />
    </default>
  </default>
  <asset>
    <mesh name="box" file="box.STL" />
  </asset>
  <worldbody>
    <body name="part">
      <geom name="part_visual" type="mesh" mesh="box" class="visual" />
      <geom name="part_visual_collision" type="mesh" mesh="box" class="collision" />
    </body>
  </worldbody>
</mujoco>
"""


def _fake_decompose(mesh_path, settings, output_dir=None):
    """Stand in for core.decompose_mesh: write two hull copies, return them."""
    _ = settings
    target = Path(output_dir) if output_dir is not None else mesh_path.parent
    target.mkdir(parents=True, exist_ok=True)
    hulls = []
    for i in range(2):
        hull = target / f"{mesh_path.stem}_hull{i}{mesh_path.suffix}"
        shutil.copy(mesh_path, hull)
        hulls.append(hull)
    return hulls


def _build_fixture(tmp_path: Path) -> Path:
    meshes = tmp_path / "meshes"
    mjcf_dir = tmp_path / "mjcf"
    meshes.mkdir()
    mjcf_dir.mkdir()
    trimesh.creation.box(extents=(0.1, 0.1, 0.1)).export(meshes / "box.STL")
    mjcf_path = mjcf_dir / "model.xml"
    mjcf_path.write_text(MJCF_TEMPLATE, encoding="utf-8")
    return mjcf_path


def _sibling_plan(mjcf_path: Path):
    return resolve_output_plan(mjcf_path, "../meshes/", op_label="decomposed")


def test_collision_geom_replaced_by_hulls(tmp_path, monkeypatch):
    monkeypatch.setattr("sw2rd_postprocess.mjcf.decompose_mesh", _fake_decompose)
    mjcf_path = _build_fixture(tmp_path)
    plan = _sibling_plan(mjcf_path)

    count = decompose_mjcf(mjcf_path, plan, DecompositionSettings(), all_collision=True)
    assert count == 1

    spec = mj.MjSpec.from_file(str(plan.output_xml))
    geom_names = {g.name for g in spec.geoms}
    mesh_names = {m.name for m in spec.meshes}

    assert "part_visual_collision" not in geom_names
    assert {"part_visual_collision_hull0", "part_visual_collision_hull1"} <= geom_names
    # The original mesh remains (still referenced by the visual geom).
    assert {"box", "box_hull0", "box_hull1"} <= mesh_names
    assert next(g for g in spec.geoms if g.name == "part_visual").meshname == "box"
    assert (tmp_path / "meshes" / "box_hull0.STL").is_file()
    assert (tmp_path / "meshes" / "box_hull1.STL").is_file()


def test_default_targets_collision_only(tmp_path, monkeypatch):
    monkeypatch.setattr("sw2rd_postprocess.mjcf.decompose_mesh", _fake_decompose)
    mjcf_path = _build_fixture(tmp_path)
    plan = _sibling_plan(mjcf_path)

    count = decompose_mjcf(mjcf_path, plan, DecompositionSettings())
    assert count == 1  # the lone collision geom, not the visual one

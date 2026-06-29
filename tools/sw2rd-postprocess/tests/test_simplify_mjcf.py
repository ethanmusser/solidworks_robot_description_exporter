"""Structural tests for the MJCF simplify/prune rewrite (decimation monkeypatched)."""

from __future__ import annotations

import shutil
from pathlib import Path

import pytest

mj = pytest.importorskip("mujoco")
trimesh = pytest.importorskip("trimesh")

from sw2rd_postprocess.mesh_simplify import (  # noqa: E402
    MeshStatus,
    SimplificationSettings,
    SimplifyOutcome,
)
from sw2rd_postprocess.mjcf import simplify_mjcf  # noqa: E402
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


def _fake_simplify(mesh_path, settings, output_path=None):
    """Stand in for core.simplify_mesh: write the decimated copy, return it."""
    _ = settings
    out = Path(output_path)
    out.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy(mesh_path, out)
    return SimplifyOutcome(MeshStatus.SIMPLIFIED, out)


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
    return resolve_output_plan(mjcf_path, "../meshes/", op_label="simplified")


def test_all_geoms_repointed_and_orphan_removed(tmp_path, monkeypatch):
    monkeypatch.setattr("sw2rd_postprocess.mjcf.simplify_mesh", _fake_simplify)
    mjcf_path = _build_fixture(tmp_path)
    plan = _sibling_plan(mjcf_path)

    count = simplify_mjcf(mjcf_path, plan, SimplificationSettings())
    assert count == 2  # visual + collision share one mesh

    spec = mj.MjSpec.from_file(str(plan.output_xml))
    assert {m.name for m in spec.meshes} == {"box_simplified"}
    for name in ("part_visual", "part_visual_collision"):
        g = next(gg for gg in spec.geoms if gg.name == name)
        assert g.meshname == "box_simplified"
    assert (tmp_path / "meshes" / "box_simplified.STL").is_file()
    assert (tmp_path / "meshes" / "box.STL").is_file()  # original left on disk
    spec.to_xml()


def test_collision_only_keeps_shared_original(tmp_path, monkeypatch):
    monkeypatch.setattr("sw2rd_postprocess.mjcf.simplify_mesh", _fake_simplify)
    mjcf_path = _build_fixture(tmp_path)
    plan = _sibling_plan(mjcf_path)

    count = simplify_mjcf(mjcf_path, plan, SimplificationSettings(), collision=True)
    assert count == 1

    spec = mj.MjSpec.from_file(str(plan.output_xml))
    assert {m.name for m in spec.meshes} == {"box", "box_simplified"}
    assert next(g for g in spec.geoms if g.name == "part_visual").meshname == "box"
    coll = next(g for g in spec.geoms if g.name == "part_visual_collision")
    assert coll.meshname == "box_simplified"


def test_within_budget_changes_nothing(tmp_path, monkeypatch):
    monkeypatch.setattr(
        "sw2rd_postprocess.mjcf.simplify_mesh",
        lambda *_a, **_k: SimplifyOutcome(MeshStatus.WITHIN_BUDGET),
    )
    mjcf_path = _build_fixture(tmp_path)
    plan = _sibling_plan(mjcf_path)

    count = simplify_mjcf(mjcf_path, plan, SimplificationSettings())
    assert count == 0
    spec = mj.MjSpec.from_file(str(plan.output_xml))
    assert {m.name for m in spec.meshes} == {"box"}


def test_empty_mesh_geoms_are_pruned(tmp_path, monkeypatch):
    monkeypatch.setattr(
        "sw2rd_postprocess.mjcf.simplify_mesh",
        lambda *_a, **_k: SimplifyOutcome(MeshStatus.EMPTY),
    )
    mjcf_path = _build_fixture(tmp_path)
    plan = _sibling_plan(mjcf_path)

    count = simplify_mjcf(mjcf_path, plan, SimplificationSettings())
    assert count == 0

    spec = mj.MjSpec.from_file(str(plan.output_xml))
    assert [g.name for g in spec.geoms] == []
    assert [m.name for m in spec.meshes] == []
    spec.to_xml()

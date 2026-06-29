"""End-to-end output-destination behavior for the simplify backend.

Uses the real pymeshlab decimation path (importorskip), so these validate that
each output mode produces a MuJoCo-loadable result with the right files on disk
and a correct ``meshdir``.
"""

from __future__ import annotations

from pathlib import Path

import pytest

mj = pytest.importorskip("mujoco")
trimesh = pytest.importorskip("trimesh")
pytest.importorskip("pymeshlab")

from sw2rd_postprocess.mesh_simplify import SimplificationSettings  # noqa: E402
from sw2rd_postprocess.mjcf import simplify_mjcf  # noqa: E402
from sw2rd_postprocess.output import resolve_output_plan  # noqa: E402

MJCF_TEMPLATE = """<mujoco model="fixture">
  <compiler meshdir="../meshes/" />
  <asset>
    <mesh name="box" file="box.STL" />
    <mesh name="sphere" file="sphere.STL" />
    <mesh name="empty" file="empty.STL" />
  </asset>
  <worldbody>
    <body name="part">
      <geom name="box_geom" type="mesh" mesh="box" />
      <geom name="sphere_geom" type="mesh" mesh="sphere" />
      <geom name="empty_geom" type="mesh" mesh="empty" />
    </body>
  </worldbody>
</mujoco>
"""


def _build_fixture(tmp_path: Path) -> Path:
    meshes = tmp_path / "meshes"
    mjcf_dir = tmp_path / "mjcf"
    meshes.mkdir()
    mjcf_dir.mkdir()
    trimesh.creation.box(extents=(0.1, 0.1, 0.1)).export(meshes / "box.STL")
    trimesh.creation.icosphere(subdivisions=2, radius=0.1).export(meshes / "sphere.STL")
    # 80-byte zero header + uint32(0) == a valid, empty binary STL.
    (meshes / "empty.STL").write_bytes(b"\x00" * 80 + b"\x00\x00\x00\x00")
    mjcf_path = mjcf_dir / "model.xml"
    mjcf_path.write_text(MJCF_TEMPLATE, encoding="utf-8")
    return mjcf_path


def _geom_names(xml: Path) -> set[str]:
    return {g.name for g in mj.MjSpec.from_file(str(xml)).geoms}


def test_output_dir_is_self_contained(tmp_path):
    mjcf_path = _build_fixture(tmp_path)
    out_root = tmp_path / "out"
    plan = resolve_output_plan(
        mjcf_path, "../meshes/", op_label="simplified", output_dir=out_root
    )

    # target-faces=20: box (12 faces) within budget -> copied; sphere -> decimated.
    simplify_mjcf(mjcf_path, plan, SimplificationSettings(target_faces=20))

    assert (out_root / "model.xml").is_file()
    assert (out_root / "meshes" / "box.STL").is_file()  # copied verbatim
    assert (out_root / "meshes" / "sphere.STL").is_file()  # decimated copy
    assert not (out_root / "meshes" / "empty.STL").exists()  # pruned, not copied

    spec = mj.MjSpec.from_file(str(out_root / "model.xml"))
    assert spec.meshdir.rstrip("/") == "meshes"
    assert "empty_geom" not in {g.name for g in spec.geoms}
    spec.to_xml()  # compiles -> proves all referenced meshes resolve


def test_in_place_overwrites_and_deletes_pruned(tmp_path):
    mjcf_path = _build_fixture(tmp_path)
    plan = resolve_output_plan(
        mjcf_path, "../meshes/", op_label="simplified", in_place=True
    )

    simplify_mjcf(mjcf_path, plan, SimplificationSettings(target_faces=20))

    meshes = tmp_path / "meshes"
    assert (meshes / "box.STL").is_file()
    assert (meshes / "sphere.STL").is_file()
    assert not (meshes / "empty.STL").exists()  # pruned file deleted in place
    # No suffix files created in place.
    assert not (meshes / "sphere_simplified.STL").exists()
    assert "empty_geom" not in _geom_names(mjcf_path)
    mj.MjSpec.from_file(str(mjcf_path)).to_xml()


def test_output_file_relocates_xml_with_valid_meshdir(tmp_path):
    mjcf_path = _build_fixture(tmp_path)
    out_xml = tmp_path / "elsewhere" / "model.simplified.xml"
    plan = resolve_output_plan(
        mjcf_path, "../meshes/", op_label="simplified", output_file=out_xml
    )

    # prepare-style defaults: box/sphere within the 200k budget, empty pruned.
    simplify_mjcf(mjcf_path, plan, SimplificationSettings())

    assert out_xml.is_file()
    # Meshes stay in the original dir; the relocated XML must still find them.
    assert (tmp_path / "meshes" / "box.STL").is_file()
    assert not (tmp_path / "elsewhere" / "meshes").exists()
    spec = mj.MjSpec.from_file(str(out_xml))
    assert "empty_geom" not in {g.name for g in spec.geoms}
    spec.to_xml()  # would raise if meshdir did not resolve from elsewhere/


def test_prune_only_keeps_valid_meshes(tmp_path):
    mjcf_path = _build_fixture(tmp_path)
    plan = resolve_output_plan(mjcf_path, "../meshes/", op_label="simplified")

    count = simplify_mjcf(mjcf_path, plan, SimplificationSettings(prune_only=True))
    assert count == 0  # prune-only never simplifies

    spec = mj.MjSpec.from_file(str(plan.output_xml))
    assert {m.name for m in spec.meshes} == {"box", "sphere"}
    assert {g.name for g in spec.geoms} == {"box_geom", "sphere_geom"}
    assert not (tmp_path / "meshes" / "sphere_simplified.STL").exists()
    spec.to_xml()

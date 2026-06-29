"""Geom-selection semantics for the simplify and decompose target pickers."""

from __future__ import annotations

import pytest

mj = pytest.importorskip("mujoco")

from sw2rd_postprocess.targeting import (  # noqa: E402
    select_decompose_targets,
    select_simplify_targets,
)

# Note: mujoco only populates geom .name when a spec is loaded from a file, so the
# fixture is written to disk and read back via from_file (from_string leaves names
# blank). Mesh files are never opened here (no compile), so they need not exist.

MJCF = """<mujoco model="fixture">
  <compiler meshdir="meshes/" />
  <default>
    <default class="visual"><geom contype="0" conaffinity="0" group="2" /></default>
    <default class="collision"><geom group="3" /></default>
  </default>
  <asset>
    <mesh name="m_a" file="a.STL" />
    <mesh name="m_b" file="b.STL" />
  </asset>
  <worldbody>
    <body name="link_a">
      <geom name="link_a_visual" type="mesh" mesh="m_a" class="visual" />
      <geom name="link_a_visual_collision" type="mesh" mesh="m_a" class="collision" />
    </body>
    <body name="link_b">
      <geom name="link_b_visual" type="mesh" mesh="m_b" class="visual" />
    </body>
  </worldbody>
</mujoco>
"""


@pytest.fixture
def spec(tmp_path):
    path = tmp_path / "model.xml"
    path.write_text(MJCF, encoding="utf-8")
    return mj.MjSpec.from_file(str(path))


def test_simplify_default_is_all_mesh_geoms(spec):
    names = {g.name for g in select_simplify_targets(spec)}
    assert names == {"link_a_visual", "link_a_visual_collision", "link_b_visual"}


def test_simplify_class_restriction(spec):
    names = {g.name for g in select_simplify_targets(spec, collision=True)}
    assert names == {"link_a_visual_collision"}


def test_simplify_name_selectors_take_precedence(spec):
    names = {
        g.name for g in select_simplify_targets(spec, collision=True, bodies={"link_b"})
    }
    # A name selector overrides the class restriction (union of name criteria).
    assert names == {"link_b_visual"}


def test_decompose_default_is_collision_only(spec):
    names = {g.name for g in select_decompose_targets(spec)}
    assert names == {"link_a_visual_collision"}


def test_decompose_match_selector(spec):
    names = {g.name for g in select_decompose_targets(spec, match="*_visual")}
    assert names == {"link_a_visual", "link_b_visual"}

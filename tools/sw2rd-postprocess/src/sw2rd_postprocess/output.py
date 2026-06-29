"""Output-destination planning shared by every post-process operation.

A single :class:`OutputPlan`, resolved once from the CLI choices, tells a format
backend everything it needs about *where* results go, so the backends stay free
of destination logic. Three modes:

* ``SIBLING`` (default) - produced meshes land in the original ``meshes/`` dir
  (with an operation suffix, e.g. ``M_simplified.STL``); the XML is written to a
  sibling ``<input>.<op>.<ext>`` (or an explicit ``--output-file``). The output
  ``meshdir`` is recomputed relative to the XML location so it stays valid even
  when the XML is written elsewhere.
* ``OUTPUT_DIR`` - a self-contained tree at ``DIR``: ``DIR/<input.name>`` plus
  ``DIR/<meshleaf>/`` holding ALL referenced meshes (changed ones rewritten,
  unchanged ones copied verbatim). Uses original basenames (no suffix needed in
  a fresh dir) and a repointed ``meshdir``.
* ``IN_PLACE`` - overwrite the original meshes (under their original names) and
  the input XML; orphaned / pruned mesh files are deleted. ``--in-place`` is the
  explicit opt-in; there is no separate confirmation flag.
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from enum import Enum
from pathlib import Path


class OutputMode(Enum):
    SIBLING = "sibling"
    OUTPUT_DIR = "output_dir"
    IN_PLACE = "in_place"


@dataclass
class OutputPlan:
    """Resolved destination for one post-process run.

    ``keep_suffix`` is True only in ``SIBLING`` mode, where a changed mesh must
    coexist with its original in the same directory and so gets the operation
    suffix; the other modes write under the original basename. ``copy_unchanged``
    (``OUTPUT_DIR``) makes the tree self-contained. ``delete_orphans``
    (``IN_PLACE``) physically removes mesh files that nothing references anymore.
    """

    mode: OutputMode
    source_mesh_dir: Path
    output_xml: Path
    output_mesh_dir: Path
    meshdir: str
    keep_suffix: bool
    copy_unchanged: bool
    delete_orphans: bool


def _relative_meshdir(target: Path, start: Path) -> str:
    """POSIX relative path from ``start`` to ``target`` with a trailing slash."""
    rel = Path(os.path.relpath(target, start)).as_posix()
    if not rel.endswith("/"):
        rel += "/"
    return rel


def resolve_output_plan(
    input_xml: Path,
    meshdir_attr: str,
    *,
    op_label: str,
    in_place: bool = False,
    output_dir: Path | None = None,
    output_file: Path | None = None,
) -> OutputPlan:
    """Build the :class:`OutputPlan` for the chosen mode.

    ``op_label`` ('simplified' / 'decomposed') names the default sibling XML and
    the per-mesh suffix. The three mode inputs are mutually exclusive; the CLI
    enforces that, but ``in_place`` wins, then ``output_dir``, then a sibling
    plan (default or ``output_file``).
    """
    input_xml = Path(input_xml).resolve()
    source_mesh_dir = (input_xml.parent / (meshdir_attr or "")).resolve()
    mesh_leaf = source_mesh_dir.name or "meshes"

    if in_place:
        return OutputPlan(
            mode=OutputMode.IN_PLACE,
            source_mesh_dir=source_mesh_dir,
            output_xml=input_xml,
            output_mesh_dir=source_mesh_dir,
            meshdir=meshdir_attr or "",
            keep_suffix=False,
            copy_unchanged=False,
            delete_orphans=True,
        )

    if output_dir is not None:
        output_dir = Path(output_dir).resolve()
        out_xml = output_dir / input_xml.name
        out_mesh_dir = output_dir / mesh_leaf
        return OutputPlan(
            mode=OutputMode.OUTPUT_DIR,
            source_mesh_dir=source_mesh_dir,
            output_xml=out_xml,
            output_mesh_dir=out_mesh_dir,
            meshdir=_relative_meshdir(out_mesh_dir, out_xml.parent),
            keep_suffix=False,
            copy_unchanged=True,
            delete_orphans=False,
        )

    out_xml = (
        Path(output_file).resolve()
        if output_file is not None
        else input_xml.with_name(f"{input_xml.stem}.{op_label}{input_xml.suffix}")
    )
    return OutputPlan(
        mode=OutputMode.SIBLING,
        source_mesh_dir=source_mesh_dir,
        output_xml=out_xml,
        output_mesh_dir=source_mesh_dir,
        meshdir=_relative_meshdir(source_mesh_dir, out_xml.parent),
        keep_suffix=True,
        copy_unchanged=False,
        delete_orphans=False,
    )

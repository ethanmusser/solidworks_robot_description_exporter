"""Post-process a SW2RD-exported robot description (simplify / prune / decompose)."""

from sw2rd_postprocess.mesh_decompose import DecompositionSettings, decompose_mesh
from sw2rd_postprocess.mesh_simplify import (
    MUJOCO_MAX_FACES,
    MeshStatus,
    SimplificationSettings,
    SimplifyOutcome,
    resolve_target_faces,
    simplify_mesh,
)
from sw2rd_postprocess.output import OutputMode, OutputPlan, resolve_output_plan

__all__ = [
    "MUJOCO_MAX_FACES",
    "DecompositionSettings",
    "MeshStatus",
    "OutputMode",
    "OutputPlan",
    "SimplificationSettings",
    "SimplifyOutcome",
    "decompose_mesh",
    "resolve_output_plan",
    "resolve_target_faces",
    "simplify_mesh",
]

"""URDF backend (future work).

Scaffolded but not implemented. URDF mesh geometry is referenced explicitly
(``<link><visual|collision><geometry><mesh filename="package://.../X.STL"/>
</geometry></...></link>``), so the planned implementation will reuse the core
mesh ops, parse/serialize with ``lxml``, target ``<visual>`` / ``<collision>``
mesh references, honor the same :class:`~sw2rd_postprocess.output.OutputPlan`,
and repoint each reference at its produced file.
"""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from pathlib import Path

    from sw2rd_postprocess.mesh_decompose import DecompositionSettings
    from sw2rd_postprocess.mesh_simplify import SimplificationSettings
    from sw2rd_postprocess.output import OutputPlan

_MSG = (
    "URDF post-processing is not implemented yet. Only MJCF (.xml / .mjcf) is "
    "supported in this version."
)


def simplify_urdf(
    input_path: Path,
    plan: OutputPlan,
    settings: SimplificationSettings,
    **_kwargs: object,
) -> int:
    """Simplify a URDF (not implemented yet - planned future work)."""
    del input_path, plan, settings, _kwargs
    raise NotImplementedError(_MSG)


def decompose_urdf(
    input_path: Path,
    plan: OutputPlan,
    settings: DecompositionSettings,
    **_kwargs: object,
) -> int:
    """Convex-decompose a URDF (not implemented yet - planned future work)."""
    del input_path, plan, settings, _kwargs
    raise NotImplementedError(_MSG)

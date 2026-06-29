"""Post-process a SW2RD-exported robot description."""

from __future__ import annotations

import argparse
import pathlib
import sys

from loguru import logger

from sw2rd_postprocess.mesh_decompose import DecompositionSettings
from sw2rd_postprocess.mesh_simplify import SimplificationSettings
from sw2rd_postprocess.output import resolve_output_plan

MJCF_SUFFIXES = {".xml", ".mjcf"}
URDF_SUFFIXES = {".urdf"}

_SIMPLIFY_FAMILY = {"prepare", "decimate", "prune"}


def _add_output_group(sub: argparse.ArgumentParser) -> None:
    """Add the shared, mutually-exclusive output-destination options."""
    out = sub.add_argument_group(
        "output",
        "Default: write meshes beside the originals (with an operation suffix) "
        "and the XML to <input>.<op>.<ext>. The three options below are mutually "
        "exclusive.",
    )
    excl = out.add_mutually_exclusive_group()
    excl.add_argument(
        "-o",
        "--output-file",
        type=pathlib.Path,
        default=None,
        metavar="PATH",
        help="Write the XML to PATH (meshes stay beside the originals).",
    )
    excl.add_argument(
        "--output-dir",
        type=pathlib.Path,
        default=None,
        metavar="DIR",
        help="Write a self-contained tree (DIR/<name> + DIR/meshes/ with ALL meshes).",
    )
    excl.add_argument(
        "--in-place",
        action="store_true",
        help="Overwrite the original meshes and XML (deletes pruned/orphaned meshes).",
    )


def _add_targeting_group(sub: argparse.ArgumentParser, *, decompose: bool) -> None:
    """Add the shared name/class targeting selectors."""
    grp = sub.add_argument_group("targeting")
    if decompose:
        grp.add_argument(
            "--all",
            dest="all_collision",
            action="store_true",
            help="Target every collision geom (default targets class='collision').",
        )
    else:
        grp.add_argument(
            "--visual", action="store_true", help="Target only visual-class geoms."
        )
        grp.add_argument(
            "--collision",
            action="store_true",
            help="Target only collision-class geoms.",
        )
    grp.add_argument(
        "--geom",
        action="append",
        default=[],
        metavar="NAME",
        help="Target the geom with this exact name (repeatable).",
    )
    grp.add_argument(
        "--mesh",
        action="append",
        default=[],
        metavar="NAME",
        help="Target geoms referencing this mesh asset (repeatable).",
    )
    grp.add_argument(
        "--body",
        action="append",
        default=[],
        metavar="NAME",
        help="Target mesh geoms under this body (repeatable).",
    )
    grp.add_argument(
        "--match",
        default=None,
        metavar="PATTERN",
        help="Target geoms whose name matches this regex or shell glob.",
    )


def _add_max_faces(grp: argparse._ArgumentGroup) -> None:
    grp.add_argument(
        "--max-faces",
        type=int,
        default=SimplificationSettings.max_faces,
        metavar="N",
        help="Per-mesh face cap (default %(default)s, MuJoCo's limit). 0 disables it.",
    )


def _add_qecd_group(sub: argparse.ArgumentParser) -> None:
    """Add the MeshLab QECD tuning knobs (decimate only)."""
    q = sub.add_argument_group("decimation tuning")
    q.add_argument(
        "--quality-threshold",
        type=float,
        default=SimplificationSettings.quality_threshold,
        help="QECD quality threshold, 0-1 (default %(default)s).",
    )
    q.add_argument(
        "--boundary-weight",
        type=float,
        default=SimplificationSettings.boundary_weight,
        help="Weight applied to preserved boundary edges (default %(default)s).",
    )
    q.add_argument(
        "--no-preserve-boundary", dest="preserve_boundary", action="store_false"
    )
    q.add_argument("--no-preserve-normal", dest="preserve_normal", action="store_false")
    q.add_argument(
        "--no-preserve-topology", dest="preserve_topology", action="store_false"
    )
    q.add_argument(
        "--no-optimal-placement", dest="optimal_placement", action="store_false"
    )
    q.add_argument("--no-planar-quadric", dest="planar_quadric", action="store_false")
    q.add_argument("--no-auto-clean", dest="auto_clean", action="store_false")


def _add_input(sub: argparse.ArgumentParser) -> None:
    sub.add_argument(
        "input_file",
        type=pathlib.Path,
        help="Exported robot description to process (.xml/.mjcf; .urdf future).",
    )


def parse_arguments() -> argparse.Namespace:
    """Parse the command-line arguments."""
    parser = argparse.ArgumentParser(
        prog="sw2rd-postprocess",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        description=__doc__,
    )
    subs = parser.add_subparsers(dest="command", required=True, metavar="<operation>")

    # prepare - easy mode
    prep = subs.add_parser(
        "prepare",
        help="Make the model MuJoCo-loadable.",
        description=(
            "Produce a MuJoCo-loadable model by pruning empty meshes and decimating "
            "meshes over the maximum face count."
        ),
    )
    _add_input(prep)
    _add_max_faces(prep.add_argument_group("decimation"))
    _add_output_group(prep)

    # decimate
    dec = subs.add_parser(
        "decimate",
        help="Reduce mesh face counts.",
        description=(
            "Decimate the targeted meshes with MeshLab QECD. Requires exactly one of "
            "--ratio / --target-faces."
        ),
    )
    _add_input(dec)
    mode = dec.add_argument_group("decimation").add_mutually_exclusive_group(
        required=True
    )
    mode.add_argument(
        "--ratio", type=float, metavar="R", help="Keep this fraction of faces, 0-1."
    )
    mode.add_argument(
        "--target-faces", type=int, metavar="N", help="Absolute target face count."
    )
    _add_max_faces(dec.add_argument_group("cap"))
    _add_targeting_group(dec, decompose=False)
    _add_qecd_group(dec)
    _add_output_group(dec)

    # prune
    pru = subs.add_parser(
        "prune",
        help="Remove empty or unreadable meshes.",
        description=(
            "Remove geoms whose mesh is empty or unreadable.  Valid meshes are left "
            "untouched."
        ),
    )
    _add_input(pru)
    _add_targeting_group(pru, decompose=False)
    _add_output_group(pru)

    # decompose
    dcmp = subs.add_parser(
        "decompose",
        help="Replace concave collision meshes with convex-hull unions (CoACD).",
        description=(
            "Convex-decompose targeted collision meshes so a contact-based simulator can "
            "approximate a concave body as a union of convex hulls."
        ),
    )
    _add_input(dcmp)
    coacd = dcmp.add_argument_group("CoACD parameters")
    coacd.add_argument(
        "--threshold",
        type=float,
        default=DecompositionSettings.threshold,
        help="Concavity threshold (meters with real-metric; default %(default)s).",
    )
    coacd.add_argument(
        "--normalized",
        dest="real_metric",
        action="store_false",
        help="Interpret --threshold on CoACD's normalized [0,1] scale.",
    )
    coacd.add_argument(
        "--max-hulls",
        type=int,
        default=DecompositionSettings.max_convex_hull,
        help="Maximum number of convex hulls (-1 = unlimited; default %(default)s).",
    )
    coacd.add_argument(
        "--seed",
        type=int,
        default=DecompositionSettings.seed,
        help="Random seed (default %(default)s).",
    )
    coacd.add_argument(
        "--preprocess-resolution",
        type=int,
        default=DecompositionSettings.preprocess_resolution,
        help="Manifold preprocessing resolution (default %(default)s).",
    )
    coacd.add_argument(
        "--resolution",
        type=int,
        default=DecompositionSettings.resolution,
        help="Sampling resolution for the Hausdorff metric (default %(default)s).",
    )
    coacd.add_argument(
        "--no-merge",
        dest="merge",
        action="store_false",
        help="Disable the post-decomposition merge step.",
    )
    coacd.add_argument("--pca", action="store_true", help="Enable PCA pre-alignment.")
    _add_targeting_group(dcmp, decompose=True)
    _add_output_group(dcmp)

    return parser.parse_args()


def _simplification_settings(args: argparse.Namespace) -> SimplificationSettings:
    """Map a simplify-family subcommand's args onto SimplificationSettings."""
    max_faces = getattr(args, "max_faces", SimplificationSettings.max_faces)
    common = {
        "max_faces": max_faces if max_faces and max_faces > 0 else None,
    }
    if args.command == "prune":
        return SimplificationSettings(prune_only=True, **common)
    if args.command == "decimate":
        return SimplificationSettings(
            ratio=args.ratio,
            target_faces=args.target_faces or 0,
            quality_threshold=args.quality_threshold,
            preserve_boundary=args.preserve_boundary,
            boundary_weight=args.boundary_weight,
            preserve_normal=args.preserve_normal,
            preserve_topology=args.preserve_topology,
            optimal_placement=args.optimal_placement,
            planar_quadric=args.planar_quadric,
            auto_clean=args.auto_clean,
            **common,
        )
    # prepare: cap + prune, default QECD tuning.
    return SimplificationSettings(**common)


def _simplify_targeting(args: argparse.Namespace) -> dict:
    return {
        "visual": getattr(args, "visual", False),
        "collision": getattr(args, "collision", False),
        "geoms": set(getattr(args, "geom", [])),
        "meshes": set(getattr(args, "mesh", [])),
        "bodies": set(getattr(args, "body", [])),
        "match": getattr(args, "match", None),
    }


def _decompose_targeting(args: argparse.Namespace) -> dict:
    return {
        "all_collision": args.all_collision,
        "geoms": set(args.geom),
        "meshes": set(args.mesh),
        "bodies": set(args.body),
        "match": args.match,
    }


def main() -> int:
    """Run the post-processing CLI."""
    args = parse_arguments()

    if not args.input_file.is_file():
        logger.error("Input file not found: {}", args.input_file)
        return 2

    suffix = args.input_file.suffix.lower()
    if suffix not in MJCF_SUFFIXES and suffix not in URDF_SUFFIXES:
        logger.error(
            "Unsupported file type {!r}. Expected one of {}.",
            suffix,
            sorted(MJCF_SUFFIXES | URDF_SUFFIXES),
        )
        return 2

    is_simplify = args.command in _SIMPLIFY_FAMILY
    op_label = "simplified" if is_simplify else "decomposed"

    import mujoco as mj

    meshdir = (
        mj.MjSpec.from_file(str(args.input_file)).meshdir
        if suffix in MJCF_SUFFIXES
        else ""
    )
    plan = resolve_output_plan(
        args.input_file,
        meshdir or "",
        op_label=op_label,
        in_place=args.in_place,
        output_dir=args.output_dir,
        output_file=args.output_file,
    )

    try:
        if is_simplify:
            settings = _simplification_settings(args)
            targeting = _simplify_targeting(args)
            if suffix in MJCF_SUFFIXES:
                from sw2rd_postprocess.mjcf import simplify_mjcf

                simplify_mjcf(args.input_file, plan, settings, **targeting)
            else:
                from sw2rd_postprocess.urdf import simplify_urdf

                simplify_urdf(args.input_file, plan, settings, **targeting)
        else:
            settings = DecompositionSettings(
                threshold=args.threshold,
                max_convex_hull=args.max_hulls,
                real_metric=args.real_metric,
                preprocess_resolution=args.preprocess_resolution,
                resolution=args.resolution,
                merge=args.merge,
                pca=args.pca,
                seed=args.seed,
            )
            targeting = _decompose_targeting(args)
            if suffix in MJCF_SUFFIXES:
                from sw2rd_postprocess.mjcf import decompose_mjcf

                decompose_mjcf(args.input_file, plan, settings, **targeting)
            else:
                from sw2rd_postprocess.urdf import decompose_urdf

                decompose_urdf(args.input_file, plan, settings, **targeting)
    except ModuleNotFoundError as exc:
        logger.error(
            "Missing dependency {!r} for `{}`. Reinstall the tool's dependencies "
            "(e.g. `uv sync`).",
            exc.name,
            args.command,
        )
        return 3
    except NotImplementedError as exc:
        logger.error("{}", exc)
        return 2

    return 0


if __name__ == "__main__":
    sys.exit(main())

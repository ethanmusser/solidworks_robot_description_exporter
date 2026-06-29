"""CLI argument parsing and subcommand -> settings mapping."""

from __future__ import annotations

import pytest

from sw2rd_postprocess.__main__ import _simplification_settings, parse_arguments


def _parse(monkeypatch, argv):
    monkeypatch.setattr("sys.argv", ["sw2rd-postprocess", *argv])
    return parse_arguments()


def test_prepare_maps_to_cap_plus_prune(monkeypatch):
    args = _parse(monkeypatch, ["prepare", "model.xml"])
    assert args.command == "prepare"
    settings = _simplification_settings(args)
    assert settings.prune_only is False
    assert settings.max_faces == 200_000
    assert settings.ratio is None
    assert settings.target_faces == 0


def test_prepare_max_faces_zero_disables_cap(monkeypatch):
    args = _parse(monkeypatch, ["prepare", "model.xml", "--max-faces", "0"])
    assert _simplification_settings(args).max_faces is None


def test_decimate_ratio(monkeypatch):
    args = _parse(monkeypatch, ["decimate", "model.xml", "--ratio", "0.25"])
    settings = _simplification_settings(args)
    assert settings.ratio == 0.25
    assert settings.prune_only is False


def test_decimate_requires_a_mode(monkeypatch):
    with pytest.raises(SystemExit):
        _parse(monkeypatch, ["decimate", "model.xml"])


def test_decimate_modes_are_mutually_exclusive(monkeypatch):
    with pytest.raises(SystemExit):
        _parse(
            monkeypatch,
            ["decimate", "model.xml", "--ratio", "0.5", "--target-faces", "100"],
        )


def test_prune_sets_prune_only(monkeypatch):
    args = _parse(monkeypatch, ["prune", "model.xml"])
    assert _simplification_settings(args).prune_only is True


def test_prune_rejects_decimation_knobs(monkeypatch):
    with pytest.raises(SystemExit):
        _parse(monkeypatch, ["prune", "model.xml", "--ratio", "0.5"])


def test_output_modes_are_mutually_exclusive(monkeypatch):
    with pytest.raises(SystemExit):
        _parse(
            monkeypatch, ["prepare", "model.xml", "--in-place", "--output-dir", "out"]
        )


def test_decompose_parses_coacd_params(monkeypatch):
    args = _parse(
        monkeypatch, ["decompose", "model.xml", "--threshold", "0.02", "--all"]
    )
    assert args.command == "decompose"
    assert args.threshold == 0.02
    assert args.all_collision is True


def test_subcommand_is_required(monkeypatch):
    with pytest.raises(SystemExit):
        _parse(monkeypatch, [])

"""Deliberately fails on Python 3.10 only, to test the merge failure report. Do not merge upstream."""

from __future__ import annotations

import sys

FIRST_PASSING_VERSION = (3, 11)


def test_fails_only_on_python_3_10() -> None:
    assert sys.version_info >= FIRST_PASSING_VERSION, "deliberate failure to test the merge failure report"

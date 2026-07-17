from __future__ import annotations

import sys
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path
from typing import Any, cast


def load_module() -> Any:
    module_path = Path(__file__).with_name("create_large_ruleset.py")
    spec = spec_from_file_location("create_large_ruleset", module_path)
    assert spec is not None
    assert spec.loader is not None
    module = module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return cast("Any", module)


def test_chunks_splits_rows_by_batch_size() -> None:
    module = load_module()
    rows = [{"id": index} for index in range(5)]

    assert module.chunks(rows, 2) == [[{"id": 0}, {"id": 1}], [{"id": 2}, {"id": 3}], [{"id": 4}]]


def test_build_headers_prefers_admin_secret() -> None:
    module = load_module()
    args = type(
        "Args",
        (),
        {
            "admin_secret": "secret",
            "jwt": "jwt",
            "middleware_url": None,
            "user": "admin",
            "password_file": None,
            "timeout": 1,
            "insecure": False,
        },
    )()

    assert module.build_headers(args) == {"Content-Type": "application/json", "x-hasura-admin-secret": "secret"}

from __future__ import annotations

import argparse
from typing import TYPE_CHECKING, Any

import pytest
import requests

if TYPE_CHECKING:
    from types import TracebackType
    from typing import Self

    from _pytest.monkeypatch import MonkeyPatch

from scripts.customizing.provisioning.guardicore.guardicore_lib import (
    FwoConfig,
    extract_label_items,
    login_fwo,
    login_guardicore,
    resolve_ssl_verification_settings,
    run_graphql_query,
)

SAMPLE_FWO_CA_CERT = "/etc/ssl/certs/fwo-ca.pem"
SAMPLE_GUARDICORE_CA_CERT = "/etc/ssl/certs/guardicore-ca.pem"


def test_extract_label_items_reads_objects_list():
    payload = {"objects": [{"key": "AppRole", "value": "Role-A"}]}

    items = extract_label_items(payload)

    assert items == [{"key": "AppRole", "value": "Role-A"}]


def test_extract_label_items_returns_dict_when_no_known_list_key_exists():
    payload = {"key": "AppRole", "value": "Role-A"}

    items = extract_label_items(payload)

    assert items == [payload]


def test_resolve_ssl_verification_settings_prefers_specific_flags():
    args = argparse.Namespace(
        insecure=True,
        fwo_insecure=False,
        guardicore_insecure=False,
        fwo_ca_cert=SAMPLE_FWO_CA_CERT,
        guardicore_ca_cert=SAMPLE_GUARDICORE_CA_CERT,
    )

    fwo_verify, guardicore_verify = resolve_ssl_verification_settings(args)

    assert fwo_verify == SAMPLE_FWO_CA_CERT
    assert guardicore_verify == SAMPLE_GUARDICORE_CA_CERT


def test_resolve_ssl_verification_settings_allows_endpoint_specific_insecure():
    args = argparse.Namespace(
        insecure=False,
        fwo_insecure=True,
        guardicore_insecure=True,
        fwo_ca_cert=SAMPLE_FWO_CA_CERT,
        guardicore_ca_cert=SAMPLE_GUARDICORE_CA_CERT,
    )

    fwo_verify, guardicore_verify = resolve_ssl_verification_settings(args)

    assert fwo_verify is False
    assert guardicore_verify is False


def test_login_fwo_returns_jwt_and_rejects_non_ok_response(monkeypatch: MonkeyPatch):
    class FakeResponse:
        status_code = 200
        text = "jwt"

    class FakeSession:
        def __init__(self) -> None:
            self.verify = True

        def __enter__(self) -> Self:
            return self

        def __exit__(
            self,
            exc_type: type[BaseException] | None,
            exc: BaseException | None,
            tb: TracebackType | None,
        ) -> None:
            return None

        def post(self, endpoint: str, json: dict[str, Any], headers: dict[str, str], timeout: int) -> FakeResponse:
            del endpoint, json, headers, timeout
            return FakeResponse()

    monkeypatch.setattr(requests, "Session", FakeSession)

    assert login_fwo("user", "secret", "https://fwo", True, 10, RuntimeError) == "jwt"

    FakeResponse.status_code = 401
    FakeResponse.text = "denied"
    with pytest.raises(RuntimeError, match="status 401"):
        login_fwo("user", "secret", "https://fwo", True, 10, RuntimeError)


def test_login_guardicore_accepts_supported_token_keys(monkeypatch: MonkeyPatch):
    class FakeResponse:
        def raise_for_status(self) -> None:
            return None

        def json(self) -> dict[str, Any]:
            return {"accessToken": "gc-token"}

    class FakeSession:
        def __init__(self) -> None:
            self.verify = True

        def __enter__(self) -> Self:
            return self

        def __exit__(
            self,
            exc_type: type[BaseException] | None,
            exc: BaseException | None,
            tb: TracebackType | None,
        ) -> None:
            return None

        def post(self, endpoint: str, json: dict[str, Any], headers: dict[str, str], timeout: int) -> FakeResponse:
            del endpoint, json, headers, timeout
            return FakeResponse()

    monkeypatch.setattr(requests, "Session", FakeSession)

    assert login_guardicore("user", "secret", "https://gc", True, 10, RuntimeError) == "gc-token"


def test_login_guardicore_rejects_invalid_json_and_missing_token(monkeypatch: MonkeyPatch):
    class FakeResponse:
        payload: dict[str, Any] | None = None

        def raise_for_status(self) -> None:
            return None

        def json(self) -> dict[str, Any]:
            if self.payload is None:
                raise ValueError("invalid")
            return self.payload

    class FakeSession:
        def __init__(self) -> None:
            self.verify = True

        def __enter__(self) -> Self:
            return self

        def __exit__(
            self,
            exc_type: type[BaseException] | None,
            exc: BaseException | None,
            tb: TracebackType | None,
        ) -> None:
            return None

        def post(self, endpoint: str, json: dict[str, Any], headers: dict[str, str], timeout: int) -> FakeResponse:
            del endpoint, json, headers, timeout
            return FakeResponse()

    monkeypatch.setattr(requests, "Session", FakeSession)

    with pytest.raises(RuntimeError, match="not valid JSON"):
        login_guardicore("user", "secret", "https://gc", True, 10, RuntimeError)

    FakeResponse.payload = {"token": ""}
    with pytest.raises(RuntimeError, match="did not include a token"):
        login_guardicore("user", "secret", "https://gc", True, 10, RuntimeError)


def test_run_graphql_query_removes_line_breaks_in_payload(monkeypatch: MonkeyPatch):
    captured_payload: dict[str, Any] = {}

    class FakeResponse:
        def raise_for_status(self) -> None:
            return None

        def json(self) -> dict[str, Any]:
            return {"data": {"ok": True}}

    class FakeSession:
        def __init__(self) -> None:
            self.headers: dict[str, Any] = {}
            self.verify = True

        def __enter__(self) -> Self:
            return self

        def __exit__(
            self,
            exc_type: type[BaseException] | None,
            exc: BaseException | None,
            tb: TracebackType | None,
        ) -> None:
            return None

        def post(self, url: str, json: dict[str, Any], timeout: int) -> FakeResponse:
            del url, timeout
            captured_payload.update(json)
            return FakeResponse()

    monkeypatch.setattr(requests, "Session", FakeSession)

    config = FwoConfig(
        graphql_url="https://fwo/graphql",
        jwt="jwt",
        verify_ssl=True,
        timeout_seconds=10,
        role="reporter",
    )
    query = "query Test {\\n  owner {\\n    id\\n  }\\n}"
    result = run_graphql_query(config, query, {"x": 1}, RuntimeError)

    assert result == {"data": {"ok": True}}
    assert "\n" not in captured_payload["query"]

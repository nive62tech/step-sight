from uuid import uuid4

import pytest
from app.remote import RemoteBroker, RemoteCommandRequest, RemotePoll, RemoteRegistration, RemoteStatus
from fastapi import HTTPException


def register(client, autonomous=False, owner="alice"):
    reply = client.post("/v1/remote/connections", headers={"x-dev-user": owner}, json={
        "device_id": str(uuid4()), "label": "Windows PC", "allow_autonomous": autonomous})
    assert reply.status_code == 201, reply.text
    lease = reply.json()
    return lease, f"/v1/remote/connections/{lease['connection_id']}"


def test_account_isolation_desktop_key_and_prompt_not_exposed(client):
    lease, url = register(client)
    request = {"request_id": str(uuid4()), "kind": "start", "mode": "guide", "goal": "A private task"}
    assert client.post(url + "/commands", json=request, headers={"x-dev-user": "bob"}).status_code == 404
    assert client.get("/v1/remote/connections", headers={"x-dev-user": "bob"}).json()["devices"] == []
    assert client.post(url + "/commands", json=request, headers={"x-dev-user": "alice"}).status_code == 200
    listed = client.get("/v1/remote/connections", headers={"x-dev-user": "alice"})
    assert "private task" not in listed.text and lease["desktop_key"] not in listed.text
    for headers, expected in [({"x-dev-user": "alice"}, 403),
                              ({"x-dev-user": "bob", "x-desktop-lease": lease["desktop_key"]}, 404)]:
        assert client.post(url + "/poll", json={"status": {}}, headers=headers).status_code == expected
    headers = {"x-dev-user": "alice", "x-desktop-lease": lease["desktop_key"]}
    delivered = client.post(url + "/poll", json={"status": {}}, headers=headers).json()["command"]
    assert delivered["goal"] == request["goal"]
    assert client.post(url + "/poll", json={"status": {}}, headers=headers).json()["command"] == delivered
    ack = {"command_id": delivered["id"], "accepted": True, "message": "Task received"}
    assert client.post(url + "/poll", json={"status": {"state": "Observing"}, "acknowledgement": ack}, headers=headers).json()["command"] is None
    assert client.get("/v1/remote/connections", headers={"x-dev-user": "alice"}).json()["devices"][0]["last_command"]["status"] == "accepted"


def test_local_autonomous_delegation_and_immutable_idempotency(client):
    lease, url = register(client)
    headers = {"x-dev-user": "alice"}
    request = {"request_id": str(uuid4()), "kind": "start", "mode": "autonomous", "goal": "Find trains"}
    assert client.post(url + "/commands", json=request, headers=headers).status_code == 403
    request["mode"] = "guide"
    first = client.post(url + "/commands", json=request, headers=headers).json()
    assert client.post(url + "/commands", json=request, headers=headers).json() == first
    assert client.post(url + "/commands", json=request | {"goal": "Different task"}, headers=headers).status_code == 409
    assert client.post(url + "/commands", json={"request_id": str(uuid4()), "kind": "resume", "mode": "autonomous"}, headers=headers).status_code == 422
    assert client.delete(url, headers=headers).status_code == 200
    assert client.post(url + "/poll", json={"status": {}}, headers=headers | {"x-desktop-lease": lease["desktop_key"]}).status_code == 410


def test_stop_supersedes_queued_task_and_resume_retains_autonomous_status(client):
    lease, url = register(client, autonomous=True)
    headers = {"x-dev-user": "alice"}
    first = {"request_id": str(uuid4()), "kind": "start", "mode": "autonomous", "goal": "Find trains"}
    assert client.post(url + "/commands", json=first, headers=headers).status_code == 200
    stop = {"request_id": str(uuid4()), "kind": "stop"}
    assert client.post(url + "/commands", json=stop, headers=headers).status_code == 200
    assert client.post(url + "/commands", json=first, headers=headers).json()["status"] == "cancelled"
    desktop = headers | {"x-desktop-lease": lease["desktop_key"]}
    assert client.post(url + "/poll", json={"status": {}}, headers=desktop).json()["command"]["kind"] == "stop"
    client.post(url + "/poll", headers=desktop, json={"status": {"state": "Paused", "mode": "autonomous", "can_resume": True},
        "acknowledgement": {"command_id": stop["request_id"], "accepted": True}})
    assert client.post(url + "/commands", headers=headers, json={"request_id": str(uuid4()), "kind": "resume"}).status_code == 200
    assert client.get("/v1/remote/connections", headers=headers).json()["devices"][0]["status"]["mode"] == "autonomous"


def test_offline_timeout_and_reconnection_never_replay_commands():
    stamp = [1.0]
    broker = RemoteBroker(clock=lambda: stamp[0])
    registration = RemoteRegistration(device_id=uuid4(), label="PC")
    lease = broker.register("alice", registration)
    request = RemoteCommandRequest(request_id=uuid4(), kind="start", mode="guide", goal="Task")
    broker.submit("alice", lease.connection_id, request)
    stamp[0] += 21
    assert not broker.list_for("alice")["devices"][0]["online"]
    assert broker.list_for("alice")["devices"][0]["last_command"].status == "cancelled"
    with pytest.raises(HTTPException) as failure:
        broker.submit("alice", lease.connection_id, request)
    assert failure.value.status_code == 409
    replacement = broker.register("alice", registration)
    assert replacement.connection_id != lease.connection_id
    assert broker.poll("alice", replacement.connection_id, replacement.desktop_key, RemotePoll(status=RemoteStatus())).command is None


def test_unacknowledged_command_expires_even_while_pc_stays_online():
    stamp = [1.0]
    broker = RemoteBroker(clock=lambda: stamp[0])
    lease = broker.register("alice", RemoteRegistration(device_id=uuid4(), label="PC"))
    broker.submit("alice", lease.connection_id, RemoteCommandRequest(request_id=uuid4(), kind="start", mode="guide", goal="Task"))
    for stamp[0] in [11, 21, 32]:
        result = broker.poll("alice", lease.connection_id, lease.desktop_key, RemotePoll(status=RemoteStatus()))
    assert result.command is None
    assert broker.list_for("alice")["devices"][0]["last_command"].status == "expired"


def test_control_assets_and_config_are_public_and_hardened(client):
    page = client.get("/control/")
    assert page.status_code == 200 and "Your desktop" in page.text
    assert "frame-ancestors 'none'" in page.headers["content-security-policy"]
    assert "no-store" in page.headers["cache-control"]
    assert client.get("/control/config.json").json()["configured"] is False
    assert client.get("/control/auth.js").status_code == 200
    assert client.get("/control/private.env").status_code == 404

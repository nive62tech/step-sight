from uuid import uuid4

from app.contracts import ActionProposal, Evidence, GuidanceStep, ProviderResult, Target, VerificationResult
from app.providers import Usage
from conftest import create_session, observation, send_turn
from fastapi.testclient import TestClient
from test_autonomous import AutonomousProvider, app_for


class MultiAppProvider(AutonomousProvider):
    def __init__(self):
        super().__init__()
        self.calls = []

    async def guide(self, goal, lesson, turn, image, *, repair=False):
        self.calls.append((goal, lesson))
        obs = turn.observation
        index = len(self.calls) - 1
        instructions = ["Create a meeting for later", "Copy meeting link", "Open WhatsApp Desktop", "Search for Sample Contact"]
        kinds = ["click", "click", "open_app", "type"]
        complete = index == 4
        evidence = [Evidence(observation_id=obs.id, detail="Visible synthetic outcome")]
        verification = VerificationResult(status="CONFIRMED", evidence=evidence, provenance="mock") if turn.previous_step else None
        if complete:
            return ProviderResult(step=None, verification=verification, completed=True), Usage(100, 20)
        action = {"kind": kinds[index]}
        if kinds[index] == "click":
            action |= {"x": .2, "y": .2}
        elif kinds[index] == "open_app":
            action["application"] = "whatsapp"
        else:
            action["text"] = "Sample Contact"
        step = GuidanceStep(id=uuid4(), source_observation_id=obs.id, instruction=instructions[index], explanation="Next subgoal",
            expected_user_action=kinds[index], postconditions=[{"kind": "visible_state", "value": instructions[index]+" complete"}],
            evidence=evidence)
        if kinds[index] != "open_app":
            step.target = Target(geometry_id=obs.geometry_id,
                bounds={"left": .1, "top": .1, "right": .3, "bottom": .3}, visible_label="Search")
        proposal = ActionProposal(id=uuid4(), source_observation_id=obs.id, geometry_id=obs.geometry_id,
            action=action, effect=instructions[index], risk="low")
        return ProviderResult(step=step, verification=verification, action_proposal=proposal), Usage(100, 20)


def test_meeting_copy_history_and_same_goal_survive_whatsapp_scope_handoff():
    provider = MultiAppProvider()
    app = app_for(provider)
    with TestClient(app) as client:
        goal = "Create a meeting, copy its link, then open WhatsApp Desktop and search for Sample Contact"
        session = create_session(client, mode="autonomous", lesson_id="browser", goal=goal)
        original_scope = session["scope_id"]
        pending = None
        for index in range(5):
            task = {"current_application": "whatsapp" if index >= 3 else "starting_window", "allowed_applications": ["whatsapp"]}
            event = "start" if index == 0 else "autonomous_result"
            if index == 3:
                task["handoff_from_scope_id"] = original_scope
                session["scope_id"] = str(uuid4())
                event = "app_handoff"
            metadata, image = observation(session, event_kind=event, previous_step=pending, task_context=task)
            response = send_turn(client, session, metadata, image)
            assert response.status_code == 200, response.text
            result = response.json()
            session["session_version"] = result["session_version"]
            pending = result["step"]
        assert result["completed"]
        assert all(call[0] == goal for call in provider.calls)
        history = provider.calls[3][1]["verified_history"]
        assert [item["instruction"] for item in history] == ["Create a meeting for later", "Copy meeting link"]
        assert client.get(f"/v1/sessions/{session['id']}").json()["scope_id"] == session["scope_id"]
        # No ephemeral model instructions were saved in session/step records.
        from app.persistence import Session
        with app.state.db_factory() as db:
            assert db.get(Session, session["id"]).pending_step is None


def test_scope_cannot_change_without_pending_app_handoff():
    with TestClient(app_for(MultiAppProvider())) as client:
        session = create_session(client, mode="autonomous", lesson_id="browser", goal="Open WhatsApp")
        metadata, image = observation(session)
        metadata["observation"]["scope_id"] = str(uuid4())
        metadata["event_kind"] = "app_handoff"
        metadata["task_context"] = {"current_application": "whatsapp", "allowed_applications": ["whatsapp"], "handoff_from_scope_id": session["scope_id"]}
        assert send_turn(client, session, metadata, image).status_code == 409


def test_open_app_is_not_proposed_to_older_client_or_unrelated_goal():
    for goal, task in [("Create a meeting", {"current_application": "starting_window", "allowed_applications": ["whatsapp"]}),
                       ("Create a meeting then open WhatsApp", None)]:
        provider = MultiAppProvider()
        with TestClient(app_for(provider)) as client:
            session = create_session(client, mode="autonomous", lesson_id="browser", goal=goal)
            pending = None
            for _ in range(2):
                metadata, image = observation(session, previous_step=pending, task_context=task)
                result = send_turn(client, session, metadata, image).json()
                session["session_version"] = result["session_version"]
                pending = result["step"]
            metadata, image = observation(session, previous_step=pending, task_context=task)
            assert send_turn(client, session, metadata, image).status_code == 502

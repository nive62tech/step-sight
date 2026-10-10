import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3] / "lessons"


class LessonRepository:
    def __init__(self, root: Path = ROOT):
        self.lessons = {}
        for path in sorted(root.glob("*/lesson.json")):
            content = json.loads(path.read_text(encoding="utf-8"))
            self.lessons[content["id"]] = content

    def get(self, identifier: str):
        return self.lessons.get(identifier)

    def list(self):
        return [{key: lesson[key] for key in ("id", "title", "version", "qualification", "prerequisites")}
                for lesson in self.lessons.values()]

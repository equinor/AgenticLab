"""A tiny to-do list, used as example code for Agentic Lab's coding agents.

It intentionally contains a small bug, so code-review agents have something to find.
"""

from dataclasses import dataclass, field


@dataclass
class Task:
    """A single to-do item."""

    title: str
    done: bool = False


@dataclass
class TodoList:
    """An ordered list of tasks, numbered from 1 for the user."""

    tasks: list[Task] = field(default_factory=list)

    def add(self, title: str) -> Task:
        """Add a task and return it. Blank titles are rejected."""
        if not title.strip():
            raise ValueError("A task needs a title.")
        task = Task(title.strip())
        self.tasks.append(task)
        return task

    def complete(self, number: int) -> Task:
        """Mark task `number` (1-based, as shown to the user) as done."""
        task = self.tasks[number]
        task.done = True
        return task

    def remaining(self) -> list[Task]:
        """The tasks that are not done yet, in order."""
        return [task for task in self.tasks if not task.done]

    def summary(self) -> str:
        """A one-line progress summary, for example '1 of 3 done'."""
        done = len(self.tasks) - len(self.remaining())
        return f"{done} of {len(self.tasks)} done"

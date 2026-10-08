"""Tests for the example to-do list. Note what they don't cover."""

import pytest

from src.todo import TodoList


def test_add_strips_title():
    todos = TodoList()
    assert todos.add("  Buy milk ").title == "Buy milk"


def test_add_rejects_blank_title():
    with pytest.raises(ValueError):
        TodoList().add("   ")


def test_summary_counts_done_tasks():
    todos = TodoList()
    todos.add("First")
    todos.add("Second")
    todos.tasks[0].done = True
    assert todos.summary() == "1 of 2 done"

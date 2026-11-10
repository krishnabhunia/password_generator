#!/usr/bin/env python3
"""Run a command and, if it fails, show the end of its output as a GitHub error annotation.

Usage: python annotate.py "Step name" -- command arg1 arg2 ...

Job logs are not always readable from outside GitHub, annotations are, so every
step that can fail goes through this.
"""
import subprocess
import sys

KEEP_LINES = 40


def escape(text: str) -> str:
    return text.replace("%", "%25").replace("\r", "").replace("\n", "%0A")


def main(argv: list[str]) -> int:
    if "--" not in argv or argv.index("--") < 1:
        print("usage: annotate.py <step name> -- <command...>", file=sys.stderr)
        return 2
    split = argv.index("--")
    title, command = " ".join(argv[:split]), argv[split + 1:]
    tail: list[str] = []
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="replace")
    assert process.stdout is not None
    for line in process.stdout:
        sys.stdout.write(line)
        tail.append(line.rstrip("\n"))
        del tail[:-KEEP_LINES]
    code = process.wait()
    if code != 0:
        print(f"::error title={title} failed::{escape(chr(10).join(tail))}")
    return code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

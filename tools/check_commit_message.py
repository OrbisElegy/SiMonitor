#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Check the subset of Linux kernel commit-message rules used by this repo."""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path


SUBJECT = re.compile(r"^[a-z0-9][a-z0-9_.-]*: [a-z0-9].+")
IDENTITY = re.compile(r"^(.+ <[^<>\s]+@[^<>\s]+>) \d+ [+-]\d{4}$")
COAUTHOR = re.compile(r"^Co-authored-by: [^<>\n]+ <[^<>\s]+@[^<>\s]+>$")


def validate(message: str, author: str) -> list[str]:
    lines = message.splitlines()
    subject = lines[0] if lines else ""
    errors: list[str] = []
    if not SUBJECT.fullmatch(subject):
        errors.append("subject must use lowercase 'subsystem: imperative summary'")
    if len(subject) > 75:
        errors.append("subject exceeds 75 characters")
    if subject.endswith("."):
        errors.append("subject must not end with a period")
    if len(lines) > 1 and lines[1] != "":
        errors.append("subject must be followed by a blank line")
    for number, line in enumerate(lines[2:], start=3):
        if len(line) > 75 and not re.match(r"^(https?://|\S+-by:|Fixes:)", line):
            errors.append(f"line {number} exceeds 75 characters")

    # Only the final paragraph can contain contribution trailers.
    paragraphs = re.split(r"\n\s*\n", message.strip())
    trailers = paragraphs[-1].splitlines() if len(paragraphs) > 1 else []
    signoffs = [line for line in lines if line.lower().startswith('signed-off-by:')]
    expected = f'Signed-off-by: {author}'
    if signoffs != [expected] or expected not in trailers:
        errors.append('exactly one Signed-off-by trailer must match the Git author name and email')
    if author.endswith('@localhost>'):
        errors.append('Git author must be a human contributor, not a tool identity')
    coauthors = [line for line in lines if line.lower().startswith(('co-authored-by:', 'co-author:'))]
    for line in coauthors:
        if not COAUTHOR.fullmatch(line) or line not in trailers:
            errors.append('use a final Co-authored-by: MODEL_NAME <tool@localhost> trailer for tool assistance')
    if len(set(coauthors)) != len(coauthors):
        errors.append('Co-authored-by trailers must not be duplicated')
    return errors


def main() -> int:
    message_path = Path(sys.argv[1])
    identity = subprocess.check_output(['git', 'var', 'GIT_AUTHOR_IDENT'], text=True).strip()
    match = IDENTITY.fullmatch(identity)
    if match is None:
        print('error: could not resolve the Git author identity', file=sys.stderr)
        return 1
    errors = validate(message_path.read_text(encoding="utf-8"), match[1])

    for error in errors:
        print(f"error: {error}", file=sys.stderr)
    return int(bool(errors))


if __name__ == "__main__":
    raise SystemExit(main())

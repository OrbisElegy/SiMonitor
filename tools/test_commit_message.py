# SPDX-License-Identifier: AGPL-3.0-or-later
"""Regression checks for human authorship and tool attribution trailers."""
import os
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile
import unittest

from check_commit_message import validate


AUTHOR = 'Example Contributor <contributor@example.com>'
BODY = 'build: document contribution rules\n\nExplain the contribution requirements.\n\n'
SIGNOFF = f'Signed-off-by: {AUTHOR}\n'
COAUTHOR = 'Co-authored-by: gpt-6-astra <codex@localhost>\n'


class CommitMessageTests(unittest.TestCase):
    def test_human_and_assisted_contributions(self):
        self.assertEqual(validate(BODY + SIGNOFF, AUTHOR), [])
        self.assertEqual(validate(BODY + COAUTHOR + SIGNOFF, AUTHOR), [])
        self.assertEqual(validate(BODY + COAUTHOR.replace('gpt-6-astra', 'gpt-5.6-sol') + SIGNOFF, AUTHOR), [])

    def test_missing_or_mismatched_signoff(self):
        for trailer in ('', 'Signed-off-by: GPT-6\n',
                        'Signed-off-by: ExampleContributor <contributor@example.com>\n',
                        'Signed-off-by: Example Contributor <other@example.org>\n', SIGNOFF * 2):
            with self.subTest(trailer=trailer):
                self.assertTrue(validate(BODY + trailer, AUTHOR))

    def test_tool_cannot_replace_human_author(self):
        author = 'Codex <codex@localhost>'
        self.assertTrue(validate(BODY + f'Signed-off-by: {author}\n', author))

    def test_trailers_must_be_in_final_paragraph(self):
        self.assertTrue(validate(BODY + SIGNOFF + '\nExtra body paragraph.\n', AUTHOR))
        self.assertTrue(validate(BODY + COAUTHOR + '\n' + SIGNOFF, AUTHOR))

    def test_malformed_or_duplicate_coauthor(self):
        for trailer in ('Co-author: gpt-6-astra <codex@localhost>\n',
                        'Co-authored-by: gpt-6-astra\n', COAUTHOR * 2):
            with self.subTest(trailer=trailer):
                self.assertTrue(validate(BODY + trailer + SIGNOFF, AUTHOR))

    def test_other_real_contributor_identity(self):
        author = 'Other Contributor <contributor@example.org>'
        self.assertEqual(validate(BODY + f'Signed-off-by: {author}\n', author), [])


class CommitHookIntegrationTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.repo = Path(directory.name)
        self.env = {key: value for key, value in os.environ.items()
                    if not key.startswith('GIT_')}
        self.env.update(GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL=os.devnull)
        self.git('init', '--quiet')
        self.git('config', 'user.name', 'Example Contributor')
        self.git('config', 'user.email', 'contributor@example.com')
        self.git('config', 'commit.gpgsign', 'false')
        hooks = self.repo / 'hooks'
        hooks.mkdir()
        checker = Path(__file__).resolve().with_name('check_commit_message.py')
        hook = hooks / 'commit-msg'
        hook.write_text('#!/bin/sh\nexec ' + shlex.join([sys.executable, str(checker)])
                        + ' "$1"\n', encoding='utf-8')
        hook.chmod(0o755)
        self.git('config', 'core.hooksPath', str(hooks))
        editor = self.repo / 'editor.py'
        editor.write_text(
            'import sys\nfrom pathlib import Path\n'
            'path = Path(sys.argv[1])\n'
            'path.write_text("build: check commit hook\\n" + path.read_text(encoding="utf-8"), encoding="utf-8")\n',
            encoding='utf-8')
        self.env['GIT_EDITOR'] = shlex.join([sys.executable, str(editor)])

    def git(self, *args, check=True):
        return subprocess.run(['git', *args], cwd=self.repo, env=self.env,
                              encoding='utf-8', capture_output=True, check=check)

    def assert_unassisted_commit(self):
        message = self.git('log', '-1', '--format=%B').stdout
        self.assertIn(SIGNOFF.strip(), message)
        self.assertNotIn('Co-authored-by:', message)
        self.assertEqual(self.git('log', '-1', '--format=%an <%ae>').stdout.strip(), AUTHOR)

    def test_editor_comments_do_not_hide_signoff(self):
        self.git('commit', '--allow-empty', '--author=' + AUTHOR, '-s')
        self.assert_unassisted_commit()

    def test_configured_comment_character(self):
        self.git('config', 'core.commentChar', ';')
        self.git('commit', '--allow-empty', '-s')
        self.assert_unassisted_commit()

    def test_command_line_message_without_coauthor(self):
        self.git('commit', '--allow-empty', '-s', '-m', 'build: check commit hook')
        self.assert_unassisted_commit()

    def test_author_override_does_not_change_signoff_identity(self):
        author = 'ExampleContributor <contributor@example.com>'
        result = self.git('commit', '--allow-empty', '--author=' + author,
                          '-s', '-m', 'build: check commit hook', check=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Signed-off-by: ' + author, result.stderr)
        self.assertIn(SIGNOFF.strip(), result.stderr)
        self.assertNotEqual(self.git('rev-parse', '--verify', 'HEAD', check=False).returncode, 0)


if __name__ == '__main__':
    unittest.main()

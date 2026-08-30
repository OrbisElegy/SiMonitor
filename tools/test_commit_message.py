# SPDX-License-Identifier: AGPL-3.0-or-later
"""Regression checks for human authorship and tool attribution trailers."""
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


if __name__ == '__main__':
    unittest.main()

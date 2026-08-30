# Contributing

Project contributions follow AGPL-3.0-or-later. Preserve third-party
ownership and notices; see `LICENSE`, `docs/license-scope.md`, and
`eng/licenses/`.

This software is a teaching simulator, not a clinical device. Keep
deterministic authority and simulation code independent of UI, storage,
platform APIs, wall-clock time, and nondeterministic random or iteration order.
Validate complete inputs before publishing state.

## File license identifiers

Project-authored source code, tests, scripts, and build definitions carry
`SPDX-License-Identifier: AGPL-3.0-or-later` in a comment near the top,
after any shebang, Python encoding declaration, or XML declaration. Use the
file format's comment syntax. Preserve existing copyright and third-party
attribution notices.

This policy documents the license of project code. It does not extend that
license to README files, other documentation, JSON metadata, PCM assets,
or repository settings. Do not add headers or sidecar licenses to these
files without a separate licensing decision. License texts, bundled
third-party notices, and vendored upstream files remain byte-identical.

Generated Infirmary-derived C# tables retain Apache-2.0 terms alongside
project code, expressed as `AGPL-3.0-or-later AND Apache-2.0`. Preserve the
existing attribution and dependency-ledger entries. Review new third-party
source separately instead of applying the project identifier blindly.

Run `python3 tools/spdx_headers.py` to add headers within this scope, then
`python3 tools/spdx_headers.py --check` to verify coverage. Table generators
must reproduce their SPDX headers so regeneration keeps the annotations.

Use an imperative commit subject prefixed by the affected subsystem. Keep
the subject concise and explain the reason and observable behavior in the
body. Keep refactors separate from behavior changes.

## Commit authorship and signatures

Use the responsible human contributor's real name and email for the Git
author and committer. Replace the example identity below with your own:

```sh
git config user.name "Your Name"
git config user.email "you@example.com"
git config core.hooksPath .githooks
```

Do not use a coding tool or model as the Git author or committer.

For a contribution assisted by a coding tool, add a standard
`Co-authored-by: MODEL_NAME <tool@localhost>` trailer. Record the actual
model identifier and tool name; for example, Codex with gpt-6-astra uses:

```text
Co-authored-by: gpt-6-astra <codex@localhost>
Signed-off-by: Your Name <you@example.com>
```

Codex contributions made with gpt-5.6-sol use
`Co-authored-by: gpt-5.6-sol <codex@localhost>` instead. Add a trailer for
each participating model/tool combination. Do not infer model identity
from the tool name or add tool attribution to an unassisted contribution.

The `Signed-off-by` name and email must exactly match the human author.
Use `git commit -s` to add this trailer. The sign-off trailer is not a
cryptographic signature.

GPG signing is optional and is a contributor's personal choice. To enable
OpenPGP signing locally, replace the key placeholder with your own key ID:

```sh
git config gpg.format openpgp
git config user.signingkey YOUR_GPG_KEY_ID
git config commit.gpgsign true
```

Use `git commit -S -s` when signing a commit. Amending or rewriting a signed
commit requires a new signature if you want the replacement to remain
signed. Verify signatures with `git verify-commit HEAD` or
`git log --show-signature`.

When the signing key is on a smart card, keep the card connected and enter
its PIN only through the local GPG pinentry prompt. Never put a PIN in a
commit message, command argument, repository file, or chat.

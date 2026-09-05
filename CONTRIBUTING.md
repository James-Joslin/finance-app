# Contributing to Finova

Thank you for considering a contribution. Finova handles sensitive household-finance workflows, so changes should favour correctness, privacy, recoverability, and clear behaviour.

By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md). By submitting a contribution, you agree that it may be distributed under the [GNU Affero General Public License v3.0](LICENSE).

## Before you start

- Search existing issues and pull requests before opening a duplicate.
- Use a bug report for reproducible faults and a feature request for proposed behaviour.
- Discuss large features, database redesigns, new external services, or breaking changes in an issue before implementation.
- Report vulnerabilities according to [SECURITY.md](SECURITY.md), not in a public issue.
- Never submit real financial records, credentials, environment files, database dumps, backup archives, or private household images.

## Development setup

Copy the development environment template and start the Docker Compose stack:

~~~sh
cp .env.dev.example .env.dev
docker compose --env-file .env.dev -f compose.dev.yml up --build
~~~

The complete development and migration workflow is documented in the [README](README.md#contributing).

## Making changes

1. Create a focused branch from the latest main branch.
2. Keep changes narrowly scoped and avoid unrelated formatting or dependency updates.
3. Add or update tests for changed behaviour.
4. Update documentation when commands, configuration, APIs, or user-facing behaviour change.
5. Use synthetic data in tests and examples.

Database changes must be implemented through a new Alembic revision. Test both upgrade and downgrade paths against a disposable database. If a downgrade cannot safely preserve data, explain that limitation prominently in the pull request.

Dependency and runtime major-version upgrades should be proposed separately, include relevant migration notes, and update all coupled versions together. Examples include the .NET SDK/runtime/target framework, Node.js build image and CI version, PostgreSQL server and backup tooling, and Playwright package and container image.

## Quality checks

Run the complete local release suite:

~~~sh
./scripts/check-all.sh
~~~

For documentation-only changes, run the checks relevant to the files changed and explain in the pull request why the complete suite was unnecessary.

Before submitting, confirm that:

- Formatting, tests, analyzers, audits, container builds, migrations, and security checks pass.
- Browser and backup/restore tests clean up their disposable containers, volumes, databases, and blobs.
- No generated files, local environment files, credentials, or real household data are included.
- Production container references remain digest-pinned.

## Pull requests

Describe the problem, the chosen solution, testing performed, operational or migration impact, and any follow-up work. Screenshots are welcome for visual changes, but must contain synthetic data.

All required GitHub checks, including CI gate, must pass before merge. A passing check does not replace review for destructive data operations, security-sensitive changes, or major dependency upgrades.

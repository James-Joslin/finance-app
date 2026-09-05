# Security Policy

## Supported versions

Finova currently supports only the latest successful release from the main branch.

| Version | Supported |
| --- | --- |
| Latest main / GHCR latest | Yes |
| Older sha-* images and source revisions | No |

Pinned sha-* images remain available for rollback, but security fixes are provided only in the newest release.

## Reporting a vulnerability

Do not disclose vulnerabilities in a public issue, discussion, pull request, or Actions log.

Use [GitHub private vulnerability reporting](https://github.com/James-Joslin/finance-app/security/advisories/new). If that option is unavailable, open a minimal public issue requesting a private contact channel without including vulnerability details.

Include, where possible:

- The affected version or image tag.
- A description of the impact and affected component.
- Reproduction steps using synthetic data.
- Any suggested mitigation.

Never include passwords, tokens, connection strings, real financial records, database dumps, backup archives, or private household images.

Reports are reviewed on a best-effort basis. There is currently no paid bug-bounty programme or guaranteed response time. Please allow a reasonable period for investigation and remediation before public disclosure.

## Deployment boundary

Finova is intentionally login-free and HTTP-only. It is designed for a trusted private network behind appropriate firewall, VPN, or authenticated TLS-proxy controls. Merely observing the absence of application authentication or TLS is not a vulnerability; a way to bypass the documented deployment boundary or access data contrary to it may be.

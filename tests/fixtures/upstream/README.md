# Sanitized upstream contract fixtures

These fixtures preserve public response shapes without credentials, private
paths, device identifiers, or full command output. They cover accepted,
partial, malformed, unknown-field, and rate-limit contracts used by release and
hardware parsing tests.

Refresh a fixture only through a reviewed pull request. Remove request headers,
tokens, usernames, serial numbers, host paths, and unrelated response data;
retain the upstream field names and representative value types needed by the
parser test.

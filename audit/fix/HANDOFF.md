# Handoff between agents

Each agent writes ONLY in its own section: requests for files it does not own, decisions the others must know, and its final summary.
Format for a request: `- [ ] <file> — <what to change> — <why / issue id>`.

## Agent A (API config, auth) — branch `fix/a-api-config`

### Requests

### Notes for B / C

### Summary

## Agent B (API controllers, data, migrations) — branch `fix/b-api-data`

### Requests

### Final DTO / error changes for C

### Summary

## Agent C (WPF client) — branch `fix/c-wpf`

### Requests

### Summary
